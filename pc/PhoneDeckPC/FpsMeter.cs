using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>
/// Images par seconde des jeux : compte les « Present » de DirectX (9 à 12) de chaque programme, comme PresentMon,
/// grâce aux traces ETW de Windows. Windows ne les donne qu'à un programme lancé en administrateur :
/// sinon <see cref="Rate"/> renvoie null et le widget affiche « — ».
/// </summary>
public sealed class FpsMeter
{
    private const string SessionName = "KeyraDeck-FPS";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9"); // Microsoft-Windows-D3D9
    private const ushort DxgiPresentStart = 42, D3D9PresentStart = 1;

    private readonly ConcurrentDictionary<int, int> _counts = new();
    private readonly object _lock = new();
    private ulong _session, _trace;
    private IntPtr _props;
    private Thread? _thread;
    private EventRecordCallback? _callback; // gardé en vie tant que Windows peut l'appeler
    private bool _running, _denied, _exitHooked;
    private DateTime _lastAt = DateTime.UtcNow;
    private readonly Dictionary<int, int> _rates = new();

    /// <summary>Démarre la trace (une seule fois). Sans les droits administrateur, on n'essaie qu'une fois.</summary>
    public void Start()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
        lock (_lock)
        {
            if (_running || _denied) return;
            try
            {
                _props = NewProperties();
                var rc = StartTraceW(out _session, SessionName, _props);
                if (rc == ERROR_ALREADY_EXISTS)
                {
                    // Trace restée ouverte après un arrêt brutal de KeyraDeck : on la ferme et on recommence.
                    ControlTraceW(0, SessionName, _props, EVENT_TRACE_CONTROL_STOP);
                    Marshal.FreeHGlobal(_props);
                    _props = NewProperties();
                    rc = StartTraceW(out _session, SessionName, _props);
                }
                if (rc != 0) { _denied = true; Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; return; }

                var dxgi = DxgiProvider; var d3d9 = D3D9Provider;
                EnableTraceEx2(_session, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, ulong.MaxValue, 0, 0, IntPtr.Zero);
                EnableTraceEx2(_session, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, ulong.MaxValue, 0, 0, IntPtr.Zero);

                _callback = OnEvent;
                var logfile = Marshal.AllocHGlobal(LogfileSize);
                try
                {
                    for (var i = 0; i < LogfileSize; i++) Marshal.WriteByte(logfile, i, 0);
                    var name = Marshal.StringToHGlobalUni(SessionName);
                    Marshal.WriteIntPtr(logfile, 8, name);                                   // LoggerName
                    Marshal.WriteInt32(logfile, 28, PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD); // ProcessTraceMode
                    Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(_callback)); // EventRecordCallback
                    _trace = OpenTraceW(logfile);
                }
                catch { _trace = INVALID_PROCESSTRACE_HANDLE; }
                // logfile et le nom restent alloués : Windows peut s'y référer tant que la trace tourne (quelques octets, une fois).
                if (_trace == INVALID_PROCESSTRACE_HANDLE) { StopSession(); _denied = true; return; }

                _running = true;
                _lastAt = DateTime.UtcNow;
                var handles = new[] { _trace };
                _thread = new Thread(() => { try { ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "KeyraDeck FPS" };
                _thread.Start();
                if (!_exitHooked) { _exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(); }
            }
            catch
            {
                _denied = true;
            }
        }
    }

    /// <summary>Images par seconde du programme, depuis le dernier appel ; null si on ne peut pas mesurer.</summary>
    public int? Rate(int pid)
    {
        if (!_running) return null;
        var now = DateTime.UtcNow;
        var secs = (now - _lastAt).TotalSeconds;
        if (secs >= 0.5)
        {
            // Relevé de tous les programmes d'un coup, puis remise à zéro.
            _rates.Clear();
            foreach (var key in _counts.Keys)
                if (_counts.TryRemove(key, out var n)) _rates[key] = (int)Math.Round(n / secs);
            _lastAt = now;
        }
        return _rates.GetValueOrDefault(pid);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            try { CloseTrace(_trace); } catch { }
            StopSession();
            _counts.Clear();
        }
    }

    private void StopSession()
    {
        try { if (_props != IntPtr.Zero) ControlTraceW(_session, null, _props, EVENT_TRACE_CONTROL_STOP); } catch { }
        if (_props != IntPtr.Zero) { Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; }
    }

    // EVENT_RECORD : EventHeader.ProcessId à 12, ProviderId à 24, EventDescriptor.Id à 40.
    private void OnEvent(IntPtr record)
    {
        try
        {
            var id = (ushort)Marshal.ReadInt16(record, 40);
            if (id != DxgiPresentStart && id != D3D9PresentStart) return;
            var provider = Marshal.PtrToStructure<Guid>(record + 24);
            if ((id == DxgiPresentStart && provider == DxgiProvider) || (id == D3D9PresentStart && provider == D3D9Provider))
                _counts.AddOrUpdate(Marshal.ReadInt32(record, 12), 1, (_, n) => n + 1);
        }
        catch { }
    }

    // EVENT_TRACE_PROPERTIES (120 octets en 64 bits) suivie du nom de la session.
    private static IntPtr NewProperties()
    {
        const int size = 120 + 2 * (64 + 1);
        var p = Marshal.AllocHGlobal(size);
        for (var i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, size);                        // Wnode.BufferSize
        Marshal.StructureToPtr(Guid.NewGuid(), p + 24, false); // Wnode.Guid
        Marshal.WriteInt32(p, 40, 1);                          // Wnode.ClientContext : horloge QPC
        Marshal.WriteInt32(p, 44, WNODE_FLAG_TRACED_GUID);     // Wnode.Flags
        Marshal.WriteInt32(p, 64, EVENT_TRACE_REAL_TIME_MODE); // LogFileMode
        Marshal.WriteInt32(p, 68, 1);                          // FlushTimer : 1 s
        Marshal.WriteInt32(p, 116, 120);                       // LoggerNameOffset
        return p;
    }

    private const int LogfileSize = 448; // EVENT_TRACE_LOGFILEW en 64 bits
    private const uint ERROR_ALREADY_EXISTS = 183;
    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const int WNODE_FLAG_TRACED_GUID = 0x00020000;
    private const int EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    private const int PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    private const int PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    private const byte TRACE_LEVEL_INFORMATION = 4;
    private const ulong INVALID_PROCESSTRACE_HANDLE = ulong.MaxValue;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(IntPtr record);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint ControlTraceW(ulong handle, string? name, IntPtr properties, uint control);
    [DllImport("advapi32.dll")] private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern ulong OpenTraceW(IntPtr logfile);
    [DllImport("advapi32.dll")] private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
}
