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
    private const string ReleaseApi = "https://api.github.com/repos/Lumanee/PhoneDeck/releases/tags/derniere-version";
    private const string AssetName = "PhoneDeckPC-Windows.zip";

    private static readonly HttpClient Http = CreateClient();

    private string? _downloadUrl;

    /// <summary>Supprime les restes d'une mise à jour précédente (ancien .exe renommé).</summary>
    public static void CleanupOld()
    {
        try { var exe = Environment.ProcessPath; if (exe != null && File.Exists(exe + ".old")) File.Delete(exe + ".old"); } catch { }
    }

    public static int CurrentBuild => Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PhoneDeckPC");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public async Task<UpdateInfo> CheckAsync()
    {
        var current = CurrentBuild;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(ReleaseApi));
            var body = doc.RootElement.GetProperty("body").GetString() ?? "";
            var m = Regex.Match(body, @"build:(\d+)");
            if (!m.Success) return new UpdateInfo(false, current, 0, null);
            var latest = int.Parse(m.Groups[1].Value);
            _downloadUrl = doc.RootElement.GetProperty("assets").EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == AssetName)
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
            var work = Path.Combine(Path.GetTempPath(), "PhoneDeckUpdate");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            var zip = Path.Combine(work, "update.zip");
            await using (var s = await Http.GetStreamAsync(_downloadUrl!))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f);
            var extracted = Path.Combine(work, "new");
            ZipFile.ExtractToDirectory(zip, extracted);
            if (!File.Exists(Path.Combine(extracted, Path.GetFileName(exe))))
                return "Le fichier téléchargé est incomplet";

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
                start "" "{exe}"
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
