using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>Sait quelle appli est au premier plan (pour changer de page automatiquement sur le téléphone).</summary>
public static class ForegroundWatcher
{
    /// <summary>Nom du programme au premier plan, sans « .exe » et en minuscules (ex. « reaper »), ou null.</summary>
    public static string? CurrentProcessName()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return Normalize(p.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Programmes qui ont une fenêtre ouverte (liste proposée dans le configurateur).</summary>
    public static List<string> OpenApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                            names.Add(Normalize(p.ProcessName));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return names.ToList();
    }

    public static string Normalize(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe") ? name[..^4] : name;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
