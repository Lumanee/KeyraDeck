using Microsoft.Win32;

namespace PhoneDeckPC;

/// <summary>« Démarrer avec Windows » : une entrée dans la clé Run de l'utilisateur (pas besoin d'être administrateur).</summary>
public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyraDeck";
    private const string LegacyValueName = "PhoneDeckPC";

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string || key?.GetValue(LegacyValueName) is string;
    }

    public static void Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe != null) key.SetValue(ValueName, $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    /// <summary>Après un renommage de l'exe : l'ancienne entrée de démarrage est remplacée par celle de KeyraDeck.</summary>
    public static void MigrateLegacy()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key.GetValue(LegacyValueName) is string) Set(true);
        }
        catch { }
    }
}
