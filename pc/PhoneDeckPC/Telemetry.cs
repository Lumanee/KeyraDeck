using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;

namespace PhoneDeckPC;

/// <summary>Un instant de la voiture du joueur. Rapport : -1 = marche arrière, 0 = point mort.</summary>
public sealed record TelemetryData(string Game, int Speed, int Gear, int Rpm, int MaxRpm, int Throttle, int Brake, int Rev, bool Demo);

/// <summary>
/// Télémétrie des jeux de course, comme SimHub : vitesse, rapport, régime, accélérateur et frein.
/// - Assetto Corsa, Assetto Corsa Competizione (et EVO s'il garde le même format) : mémoire partagée « acpmf_* »
///   que le jeu ouvre tout seul, rien à régler.
/// - F1 de Codemasters / EA (F1 2021 à F1 25) : paquets UDP, à activer dans le jeu
///   (Réglages > Télémétrie : UDP activé, port 20777, format de l'année du jeu).
/// Si aucun jeu ne donne de données et que le mode démo est choisi, une voiture fictive tourne en boucle.
/// </summary>
public sealed class Telemetry
{
    private readonly object _lock = new();
    private bool _on, _demo;
    private int _f1Port;
    private UdpClient? _udp;
    private CancellationTokenSource? _udpStop;

    // F1 : dernier paquet reçu.
    private TelemetryData? _f1;
    private long _f1At;
    private int _f1MaxRpm;

    // Assetto Corsa : pages de mémoire partagée (ouvertes quand le jeu tourne).
    private MemoryMappedFile? _acPhysFile, _acGraphFile, _acStaticFile;
    private MemoryMappedViewAccessor? _acPhys, _acGraph, _acStatic;
    private long _acNextTry, _acLastPacketAt, _acNameAt;
    private int _acLastPacket = int.MinValue, _acSeenMax;
    private string _acName = "Assetto Corsa";

    // Démo.
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _demoSpeed, _demoLast;

    /// <summary>Ce que le configurateur affiche sous le widget (ex. « Port 20777 déjà pris »).</summary>
    public string Status { get; private set; } = "";

    /// <summary>Allume ou éteint la lecture (seulement quand une page a un widget de télémétrie).</summary>
    public void Watch(bool on, int f1Port, bool demo)
    {
        lock (_lock)
        {
            _demo = demo;
            if (on == _on && f1Port == _f1Port) return;
            _on = on;
            _f1Port = f1Port;
            StopUdp();
            if (!on) { CloseAc(); Status = ""; return; }
            StartUdp();
        }
    }

    /// <summary>La voiture du joueur maintenant, ou null si aucun jeu n'envoie rien.</summary>
    public TelemetryData? Current()
    {
        lock (_lock)
        {
            if (!_on) return null;
            var now = _clock.ElapsedMilliseconds;
            var ac = ReadAc(now);
            if (ac != null) return ac;
            if (_f1 != null && now - _f1At < 1500) return _f1;
            return _demo ? Demo(now) : null;
        }
    }

    // ---------- F1 (UDP) ----------

