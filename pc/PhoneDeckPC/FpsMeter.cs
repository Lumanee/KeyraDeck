using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>
/// Images par seconde des jeux : compte les « Present » de chaque programme, comme PresentMon, grâce aux traces ETW de Windows.
/// DirectX 9 à 12 via les traces DXGI / D3D9 ; Vulkan et OpenGL via le pilote graphique de Windows (DxgKrnl).
/// Windows ne donne ces traces qu'à un programme lancé en administrateur ou à un membre du groupe
/// « Utilisateurs du journal de performances » : sinon <see cref="Rate"/> renvoie null et <see cref="Status"/> vaut « admin ».
/// </summary>
public sealed class FpsMeter
{
    private const string SessionName = "KeyraDeck-FPS";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9"); // Microsoft-Windows-D3D9
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D"); // Microsoft-Windows-DxgKrnl
    private const ushort DxgiPresentStart = 42, D3D9PresentStart = 1, DxgKrnlPresentInfo = 184;

    /// <summary>Instants (horloge QPC) des dernières images d'un programme : DirectX d'un côté, pilote (Vulkan, OpenGL) de l'autre.</summary>
    private sealed class Track
    {
        public readonly Queue<long> Dx = new(), Kernel = new();
    }

    private readonly ConcurrentDictionary<int, Track> _tracks = new();
    private readonly object _lock = new();
    private ulong _session, _trace;
    private IntPtr _props;
    private Thread? _thread;
    private EventRecordCallback? _callback; // gardé en vie tant que Windows peut l'appeler
    private bool _running, _denied, _exitHooked;

    /// <summary>null : mesure en cours ou pas encore tentée ; « admin » : Windows refuse sans droits ; « erreur » : autre échec.</summary>
    public string? Status { get; private set; }

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
                if (rc != 0)
                {
                    _denied = true; Status = rc == ERROR_ACCESS_DENIED ? "admin" : "erreur";
                    Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; return;
                }

                Enable(DxgiProvider, DxgiPresentStart);
                Enable(D3D9Provider, D3D9PresentStart);
                Enable(DxgKrnlProvider, DxgKrnlPresentInfo);

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
                if (_trace == INVALID_PROCESSTRACE_HANDLE) { StopSession(); _denied = true; Status = "erreur"; return; }

                _running = true;
                Status = null;
                var handles = new[] { _trace };
                _thread = new Thread(() => { try { ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "KeyraDeck FPS" };
                _thread.Start();
                if (!_exitHooked) { _exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(); }
            }
            catch
            {
                _denied = true; Status = "erreur";
            }
        }
    }

    /// <summary>Active un fournisseur de traces en ne gardant qu'un seul évènement (moins de travail pour Windows et pour nous).</summary>
    private void Enable(Guid provider, ushort eventId)
    {
        // EVENT_FILTER_EVENT_ID : FilterIn (1 octet), Reserved (1), Count (2), puis les numéros d'évènements.
        var filter = Marshal.AllocHGlobal(8);
        var desc = Marshal.AllocHGlobal(16);
        var parms = Marshal.AllocHGlobal(48);
        try
        {
            Marshal.WriteInt64(filter, 0);
            Marshal.WriteByte(filter, 0, 1);
            Marshal.WriteInt16(filter, 2, 1);
            Marshal.WriteInt16(filter, 4, (short)eventId);
            // EVENT_FILTER_DESCRIPTOR : Ptr (8), Size (4), Type (4).
            Marshal.WriteInt64(desc, 0, filter.ToInt64());
            Marshal.WriteInt32(desc, 8, 6);
            Marshal.WriteInt32(desc, 12, unchecked((int)EVENT_FILTER_TYPE_EVENT_ID));
            // ENABLE_TRACE_PARAMETERS (version 2) : Version, EnableProperty, ControlFlags, SourceId, EnableFilterDesc (à 32), FilterDescCount (à 40).
            for (var i = 0; i < 48; i++) Marshal.WriteByte(parms, i, 0);
            Marshal.WriteInt32(parms, 0, 2);
            Marshal.WriteIntPtr(parms, 32, desc);
            Marshal.WriteInt32(parms, 40, 1);
            var g = provider;
            var rc = EnableTraceEx2(_session, ref g, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_VERBOSE, ulong.MaxValue, 0, 0, parms);
            // Filtre refusé (vieux Windows) : on prend tout et on trie nous-mêmes.
            if (rc != 0) EnableTraceEx2(_session, ref g, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_VERBOSE, ulong.MaxValue, 0, 0, IntPtr.Zero);
        }
        catch { }
        finally { Marshal.FreeHGlobal(filter); Marshal.FreeHGlobal(desc); Marshal.FreeHGlobal(parms); }
    }

    /// <summary>
    /// Images par seconde du programme sur la dernière seconde mesurée ; null si on ne peut pas mesurer.
    /// On se fie à l'heure de chaque image (pas à l'heure d'arrivée) : Windows livre les traces par paquets, environ une fois par seconde.
    /// </summary>
    public int? Rate(int pid)
    {
        if (!_running) return null;
        var now = Stopwatch.GetTimestamp();
        var f = Stopwatch.Frequency;
        // Ménage : programmes qui n'affichent plus rien depuis 10 s.
        foreach (var (key, t) in _tracks)
            lock (t) if (Latest(t.Dx) < now - 10 * f && Latest(t.Kernel) < now - 10 * f) _tracks.TryRemove(key, out _);
        if (!_tracks.TryGetValue(pid, out var track)) return 0;
        lock (track)
        {
            // Les jeux DirectX passent aussi par le pilote : on préfère le compte DirectX quand il y en a un.
            var q = Latest(track.Dx) >= now - 3 * f ? track.Dx : track.Kernel;
            var last = Latest(q);
            if (last < now - 3 * f) return 0; // rien depuis 3 s (paquet d'1 s compris) : jeu en pause ou réduit
            var n = 0;
            foreach (var ts in q) if (ts > last - f) n++;
            return n;
        }
    }

    private static long Latest(Queue<long> q) => q.Count == 0 ? long.MinValue / 2 : q.Last();

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            try { CloseTrace(_trace); } catch { }
            StopSession();
            _tracks.Clear();
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
            if (id != DxgiPresentStart && id != D3D9PresentStart && id != DxgKrnlPresentInfo) return;
            var provider = Marshal.PtrToStructure<Guid>(record + 24);
            bool dx;
            if ((id == DxgiPresentStart && provider == DxgiProvider) || (id == D3D9PresentStart && provider == D3D9Provider)) dx = true;
            else if (id == DxgKrnlPresentInfo && provider == DxgKrnlProvider) dx = false;
            else return;
            var pid = Marshal.ReadInt32(record, 12);
            var ts = Marshal.ReadInt64(record, 16); // horloge QPC (ClientContext = 1), comme Stopwatch
            var track = _tracks.GetOrAdd(pid, _ => new Track());
            lock (track)
            {
                var q = dx ? track.Dx : track.Kernel;
                q.Enqueue(ts);
                var old = ts - 2 * Stopwatch.Frequency;
                while (q.Count > 0 && q.Peek() < old) q.Dequeue();
            }
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
    private const uint ERROR_ALREADY_EXISTS = 183, ERROR_ACCESS_DENIED = 5;
    private const uint EVENT_FILTER_TYPE_EVENT_ID = 0x80000200;
    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const int WNODE_FLAG_TRACED_GUID = 0x00020000;
    private const int EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    private const int PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    private const int PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    private const byte TRACE_LEVEL_VERBOSE = 5;
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
