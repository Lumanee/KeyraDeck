using System.Diagnostics;
using System.IO.Compression;

namespace PhoneDeckPC;

/// <summary>
/// Connexion de secours par câble USB. L'outil Android « adb » relie le port du téléphone à celui du PC
/// (adb reverse) : l'appli du téléphone se connecte alors à 127.0.0.1 et passe par le câble, sans Wi-Fi.
/// Le PC surveille les téléphones branchés, active le tunnel tout seul et peut ouvrir l'appli sur le téléphone.
/// </summary>
public class UsbBridge
{
    private const string ToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private const string ApkUrl = "https://github.com/Lumanee/KeyraDeck/releases/download/derniere-version/KeyraDeck.apk";
    private const string AppActivity = "com.phonedeck.app/.MainActivity";

    private static readonly HttpClient Http = CreateClient();

    private readonly ConfigStore _store;
    private readonly DeckServer _server;
    /// <summary>Téléphones dont le tunnel est déjà en place (numéro de série adb).</summary>
    private readonly HashSet<string> _reversed = new();
    /// <summary>Téléphones sur lesquels on a déjà ouvert l'appli depuis qu'ils sont branchés.</summary>
    private readonly HashSet<string> _launched = new();
    /// <summary>Téléphones sur lesquels l'appli n'est pas installée.</summary>
    private readonly HashSet<string> _noApp = new();
    private string? _adb;
    private bool _ownAdb;
    private Task? _download;
    private int _tick;
    private CancellationTokenSource? _stop;

    /// <summary>off, notool, downloading, nophone, unauthorized, offline, noapp, ready, error.</summary>
    public string State { get; private set; } = "off";
    public string? Phone { get; private set; }
    public string? Serial { get; private set; }
    public int? Progress { get; private set; }
    public string? Error { get; private set; }
    public bool Installing { get; private set; }

    public UsbBridge(ConfigStore store, DeckServer server)
    {
        _store = store;
        _server = server;
    }

    public object Status => new
    {
        state = State,
        phone = Phone,
        progress = Progress,
        error = Error,
        installing = Installing,
        usbClient = _server.UsbConnected,
    };