    private void StartUdp()
    {
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _f1Port));
            Status = "";
        }
        catch (SocketException)
        {
            _udp = null;
            Status = $"Port {_f1Port} déjà pris (SimHub ou un autre logiciel ?) : F1 ne peut pas être lu.";
            return;
        }
        var stop = _udpStop = new CancellationTokenSource();
        var udp = _udp;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var r = await udp.ReceiveAsync(stop.Token);
                    OnF1Packet(r.Buffer);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch { /* paquet raté : on attend le suivant */ }
            }
        });
    }

    private void StopUdp()
    {
        _udpStop?.Cancel();
        _udpStop = null;
        try { _udp?.Dispose(); } catch { }
        _udp = null;
        _f1 = null;
    }

    private void OnF1Packet(byte[] b)
    {
        if (b.Length < 24) return;
        int format = BitConverter.ToUInt16(b, 0);
        if (format < 2021 || format > 2099) return;
        // Depuis F1 23 l'en-tête fait 29 octets (année du jeu et numéro d'image en plus).
        bool newHeader = format >= 2023;
        int header = newHeader ? 29 : 24;
        int packetId = b[newHeader ? 6 : 5];
        int player = b[newHeader ? 27 : 22];
        if (player > 21) return;
        string game = newHeader ? $"F1 {b[2]:00}" : $"F1 {format}";
        if (packetId == 7)
        {
            // État de la voiture : régime maximum (rupteur).
            int stride = newHeader ? 55 : 47;
            int o = header + player * stride + 17;
            if (o + 2 > b.Length) return;
            int max = BitConverter.ToUInt16(b, o);
            if (max is > 3000 and < 25000) _f1MaxRpm = max;
            return;
        }
        if (packetId != 6) return;
        // Télémétrie : 60 octets par voiture.
        int c = header + player * 60;
        if (c + 18 > b.Length) return;
        int speed = BitConverter.ToUInt16(b, c);
        float throttle = BitConverter.ToSingle(b, c + 2);
        float brake = BitConverter.ToSingle(b, c + 10);
        int gear = (sbyte)b[c + 15];
        int rpm = BitConverter.ToUInt16(b, c + 16);
        int rev = c + 20 <= b.Length ? b[c + 19] : 0; // LED de régime du jeu, en %
        lock (_lock)
        {
            int maxRpm = _f1MaxRpm > 0 ? _f1MaxRpm : Math.Max(rpm, _f1?.MaxRpm ?? 0);
            _f1 = new TelemetryData(game, speed, Math.Clamp(gear, -1, 9), rpm, maxRpm, Pct(throttle), Pct(brake), Math.Clamp(rev, 0, 100), false);
            _f1At = _clock.ElapsedMilliseconds;
        }
    }

    // ---------- Assetto Corsa / ACC (mémoire partagée) ----------

    private TelemetryData? ReadAc(long now)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (_acPhys == null)
        {
            if (now < _acNextTry) return null;
            _acNextTry = now + 2000; // le jeu n'est pas lancé : on réessaie toutes les 2 secondes
            try
            {
                _acPhysFile = MemoryMappedFile.OpenExisting("Local\\acpmf_physics", MemoryMappedFileRights.Read);
                _acGraphFile = MemoryMappedFile.OpenExisting("Local\\acpmf_graphics", MemoryMappedFileRights.Read);
                _acStaticFile = MemoryMappedFile.OpenExisting("Local\\acpmf_static", MemoryMappedFileRights.Read);
                _acPhys = _acPhysFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _acGraph = _acGraphFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _acStatic = _acStaticFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _acSeenMax = 0;
                _acNameAt = 0;
            }
            catch { CloseAc(); return null; }
        }
        try
        {
            // Graphique : 0 = éteint, 1 = replay, 2 = en piste, 3 = pause.
            var phys = _acPhys!;
            int status = _acGraph!.ReadInt32(4);
            int packet = phys.ReadInt32(0);
            if (packet != _acLastPacket) { _acLastPacket = packet; _acLastPacketAt = now; }
            bool live = status == 3 || (status == 2 && now - _acLastPacketAt < 2000);
            if (!live)
            {
                // Jeu fermé (ou resté ouvert par un autre logiciel) : on relâche la mémoire de temps en temps.
                if (now - _acLastPacketAt > 10000) { CloseAc(); _acNextTry = now + 2000; }
                return null;
            }
            float gas = phys.ReadSingle(4);
            float brake = phys.ReadSingle(8);
            int gear = phys.ReadInt32(16) - 1; // AC : 0 = arrière, 1 = point mort
            int rpm = phys.ReadInt32(20);
            float speed = phys.ReadSingle(28);
            int maxRpm = _acStatic!.Capacity >= 416 ? _acStatic.ReadInt32(412) : 0;
            _acSeenMax = Math.Max(_acSeenMax, rpm);
            if (maxRpm is < 1000 or > 25000) maxRpm = _acSeenMax;
            if (now - _acNameAt > 5000) { _acNameAt = now; _acName = AcGameName(); }
            // LED : elles s'allument de 80 % à 97 % du régime maximum.
            int rev = maxRpm <= 0 ? 0 : (int)Math.Round(Math.Clamp((rpm / (double)maxRpm - 0.80) / 0.17, 0, 1) * 100);
            return new TelemetryData(_acName, (int)Math.Round(Math.Max(0, speed)), Math.Clamp(gear, -1, 9), Math.Max(0, rpm), maxRpm, Pct(gas), Pct(brake), rev, false);
        }
        catch { CloseAc(); return null; }
    }

    private static string AcGameName()
    {
        static bool Running(string name) { try { var p = Process.GetProcessesByName(name); foreach (var x in p) x.Dispose(); return p.Length > 0; } catch { return false; } }
        if (Running("AC2-Win64-Shipping")) return "Assetto Corsa Competizione";
        if (Running("acs") || Running("acs_x86")) return "Assetto Corsa";
        return "Assetto Corsa EVO";
    }

    private void CloseAc()
    {
        foreach (var d in new IDisposable?[] { _acPhys, _acGraph, _acStatic, _acPhysFile, _acGraphFile, _acStaticFile })
            try { d?.Dispose(); } catch { }
        _acPhys = _acGraph = _acStatic = null;
        _acPhysFile = _acGraphFile = _acStaticFile = null;
        _acLastPacket = int.MinValue;
    }

    // ---------- Démo ----------

    private static readonly int[] GearTop = { 0, 72, 108, 142, 178, 214, 252, 290 }; // vitesse max de chaque rapport (km/h)

    /// <summary>Une voiture fictive : pleine accélération, gros freinage, virage, et on recommence (16 secondes).</summary>
    private TelemetryData Demo(long now)
    {
        double t = now / 1000.0;
        double dt = Math.Clamp(t - _demoLast, 0, 0.2);
        _demoLast = t;
        double cyc = t % 16.0;
        double thr, brk;
        if (cyc < 9) { thr = 1; brk = 0; }
        else if (cyc < 11.5) { thr = 0; brk = 0.95 - (cyc - 9) / 2.5 * 0.6; }
        else if (cyc < 13) { thr = 0.35 + (cyc - 11.5) * 0.2; brk = 0; }
        else { thr = 1; brk = 0; }
        double accel = thr * 9.0 * (1 - _demoSpeed / 300.0) - brk * 32.0 - 1.2;
        _demoSpeed = Math.Clamp(_demoSpeed + accel * dt * 3.6, 40, 295);
        int gear = 1;
        while (gear < GearTop.Length - 1 && _demoSpeed > GearTop[gear] * 0.96) gear++;
        const int maxRpm = 8500;
        int rpm = (int)Math.Clamp(_demoSpeed / GearTop[gear] * maxRpm, 1800, maxRpm);
        int rev = (int)Math.Round(Math.Clamp((rpm / (double)maxRpm - 0.80) / 0.17, 0, 1) * 100);
        return new TelemetryData("Démo", (int)_demoSpeed, gear, rpm, maxRpm, Pct(thr), Pct(brk), rev, true);
    }

    private static int Pct(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 100);
}
