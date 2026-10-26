using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>
/// Simule des raccourcis clavier. Format : "Ctrl+Shift+M", plusieurs étapes séparées
/// par des virgules, et "wait:300" pour attendre 300 ms (petite macro).
/// </summary>
public static class KeySender
{
    private static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Keys.ControlKey, ["control"] = Keys.ControlKey,
        ["shift"] = Keys.ShiftKey, ["maj"] = Keys.ShiftKey,
        ["alt"] = Keys.Menu,
        ["win"] = Keys.LWin, ["windows"] = Keys.LWin,
        ["esc"] = Keys.Escape, ["echap"] = Keys.Escape,
        ["entree"] = Keys.Enter, ["return"] = Keys.Enter,
        ["espace"] = Keys.Space,
        ["suppr"] = Keys.Delete, ["del"] = Keys.Delete,
        ["playpause"] = Keys.MediaPlayPause, ["play"] = Keys.MediaPlayPause,
        ["next"] = Keys.MediaNextTrack, ["suivant"] = Keys.MediaNextTrack,
        ["prev"] = Keys.MediaPreviousTrack, ["previous"] = Keys.MediaPreviousTrack, ["precedent"] = Keys.MediaPreviousTrack,
        ["stop"] = Keys.MediaStop,
        ["mute"] = Keys.VolumeMute, ["volumeup"] = Keys.VolumeUp, ["volumedown"] = Keys.VolumeDown,
        ["printscreen"] = Keys.PrintScreen, ["impr"] = Keys.PrintScreen,
    };

    private static readonly HashSet<Keys> Modifiers = new() { Keys.ControlKey, Keys.ShiftKey, Keys.Menu, Keys.LWin };

    private static readonly HashSet<Keys> Extended = new()
    {
        Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
        Keys.PageUp, Keys.PageDown, Keys.NumLock, Keys.Divide, Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin,
        Keys.MediaPlayPause, Keys.MediaNextTrack, Keys.MediaPreviousTrack, Keys.MediaStop,
        Keys.VolumeMute, Keys.VolumeUp, Keys.VolumeDown, Keys.PrintScreen,
    };

    /// <summary>Renvoie null si tout va bien, sinon un message d'erreur.</summary>
    public static string? Send(string spec)
    {
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("wait:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(raw[5..], out var ms)) Thread.Sleep(Math.Clamp(ms, 0, 10_000));
                continue;
            }

            var keys = new List<Keys>();
            foreach (var part in raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = Parse(part);
                if (key == null) return $"Touche inconnue : « {part} »";
                keys.Add(key.Value);
            }
            if (keys.Count == 0) continue;

            var inputs = new List<INPUT>();
            var mods = keys.Where(Modifiers.Contains).ToList();
            var normal = keys.Where(k => !Modifiers.Contains(k)).ToList();
            foreach (var k in mods) inputs.Add(Key(k, up: false));
            foreach (var k in normal) { inputs.Add(Key(k, up: false)); inputs.Add(Key(k, up: true)); }
            for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], up: true));

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            Thread.Sleep(30);
        }
        return null;
    }

    /// <summary>Tape un texte caractère par caractère (accents et emojis compris).</summary>
    public static string? TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n')
            {
                inputs.Add(Key(Keys.Enter, up: false));
                inputs.Add(Key(Keys.Enter, up: true));
                continue;
            }
            foreach (var up in new[] { false, true })
            {
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
                });
            }
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return null;
    }

    private static Keys? Parse(string token)
    {
        if (Aliases.TryGetValue(token, out var alias)) return alias;
        if (token.Length == 1 && char.IsDigit(token[0])) return Keys.D0 + (token[0] - '0');
        if (token.Length == 1 && char.IsLetter(token[0])) return (Keys)char.ToUpperInvariant(token[0]);
        if (!int.TryParse(token, out _) && Enum.TryParse<Keys>(token, ignoreCase: true, out var k)) return k;
        return null;
    }

    private static INPUT Key(Keys k, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (Extended.Contains(k)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)k, dwFlags = flags } },
        };
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