    public void Start()
    {
        _stop = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_stop.Token));
    }

    public void Stop()
    {
        _stop?.Cancel();
        // Notre propre copie d'adb laisse sinon un petit programme tourner en arrière-plan.
        if (_ownAdb && _adb != null) try { Run(_adb, TimeSpan.FromSeconds(3), "kill-server").Wait(4000); } catch { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception e)
            {
                State = "error";
                Error = e.Message;
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickAsync()
    {
        if (!_store.Config.UsbEnabled)
        {
            State = "off";
            Phone = null;
            return;
        }
        if (_adb == null)
        {
            (_adb, _ownAdb) = FindAdb(_store.DataDir);
            if (_adb == null)
            {
                // Première utilisation : on télécharge l'outil officiel de Google (une seule fois, ~7 Mo).
                if (!OperatingSystem.IsWindows()) { State = "notool"; return; }
                _download ??= DownloadToolsAsync();
                if (_download.IsCompleted)
                {
                    if (_download.IsFaulted)
                    {
                        State = "error";
                        Error = "Téléchargement de l'outil USB impossible : " + _download.Exception?.GetBaseException().Message;
                        _download = null;
                    }
                }
                else State = "downloading";
                return;
            }
        }

        var (_, output) = await Run(_adb, TimeSpan.FromSeconds(15), "devices", "-l");
        var devices = ParseDevices(output);
        var present = devices.Select(d => d.Serial).ToHashSet();
        _reversed.IntersectWith(present);
        _launched.IntersectWith(present);
        _noApp.IntersectWith(present);
        // De temps en temps on remet le tunnel (il disparaît si le débogage USB est coupé puis remis).
        if (++_tick % 15 == 0) _reversed.Clear();

        var ready = devices.Where(d => d.State == "device").ToList();
        if (ready.Count == 0)
        {
            Phone = devices.FirstOrDefault()?.Model;
            Serial = null;
            State = devices.Any(d => d.State == "unauthorized") ? "unauthorized"
                : devices.Count > 0 ? "offline"
                : "nophone";
            Error = null;
            return;
        }

        var phone = ready[0];
        Phone = phone.Model;
        Serial = phone.Serial;
        Error = null;
        foreach (var d in ready)
        {
            if (_reversed.Contains(d.Serial)) continue;
            // Port du téléphone = port habituel ; côté PC, le port réservé au câble.
            var (code, outText) = await Run(_adb, TimeSpan.FromSeconds(10), "-s", d.Serial, "reverse",
                $"tcp:{_store.Config.Port}", $"tcp:{_server.UsbPort}");
            if (code == 0) _reversed.Add(d.Serial);
            else Error = outText.Trim();
        }

        // Téléphone branché mais appli pas connectée : on l'ouvre pour lui (une fois par branchement).
        if (_store.Config.UsbOpenApp && !_launched.Contains(phone.Serial) && _reversed.Contains(phone.Serial) && _server.ClientNames.Count == 0)
        {
            _launched.Add(phone.Serial);
            var url = LocalNetwork.ConnectUrl(LocalNetwork.Addresses().First(), _store);
            var (_, startOut) = await Run(_adb, TimeSpan.FromSeconds(10), "-s", phone.Serial, "shell",
                "am", "start", "-n", AppActivity, "--es", "keyradeck_pair", $"'{url}'");
            if (startOut.Contains("does not exist", StringComparison.OrdinalIgnoreCase)) _noApp.Add(phone.Serial);
        }
        State = _noApp.Contains(phone.Serial) && _server.ClientNames.Count == 0 ? "noapp" : "ready";
    }

    /// <summary>Installe (ou met à jour) l'appli Android sur le téléphone branché.</summary>
    public async Task<string?> InstallAppAsync()
    {
        if (_adb == null || Serial == null) return "Aucun téléphone prêt : branche-le et accepte le débogage USB.";
        if (Installing) return null;
        Installing = true;
        try
        {
            var apk = Path.Combine(_store.DataDir, "KeyraDeck.apk");
            await using (var s = await Http.GetStreamAsync(ApkUrl))
            await using (var f = File.Create(apk))
                await s.CopyToAsync(f);
            var (code, output) = await Run(_adb, TimeSpan.FromMinutes(3), "-s", Serial, "install", "-r", apk);
            try { File.Delete(apk); } catch { }
            if (code != 0 || !output.Contains("Success")) return "Installation refusée par le téléphone : " + output.Trim();
            _launched.Remove(Serial);
            _noApp.Remove(Serial);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            Installing = false;
        }
    }

    private async Task DownloadToolsAsync()
    {
        var dir = _store.DataDir;
        var zip = Path.Combine(dir, "platform-tools.zip");
        Progress = 0;
        try
        {
            using (var resp = await Http.GetAsync(ToolsUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n));
                    done += n;
                    if (total > 0) Progress = (int)(done * 100 / total);
                }
            }
            var target = Path.Combine(dir, "platform-tools");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(zip, dir);
        }
        finally
        {
            Progress = null;
            try { File.Delete(zip); } catch { }
        }
    }

    /// <summary>
    /// Cherche adb : celui d'Android Studio s'il existe (deux versions différentes se gênent), sinon notre copie.
    /// </summary>
    private static (string? Path, bool Own) FindAdb(string dataDir)
    {
        var exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var candidates = new List<string>();
        foreach (var v in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "platform-tools", exe));
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) candidates.Add(Path.Combine(local, "Android", "Sdk", "platform-tools", exe));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), exe));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return (found, false);
        var own = Path.Combine(dataDir, "platform-tools", exe);
        return File.Exists(own) ? (own, true) : (null, false);
    }

    private record AdbDevice(string Serial, string State, string? Model);

    /// <summary>Lit la sortie de « adb devices -l » : « SERIE  device product:x model:Pixel_7 … ».</summary>
    private static List<AdbDevice> ParseDevices(string output)
    {
        var list = new List<AdbDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith("*")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            // Connexions adb par Wi-Fi (adresse:port) : inutiles ici, le Wi-Fi est déjà géré par KeyraDeck.
            if (parts[0].Contains(':')) continue;
            var state = parts[1] == "no" ? "offline" : parts[1];
            var model = parts.FirstOrDefault(p => p.StartsWith("model:"))?["model:".Length..].Replace('_', ' ');
            list.Add(new AdbDevice(parts[0], state, model));
        }
        return list;
    }

    private static async Task<(int Code, string Output)> Run(string exe, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("adb ne démarre pas");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return (-1, "adb ne répond pas");
        }
        return (p.ExitCode, await stdout + await stderr);
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KeyraDeck");
        return c;
    }
}
