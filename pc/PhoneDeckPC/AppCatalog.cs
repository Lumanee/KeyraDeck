using System.Diagnostics;
using Microsoft.Win32;

namespace PhoneDeckPC;

/// <summary>Une appli trouvée sur le PC. ShellPath s'ouvre avec l'Explorateur et sert aussi pour l'icône.</summary>
public record AppEntry(string Name, string ShellPath);

/// <summary>
/// Détecte les applis installées (liste « Toutes les applis » du menu Démarrer, y compris
/// les applis du Store comme Xbox) et sait les lancer à partir d'un simple nom.
/// </summary>
public class AppCatalog
{
    private readonly StaWorker _sta;
    private List<AppEntry> _apps = new();
    private List<AppEntry> _shortcuts = new();

    public AppCatalog(StaWorker sta) => _sta = sta;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public static IEnumerable<string> StartMenuRoots => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
    }.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p));

    /// <summary>(Re)lit la liste des applis. À appeler au démarrage.</summary>
    public Task RefreshAsync() => _sta.Run(() =>
    {
        _apps = ReadAppsFolder();
        _shortcuts = ReadStartMenuShortcuts();
    });

    private static List<AppEntry> ReadAppsFolder()
    {
        var list = new List<AppEntry>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return list;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            dynamic items = folder.Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                string name = item.Name;
                string path = item.Path;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    list.Add(new AppEntry(name, @"shell:AppsFolder\" + path));
            }
        }
        catch { }
        return list;
    }

    private static List<AppEntry> ReadStartMenuShortcuts()
    {
        var list = new List<AppEntry>();
        foreach (var root in StartMenuRoots)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".lnk" or ".url")
                        list.Add(new AppEntry(Path.GetFileNameWithoutExtension(file), file));
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>Raccourcis d'un dossier du menu Démarrer (ex. "Launcher"), triés par nom.</summary>
    public List<AppEntry> ShortcutsInStartMenuFolder(string folderName)
    {
        var wanted = folderName.Trim().TrimEnd('s', 'S');
        var result = new List<AppEntry>();
        foreach (var root in StartMenuRoots)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(dir).TrimEnd('s', 'S');
                    if (!name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".lnk" or ".url")
                            result.Add(new AppEntry(Path.GetFileNameWithoutExtension(file), file));
                    }
                }
            }
            catch { }
        }
        return result
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Cherche une appli par nom : nom exact, puis « commence par », puis « contient ».</summary>
    public AppEntry? Find(string name)
    {
        name = name.Trim();
        var pools = new[] { _apps, _shortcuts };
        foreach (var pool in pools)
        {
            var hit = pool.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        foreach (var pool in pools)
        {
            var hit = pool.Where(a => a.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(a => a.Name.Length).FirstOrDefault();
            if (hit != null) return hit;
        }
        foreach (var pool in pools)
        {
            var hit = pool.Where(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(a => a.Name.Length).FirstOrDefault();
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Ce qui permet d'obtenir l'icône d'une cible (chemin de fichier ou "shell:AppsFolder\..."),
    /// ou null si on ne trouve rien.
    /// </summary>
    public string? IconSourceFor(string target)
    {
        if (IsUrl(target)) return null;
        if (Path.IsPathRooted(target) && (File.Exists(target) || Directory.Exists(target))) return target;
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exe = FindExe(target);
            if (exe != null) return exe;
            var byName = Find(Path.GetFileNameWithoutExtension(target) == "chrome" ? "Google Chrome" : Path.GetFileNameWithoutExtension(target));
            return byName?.ShellPath;
        }
        return Find(target)?.ShellPath;
    }

    /// <summary>Lance l'action « open ». Renvoie null si OK, sinon un message pour le téléphone.</summary>
    public Task<string?> LaunchAsync(DeckAction action) => _sta.Run<string?>(() =>
    {
        var target = action.Target?.Trim();
        if (string.IsNullOrEmpty(target)) return OpenUrlOr(action.FallbackUrl, "Aucune cible configurée");
        try
        {
            if (IsUrl(target)) { ShellOpen(target, null); return null; }

            if (Path.IsPathRooted(target))
            {
                if (File.Exists(target) || Directory.Exists(target)) { ShellOpen(target, action.Args); return null; }
                return OpenUrlOr(action.FallbackUrl, $"Introuvable : {target}");
            }

            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var exe = FindExe(target);
                if (exe != null) { ShellOpen(exe, action.Args); return null; }
            }

            var app = Find(target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? (target.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ? "Google Chrome" : Path.GetFileNameWithoutExtension(target))
                : target);
            if (app != null)
            {
                if (app.ShellPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", app.ShellPath) { UseShellExecute = true });
                else
                    ShellOpen(app.ShellPath, action.Args);
                return null;
            }

            return OpenUrlOr(action.FallbackUrl, $"Appli introuvable : {target}");
        }
        catch (Exception e)
        {
            return OpenUrlOr(action.FallbackUrl, $"Impossible d'ouvrir {target} : {e.Message}");
        }
    });

    public Task<string?> OpenUrlAsync(string url) => _sta.Run<string?>(() =>
    {
        try { ShellOpen(url, null); return null; }
        catch (Exception e) { return $"Impossible d'ouvrir {url} : {e.Message}"; }
    });

    private static string? OpenUrlOr(string? url, string error)
    {
        if (string.IsNullOrWhiteSpace(url)) return error;
        try { ShellOpen(url, null); return null; }
        catch { return error; }
    }

    private static void ShellOpen(string file, string? args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
        if (File.Exists(file)) psi.WorkingDirectory = Path.GetDirectoryName(file) ?? "";
        Process.Start(psi);
    }

    public static bool IsUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Trouve un .exe déclaré dans « App Paths » (c'est comme ça que Windows trouve chrome.exe).</summary>
    private static string? FindExe(string exeName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName);
                if (key?.GetValue(null) is string path)
                {
                    path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
        }
        return null;
    }
}
