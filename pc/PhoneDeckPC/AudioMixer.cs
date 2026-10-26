using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace PhoneDeckPC;

public record MixerChannel(string Id, string Name, float Volume, bool Muted, string? Icon);

/// <summary>
/// Mélangeur de volume Windows : volume général + une ligne par appli qui joue du son.
/// Les sessions d'une même appli (ex. plusieurs onglets Chrome) sont regroupées.
/// </summary>
public class AudioMixer
{
    public const string MasterId = "master";
    private const string SystemId = "system";

    private readonly object _lock = new();
    private readonly Dictionary<string, (string Name, string? Icon)> _infoCache = new();

    public List<MixerChannel> GetChannels(bool withIcons)
    {
        lock (_lock)
        {
            var result = new List<MixerChannel>();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var ep = device.AudioEndpointVolume;
            result.Add(new MixerChannel(MasterId, "Général", ep.MasterVolumeLevelScalar, ep.Mute,
                withIcons ? IconFor(MasterId, null).Icon : null));

            var groups = new Dictionary<string, (float Volume, bool Muted)>();
            var order = new List<string>();
            foreach (var s in Sessions(device))
            {
                var key = KeyOf(s, out var pid);
                if (key == null) continue;
                if (!groups.ContainsKey(key))
                {
                    groups[key] = (s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute);
                    order.Add(key);
                    IconFor(key, pid); // remplit le cache du nom
                }
            }

            foreach (var key in order)
            {
                var info = IconFor(key, null);
                result.Add(new MixerChannel(key, info.Name, groups[key].Volume, groups[key].Muted,
                    withIcons ? info.Icon : null));
            }
            return result;
        }
    }

    public void SetVolume(string id, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        Apply(id,
            ep => ep.MasterVolumeLevelScalar = volume,
            s => s.SimpleAudioVolume.Volume = volume);
    }

    /// <summary>Micro Windows par défaut (celui des appels) coupé ou non.</summary>
    public bool MicMuted() => ReadMute(DataFlow.Capture);

    public bool MasterMuted() => ReadMute(DataFlow.Render);

    /// <summary>Volume général de Windows (0 à 1), ou null s'il n'y a pas de sortie son.</summary>
    public float? MasterVolume()
    {
        lock (_lock)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch { return null; }
        }
    }

    public void ToggleMic() => ToggleMute(DataFlow.Capture);

    public void ToggleMaster() => ToggleMute(DataFlow.Render);

    private bool ReadMute(DataFlow flow)
    {
        lock (_lock)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
                return device.AudioEndpointVolume.Mute;
            }
            catch { return false; } // pas de micro / pas de sortie son
        }
    }

    private void ToggleMute(DataFlow flow)
    {
        lock (_lock)
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        }
    }

    public void SetMute(string id, bool muted)
    {
        Apply(id,
            ep => ep.Mute = muted,
            s => s.SimpleAudioVolume.Mute = muted);
    }

    private void Apply(string id, Action<AudioEndpointVolume> master, Action<AudioSessionControl> session)
    {
        lock (_lock)
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (id == MasterId)
            {
                master(device.AudioEndpointVolume);
                return;
            }
            foreach (var s in Sessions(device))
                if (KeyOf(s, out _) == id) session(s);
        }
    }

    private static IEnumerable<AudioSessionControl> Sessions(MMDevice device)
    {
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
            yield return s;
        }
    }

    /// <summary>Clé de regroupement : nom du processus (ex. "chrome"), ou "system".</summary>
    private static string? KeyOf(AudioSessionControl s, out int pid)
    {
        pid = 0;
        if (s.IsSystemSoundsSession) return SystemId;
        try
        {
            pid = (int)s.GetProcessID;
            if (pid == 0) return null;
            using var p = Process.GetProcessById(pid);
            return p.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private (string Name, string? Icon) IconFor(string key, int? pid)
    {
        if (_infoCache.TryGetValue(key, out var cached)) return cached;
        if (key == MasterId) return _infoCache[key] = ("Général", null);
        if (key == SystemId) return _infoCache[key] = ("Sons système", null);
        if (pid is not > 0) return (key, null);

        string name = key;
        string? icon = null;
        try
        {
            using var p = Process.GetProcessById(pid.Value);
            var path = p.MainModule?.FileName;
            if (path != null)
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc!;
                icon = IconService.FromShell(path);
            }
        }
        catch { /* processus protégé : on garde le nom court */ }

        if (name == key && name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..];
        return _infoCache[key] = (name, icon);
    }
}
