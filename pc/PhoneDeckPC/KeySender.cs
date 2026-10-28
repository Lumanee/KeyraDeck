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

    private static readonly HashSet<string> PadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "Alt", "Shift", "Win", "Back", "Enter", "Tab", "Escape", "Left", "Right", "Up", "Down",
        "Delete", "Home", "End", "PageUp", "PageDown", "Space",
    };

    /// <summary>Raccourcis acceptés depuis la page clavier : modificateurs + une touche simple (lettre, chiffre, F1-F12, flèches…).</summary>
    public static bool IsSafeSpec(string spec)
    {
        if (spec.Length == 0 || spec.Length > 40) return false;
        foreach (var part in spec.Split('+'))
        {
            if (PadKeys.Contains(part)) continue;
            if (part.Length == 1 && char.IsLetterOrDigit(part[0])) continue;
            if (part.Length is 2 or 3 && (part[0] is 'F' or 'f') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 12) continue;
            return false;
        }
        return true;
    }

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

    // ---- Clavier PC complet du téléphone : chaque touche est enfoncée puis relâchée, comme un vrai clavier ----

    private static readonly HashSet<int> PhysicalKeys = BuildPhysicalKeys();

    private static HashSet<int> BuildPhysicalKeys()
    {
        var set = new HashSet<int> { 0x08, 0x09, 0x0D, 0x14, 0x1B, 0x20, 0x2C, 0x2D, 0x2E, 0x5B, 0x5D, 0xE2 };
        for (int vk = 0x21; vk <= 0x28; vk++) set.Add(vk);  // Pg préc/suiv, Fin, Début, flèches
        for (int vk = 0x30; vk <= 0x39; vk++) set.Add(vk);  // chiffres
        for (int vk = 0x41; vk <= 0x5A; vk++) set.Add(vk);  // lettres
        for (int vk = 0x70; vk <= 0x7B; vk++) set.Add(vk);  // F1-F12
        for (int vk = 0xA0; vk <= 0xA5; vk++) set.Add(vk);  // Maj, Ctrl, Alt gauche/droite
        for (int vk = 0xBA; vk <= 0xC0; vk++) set.Add(vk);  // ponctuation (OEM)
        for (int vk = 0xDB; vk <= 0xDF; vk++) set.Add(vk);
        return set;
    }

    /// <summary>Touches acceptées depuis le clavier PC du téléphone (codes de touche virtuelle Windows).</summary>
    public static bool IsPhysicalKey(int vk) => PhysicalKeys.Contains(vk);

    /// <summary>
    /// Enfonce ou relâche une touche. On envoie le code « matériel » (scan code) de la touche pour la disposition
    /// active : les jeux le voient comme un vrai clavier et AZERTY/QWERTY sont respectés.
    /// </summary>
    public static void KeyEvent(int vk, bool down)
    {
        INPUT[] inputs = vk == 0xA5
            // AltGr = Ctrl gauche + Alt droit, comme sur un vrai clavier AZERTY.
            ? (down ? new[] { PhysicalKey(0xA2, false), PhysicalKey(0xA5, false) } : new[] { PhysicalKey(0xA5, true), PhysicalKey(0xA2, true) })
            : new[] { PhysicalKey(vk, !down) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT PhysicalKey(int vk, bool up)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        var sc = vk == 0x2C ? 0u : MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC_EX, layout);
        if (sc == 0) return Key((Keys)vk, up);
        uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
        if ((sc & 0xFF00) != 0 || Extended.Contains((Keys)vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)(sc & 0xFF), dwFlags = flags } },
        };
    }

    private const uint MAPVK_VK_TO_VSC_EX = 4;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // ---- Souris (page « souris / clavier » du téléphone) ----

    // Les déplacements arrivent par paquets irréguliers (Wi-Fi) : on les met en file et un fil dédié les répartit
    // en petits pas toutes les ~4 ms, ce qui donne un curseur fluide au lieu de sauts.
    private static readonly object MouseLock = new();
    private static readonly AutoResetEvent MouseSignal = new(false);
    private static double _pendX, _pendY, _remX, _remY;
    private static Thread? _mouseThread;

    public static void MouseMove(int dx, int dy)
    {
        lock (MouseLock) { _pendX += dx; _pendY += dy; }
        if (_mouseThread == null)
        {
            lock (MouseLock)
            {
                if (_mouseThread == null)
                {
                    try { timeBeginPeriod(1); } catch { }
                    _mouseThread = new Thread(MouseLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "PhoneDeck souris" };
                    _mouseThread.Start();
                }
            }
        }
        MouseSignal.Set();
    }

    private static double TakeStep(ref double pending)
    {
        if (Math.Abs(pending) < 1) { var v = pending; pending = 0; return v; }
        var step = pending * 0.5;
        if (Math.Abs(step) < 1) step = Math.Sign(pending);
        pending -= step;
        return step;
    }

    private static void MouseLoop()
    {
        while (true)
        {
            double sx, sy;
            lock (MouseLock)
            {
                if (Math.Abs(_pendX) < 0.01 && Math.Abs(_pendY) < 0.01) { sx = sy = 0; }
                else { sx = TakeStep(ref _pendX); sy = TakeStep(ref _pendY); }
            }
            if (sx == 0 && sy == 0) { MouseSignal.WaitOne(); continue; }
            _remX += sx; _remY += sy;
            var ix = (int)_remX; var iy = (int)_remY;
            if (ix != 0 || iy != 0)
            {
                _remX -= ix; _remY -= iy;
                try { SendMouse(ix, iy, 0, MOUSEEVENTF_MOVE); } catch { }
            }
            Thread.Sleep(4);
        }
    }

    /// <summary>Applique tout de suite les déplacements en attente (avant un clic, pour qu'il tombe au bon endroit).</summary>
    private static void FlushMouse()
    {
        int ix, iy;
        lock (MouseLock)
        {
            _remX += _pendX; _remY += _pendY; _pendX = _pendY = 0;
            ix = (int)_remX; iy = (int)_remY; _remX -= ix; _remY -= iy;
        }
        if (ix != 0 || iy != 0) SendMouse(ix, iy, 0, MOUSEEVENTF_MOVE);
    }

    public static void MouseButton(bool right, bool down)
    {
        FlushMouse();
        SendMouse(0, 0, 0, right ? (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP) : (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP));
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    /// <summary>Molette : delta positif = vers le haut (une « encoche » = 120).</summary>
    public static void MouseWheel(int delta) => SendMouse(0, 0, (uint)delta, MOUSEEVENTF_WHEEL);

    private static void SendMouse(int dx, int dy, uint data, uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

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
