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

            // Un petit script attend que l'appli se ferme, copie les nouveaux fichiers, puis relance l'appli.
            var script = Path.Combine(work, "update.cmd");
            var cmd = $"""
                @echo off
                chcp 65001 >nul
                set tries=0
                :wait
                set /a tries+=1
                timeout /t 1 /nobreak >nul
                xcopy /e /y /q "{extracted}\*" "{installDir}\" >nul
                if errorlevel 1 if %tries% lss 30 goto wait
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
