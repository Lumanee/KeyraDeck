using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PhoneDeckPC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Updater.MigrateFromLegacyExe(args)) return;
        Updater.CleanupOld();
        StartupRegistration.MigrateLegacy();
        var console = args.Contains("--console");
        var dataDir = args.SkipWhile(a => a != "--data").Skip(1).FirstOrDefault();

        var store = new ConfigStore(dataDir);
        var sta = new StaWorker();
        var catalog = new AppCatalog(sta);
        var icons = new IconService(catalog, store);
        var audio = new AudioMixer();
        var server = new DeckServer(store, catalog, icons, audio, sta);
        server.Api = new ConfigApi(store, catalog, sta, server);

        catalog.RefreshAsync().GetAwaiter().GetResult();

        if (console)
        {
            // Mode sans fenêtre (dépannage) : affiche les infos de connexion dans le terminal.
            server.Log += m => Console.WriteLine(m);
            server.StartAsync().GetAwaiter().GetResult();
            foreach (var ip in LocalNetwork.Addresses())
                Console.WriteLine(LocalNetwork.ConnectUrl(ip, store));
            Console.WriteLine($"Config : {store.ConfigPath}");
            var done = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
            done.Wait();
            server.StopAsync().GetAwaiter().GetResult();
            return;
        }

        RunWindow(store, server, catalog, args.Contains("--minimized"));
    }

    // Méthode séparée : le mode console ne charge jamais WinForms (utile pour tester hors Windows).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunWindow(ConfigStore store, DeckServer server, AppCatalog catalog, bool minimized)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            server.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            MessageBox.Show(
                $"Impossible de démarrer le serveur sur le port {store.Config.Port}.\n" +
                "KeyraDeck est peut-être déjà ouvert (regarde près de l'horloge).\n\n" + e.Message,
                "KeyraDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new MainForm(store, server, minimized));
        server.StopAsync().GetAwaiter().GetResult();
    }
}

public static class LocalNetwork
{
    /// <summary>Adresses IPv4 du PC sur le réseau local, la plus probable en premier.</summary>
    public static List<string> Addresses()
    {
        var found = new List<(string Ip, int Score)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                             && !g.Address.Equals(System.Net.IPAddress.Any));
            var desc = (nic.Description + nic.Name).ToLowerInvariant();
            var virtualNic = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v")
                             || desc.Contains("vethernet") || desc.Contains("wsl");
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = addr.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;
                var score = (hasGateway ? 4 : 0) + (virtualNic ? 0 : 2) + (ip.StartsWith("192.168.") ? 1 : 0);
                found.Add((ip, score));
            }
        }
        var list = found.OrderByDescending(f => f.Score).Select(f => f.Ip).Distinct().ToList();
        if (list.Count == 0) list.Add("127.0.0.1");
        return list;
    }

    public static string ConnectUrl(string ip, ConfigStore store) =>
        $"ws://{ip}:{store.Config.Port}/ws?token={store.Config.Token}";
}
