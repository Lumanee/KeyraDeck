using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>Charge de l'appli au premier plan (widget « Appli active »). Fps = null si on ne peut pas les mesurer.</summary>
public record AppStats(string Name, int Cpu, int Ram, long RamMb, int Gpu, int? Fps);

/// <summary>Charge du processeur, de la mémoire, de la carte graphique et du disque, pour les widgets de performance.</summary>
public class SystemStats
{
    private ulong _lastIdle, _lastTotal;
    private Pdh? _pdh;
    private bool _pdhFailed;
    private readonly FpsMeter _fps = new();

    // Appli au premier plan : temps processeur au dernier appel, pour en déduire sa charge.
    private int _appPid;
    private TimeSpan _appCpuTime;
    private DateTime _appAt;

    /// <summary>(CPU %, RAM %, GPU %, disque %), entiers de 0 à 100. À appeler à intervalle régulier : les charges sont mesurées depuis le dernier appel.</summary>
    public (int Cpu, int Ram, int Gpu, int Disk) Read()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0, 0, 0);
        try
        {
            var cpu = 0;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var i = ToU64(idle);
                var total = ToU64(kernel) + ToU64(user); // « kernel » inclut déjà le temps d'inactivité
                if (_lastTotal != 0 && total > _lastTotal)
                {
                    var dTotal = total - _lastTotal;
                    var dIdle = i - _lastIdle;
                    cpu = (int)Math.Clamp(100 - (dIdle * 100.0 / dTotal), 0, 100);
                }
                _lastIdle = i; _lastTotal = total;
            }
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            var ram = GlobalMemoryStatusEx(ref mem) ? (int)mem.dwMemoryLoad : 0;
            var pdh = Counters();
            pdh?.Collect();
            var gpu = pdh == null ? 0 : (int)Math.Clamp(pdh.Gpu(null), 0, 100);
            var disk = pdh == null ? 0 : (int)Math.Clamp(100 - pdh.DiskIdle(), 0, 100);
            return (cpu, ram, gpu, disk);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Charge de l'appli au premier plan. À appeler juste après <see cref="Read"/> (qui relève les compteurs de la carte graphique).
    /// Les FPS ne se mesurent qu'avec les droits administrateur (traces DirectX de Windows).
    /// </summary>
    public AppStats? ReadForeground()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            var now = DateTime.UtcNow;
            var cpuTime = p.TotalProcessorTime;
            var cpu = 0;
            if (_appPid == pid && now > _appAt)
                cpu = (int)Math.Clamp((cpuTime - _appCpuTime).TotalMilliseconds * 100.0 / (now - _appAt).TotalMilliseconds / Environment.ProcessorCount, 0, 100);
            _appPid = (int)pid; _appCpuTime = cpuTime; _appAt = now;

            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            var bytes = p.WorkingSet64;
            var ram = GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0 ? (int)Math.Clamp(bytes * 100.0 / mem.ullTotalPhys, 0, 100) : 0;
            var gpu = _pdh == null ? 0 : (int)Math.Clamp(_pdh.Gpu((int)pid), 0, 100);
            _fps.Start();
            var name = p.ProcessName;
            try { var d = p.MainModule?.FileVersionInfo.FileDescription; if (!string.IsNullOrWhiteSpace(d)) name = d.Trim(); } catch { }
            return new AppStats(name, cpu, ram, bytes / (1024 * 1024), gpu, _fps.Rate((int)pid));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Plus de widget « Appli active » : on arrête la trace des images.</summary>
    public void StopFps() => _fps.Stop();

    private Pdh? Counters()
    {
        if (_pdh != null || _pdhFailed) return _pdh;
        try { _pdh = new Pdh(); }
        catch { _pdhFailed = true; }
        return _pdh;
    }

    private static ulong ToU64(FILETIME t) => ((ulong)t.dwHighDateTime << 32) | t.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Compteurs de performance de Windows (ceux du Gestionnaire des tâches) : carte graphique et disque.</summary>
    private sealed class Pdh
    {
        private readonly IntPtr _query, _gpu, _disk;
        private Dictionary<string, double> _gpuItems = new();

        public Pdh()
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) throw new InvalidOperationException("PDH");
            // Moteur 3D de chaque processus (« pid_1234_..._engtype_3D »), comme la colonne GPU du Gestionnaire des tâches.
            if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu) != 0) _gpu = IntPtr.Zero;
            if (PdhAddEnglishCounterW(_query, @"\PhysicalDisk(_Total)\% Idle Time", IntPtr.Zero, out _disk) != 0) _disk = IntPtr.Zero;
            PdhCollectQueryData(_query);
        }

        public void Collect()
        {
            PdhCollectQueryData(_query);
            _gpuItems = _gpu == IntPtr.Zero ? new() : ReadArray(_gpu);
        }

        /// <summary>Charge 3D de la carte graphique : de tout le PC (pid null) ou d'un seul programme.</summary>
        public double Gpu(int? pid)
        {
            var prefix = pid == null ? null : $"pid_{pid}_";
            double sum = 0;
            foreach (var (name, v) in _gpuItems)
                if (prefix == null || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) sum += v;
            return sum;
        }

        public double DiskIdle()
        {
            if (_disk == IntPtr.Zero) return 100;
            return PdhGetFormattedCounterValue(_disk, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out var v) == 0 && v.CStatus == 0 ? v.Value : 100;
        }

        private static Dictionary<string, double> ReadArray(IntPtr counter)
        {
            var result = new Dictionary<string, double>();
            uint size = 0, count;
            var rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, IntPtr.Zero);
            if (rc != PDH_MORE_DATA || size == 0) return result;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return result;
                // PDH_FMT_COUNTERVALUE_ITEM_W : nom (pointeur), puis { CStatus ; double } aligné sur 8 octets.
                const int item = 24, status = 8, value = 16;
                for (var i = 0; i < count; i++)
                {
                    var at = buf + i * item;
                    if (Marshal.ReadInt32(at + status) != 0) continue; // instance toute neuve : pas encore de valeur
                    var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(at)) ?? "";
                    var v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(at + value));
                    result[name] = result.GetValueOrDefault(name) + v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }

        private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        private const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double Value; }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);
    }
}
