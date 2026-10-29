using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace PhoneDeckPC;

public record AudioOutput(string Id, string Name, bool IsDefault);

/// <summary>
/// Sorties son de Windows (casque, enceintes, écran HDMI…) et choix de la sortie par défaut.
/// Windows n'a pas d'API publique pour changer la sortie par défaut : on passe par l'interface COM
/// IPolicyConfig, non documentée, celle qu'utilisent le panneau Son de Windows et SoundSwitch.
/// Elle existe depuis Windows 7 et n'a pas changé depuis, mais Microsoft pourrait la retirer un jour.
/// </summary>
public static class AudioOutputs
{
    private static readonly object Lock = new();

    /// <summary>Sorties branchées et activées, la sortie par défaut marquée.</summary>
    public static List<AudioOutput> List()
    {
        lock (Lock)
        {
            using var enumerator = new MMDeviceEnumerator();
            string? current = null;
            try
            {
                using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                current = def.ID;
            }
            catch { /* aucune sortie son */ }

            var result = new List<AudioOutput>();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (d)
                {
                    string name;
                    try { name = d.FriendlyName; } catch { name = "Sortie son"; }
                    result.Add(new AudioOutput(d.ID, name, d.ID == current));
                }
            }
            return result.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Fait de cette sortie la sortie par défaut (sons, musique, jeux et appels). Renvoie son nom.</summary>
    public static string SetDefault(string id)
    {
        lock (Lock)
        {
            using var enumerator = new MMDeviceEnumerator();
            string name;
            using (var d = enumerator.GetDevice(id))
            {
                if (d.State != DeviceState.Active) throw new InvalidOperationException("Cette sortie n'est plus branchée");
                name = d.FriendlyName;
            }
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            }
            finally { Marshal.ReleaseComObject(policy); }
            return name;
        }
    }

    /// <summary>
    /// Passe à la sortie suivante de la liste (ou de toutes les sorties si la liste est vide).
    /// Les sorties débranchées sont sautées. Renvoie le nom de la nouvelle sortie, ou null s'il n'y a rien à changer.
    /// </summary>
    public static string? Cycle(IReadOnlyList<string> ids)
    {
        var all = List();
        var candidates = ids.Count == 0
            ? all
            : ids.Select(id => all.FirstOrDefault(o => o.Id == id)).OfType<AudioOutput>().ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("Aucune des sorties choisies n'est branchée");
        var current = candidates.FindIndex(o => o.IsDefault);
        var next = candidates[(current + 1) % candidates.Count];
        if (next.IsDefault) return null; // une seule sortie disponible, et c'est déjà elle
        return SetDefault(next.Id);
    }

    /// <summary>Liste d'ids enregistrée dans un bouton (séparés par « | »).</summary>
    public static List<string> ParseIds(string? target) =>
        (target ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    // Seul SetDefaultEndpoint sert ; les autres méthodes sont déclarées pour garder l'ordre de la table COM.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, int defaultPeriod, IntPtr period, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
    }
}
