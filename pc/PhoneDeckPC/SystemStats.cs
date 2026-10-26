using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>Charge du processeur et de la mémoire, pour le widget « Système ».</summary>
public class SystemStats
{
    private ulong _lastIdle, _lastTotal;

    /// <summary>(CPU %, RAM %), entiers de 0 à 100. À appeler à intervalle régulier : le CPU est mesuré depuis le dernier appel.</summary>
    public (int Cpu, int Ram) Read()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0);
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
            return (cpu, ram);
        }
        catch
        {
            return (0, 0);
        }
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
}
