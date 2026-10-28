using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhoneDeckPC;

public record UpdateInfo(bool Available, int Current, int Latest, string? Error);

/// <summary>
/// Mise à jour automatique : compare avec la dernière version publiée sur GitHub, la télécharge
/// et se remplace tout seul (le dépôt est public, aucune clé n'est nécessaire).
/// </summary>
public class Updater
{
    // Adresse du dépôt : la nouvelle (KeyraDeck) d'abord, puis l'ancienne (GitHub redirige aussi l'ancienne après un renommage).
    private static readonly string[] ReleaseApis =
    {
        "https://api.github.com/repos/Lumanee/KeyraDeck/releases/tags/derniere-version",
        "https://api.github.com/repos/Lumanee/PhoneDeck/releases/tags/derniere-version",
    };
    // Nouveau nom d'abord ; l'ancien reste publié pour les installations qui n'ont pas encore été mises à jour.
    private static readonly string[] AssetNames = { "KeyraDeck-Windows.zip", "PhoneDeckPC-Windows.zip" };
    private const string ExeName = "KeyraDeck.exe";

    private static readonly HttpClient Http = CreateClient();

    private string? _downloadUrl;

    /// <summary>Supprime les restes d'une mise à jour précédente (ancien .exe renommé).</summary>
    public static void CleanupOld()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
            var dir = Path.GetDirectoryName(exe)!;
            foreach (var f in new[] { "PhoneDeckPC.exe", "PhoneDeckPC.exe.old" })
            {
                var p = Path.Combine(dir, f);
                if (!string.Equals(p, exe, StringComparison.OrdinalIgnoreCase) && File.Exists(p)) File.Delete(p);
            }
        }
        catch { }
    }

    /// <summary>
    /// Une ancienne installation (PhoneDeckPC.exe) qui se met à jour reçoit le nouvel exe sous l'ancien nom.
    /// Au lancement il se recopie en KeyraDeck.exe, démarre cette copie et s'arrête ; l'ancien fichier est supprimé ensuite.
    /// </summary>
    public static bool MigrateFromLegacyExe(string[] args)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !Path.GetFileName(exe).Equals("PhoneDeckPC.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var target = Path.Combine(Path.GetDirectoryName(exe)!, ExeName);
            File.Copy(exe, target, true);
            Process.Start(new ProcessStartInfo(target, string.Join(' ', args.Select(a => $"\"{a}\""))) { UseShellExecute = false });
            return true;
        }
        catch { return false; }
    }

    public static int CurrentBuild => Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KeyraDeck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public async Task<UpdateInfo> CheckAsync()
    {
        var current = CurrentBuild;
        try
        {
            string? json = null; Exception? last = null;
            foreach (var api in ReleaseApis)
            {
                try { json = await Http.GetStringAsync(api); break; } catch (Exception ex) { last = ex; }
            }
            if (json == null) throw last ?? new InvalidOperationException("Aucune adresse n'a répondu");
            using var doc = JsonDocument.Parse(json);
            var body = doc.RootElement.GetProperty("body").GetString() ?? "";
            var m = Regex.Match(body, @"build:(\d+)");
            if (!m.Success) return new UpdateInfo(false, current, 0, null);
            var latest = int.Parse(m.Groups[1].Value);
            var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
            _downloadUrl = AssetNames
                .Select(n => assets.FirstOrDefault(a => a.GetProperty("name").GetString() == n))
                .Where(a => a.ValueKind == JsonValueKind.Object)
                .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
            return new UpdateInfo(latest > current && _downloadUrl != null, current, latest, null);
        }
        catch (Exception e)
        {
            return new UpdateInfo(false, current, 0, "Impossible de vérifier les mises à jour : " + e.Message);
        }
    }

    /// <summary>Télécharge la nouvelle version et prépare le remplacement. L'appelant doit ensuite quitter l'appli.</summary>
    public async Task<string?> DownloadAndPrepareAsync()
    {
        try
        {
            if (_downloadUrl == null)
            {
                var info = await CheckAsync();
                if (!info.Available) return info.Error ?? "Déjà à jour";
            }
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'appli inconnu");
            var installDir = Path.GetDirectoryName(exe)!;
            var work = Path.Combine(Path.GetTempPath(), "KeyraDeckUpdate");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            var zip = Path.Combine(work, "update.zip");
            await using (var s = await Http.GetStreamAsync(_downloadUrl!))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f);
            var extracted = Path.Combine(work, "new");
            ZipFile.ExtractToDirectory(zip, extracted);
            // Le zip contient KeyraDeck.exe (ou PhoneDeckPC.exe pour l'ancien nom) : on relance l'exe qui existe dans le zip.
            var newExeName = File.Exists(Path.Combine(extracted, ExeName)) ? ExeName : Path.GetFileName(exe);
            if (!File.Exists(Path.Combine(extracted, newExeName)))
                return "Le fichier téléchargé est incomplet";

            var launch = Path.Combine(installDir, newExeName);

            // Windows interdit d'écraser un .exe qui tourne, mais autorise à le renommer : on met l'ancien de côté
            // puis on copie tout de suite les nouveaux fichiers, sans attendre la fermeture de l'appli.
            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                foreach (var src in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(installDir, Path.GetRelativePath(extracted, src));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, true);
                }
            }
            catch
            {
                if (File.Exists(old)) { if (File.Exists(exe)) File.Delete(exe); File.Move(old, exe); }
                throw;
            }

            // Un petit script attend la fin de l'ancienne appli (son numéro de processus) puis lance la nouvelle.
            var script = Path.Combine(work, "update.cmd");
            var pid = Environment.ProcessId;
            var cmd = $"""
                @echo off
                set tries=0
                :wait
                set /a tries+=1
                tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
                if not errorlevel 1 if %tries% lss 40 (
                    timeout /t 1 /nobreak >nul
                    goto wait
                )
                start "" "{launch}"
                """;
            await File.WriteAllTextAsync(script, cmd);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
            return null;
        }
        catch (Exception e)
        {
            return "Mise à jour impossible : " + e.Message;
        }
    }
}
