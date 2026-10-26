using System.Diagnostics;
using QRCoder;

namespace PhoneDeckPC;

/// <summary>Petite fenêtre : QR code d'appairage, état, et accès au dossier de configuration.</summary>
public class MainForm : Form
{
    private readonly ConfigStore _store;
    private readonly DeckServer _server;
    private readonly AppCatalog _catalog;
    private readonly PictureBox _qr = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(280, 280) };
    private readonly ComboBox _ips = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly Label _code = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label _clients = new() { AutoSize = true };
    private readonly ListBox _log = new() { Width = 280, Height = 90, IntegralHeight = false };
    private readonly NotifyIcon _tray = new();
    private bool _exiting;
    private bool _trayHintShown;

    public MainForm(ConfigStore store, DeckServer server, AppCatalog catalog)
    {
        _store = store;
        _server = server;
        _catalog = catalog;

        Text = "PhoneDeck PC";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9.5f);
        Icon = SystemIcons.Application;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(new Label
        {
            Text = "Scanne ce QR code avec l'appli PhoneDeck\nsur ton téléphone (même Wi-Fi que le PC).",
            AutoSize = true,
        });
        layout.Controls.Add(_qr);
        layout.Controls.Add(_ips);
        layout.Controls.Add(_code);
        layout.Controls.Add(_clients);

        var openConfig = new Button { Text = "Ouvrir le dossier de configuration", Width = 280, Height = 32 };
        openConfig.Click += (_, _) => Process.Start(new ProcessStartInfo(_store.DataDir) { UseShellExecute = true });
        layout.Controls.Add(openConfig);

        var reload = new Button { Text = "Recharger la configuration", Width = 280, Height = 32 };
        reload.Click += (_, _) => _store.ReloadNow();
        layout.Controls.Add(reload);

        var apps = new Button { Text = "Voir la liste des applis détectées", Width = 280, Height = 32 };
        apps.Click += async (_, _) => await ShowAppListAsync();
        layout.Controls.Add(apps);

        layout.Controls.Add(new Label { Text = "Journal :", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(_log);
        Controls.Add(layout);

        foreach (var ip in LocalNetwork.Addresses()) _ips.Items.Add(ip);
        _ips.SelectedIndexChanged += (_, _) => UpdateQr();
        if (_ips.Items.Count > 0) _ips.SelectedIndex = 0;

        _code.Text = $"Code : {_store.Config.Token}    Port : {_store.Config.Port}";
        UpdateClients();
        if (_store.LastError != null) AddLog(_store.LastError);

        _server.ClientsChanged += () => BeginInvokeSafe(UpdateClients);
        _server.Log += m => BeginInvokeSafe(() => AddLog(m));

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "PhoneDeck PC";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Quitter", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fermer la fenêtre la range près de l'horloge : le téléphone reste connecté.
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "PhoneDeck PC", "Toujours actif ici. Clic droit > Quitter pour fermer.", ToolTipIcon.Info);
            }
            return;
        }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void UpdateQr()
    {
        if (_ips.SelectedItem is not string ip) return;
        var url = LocalNetwork.ConnectUrl(ip, _store);
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var old = _qr.Image;
        _qr.Image = Image.FromStream(new MemoryStream(png));
        old?.Dispose();
    }

    private void UpdateClients()
    {
        var names = _server.ClientNames;
        _clients.Text = names.Count == 0
            ? "Aucun téléphone connecté"
            : $"Connecté : {string.Join(", ", names)}";
    }

    private void AddLog(string message)
    {
        _log.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_log.Items.Count > 100) _log.Items.RemoveAt(_log.Items.Count - 1);
    }

    private void BeginInvokeSafe(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

    /// <summary>Écrit la liste des applis dans un fichier texte : pratique pour savoir quel nom mettre dans config.json.</summary>
    private async Task ShowAppListAsync()
    {
        await _catalog.RefreshAsync();
        var path = Path.Combine(_store.DataDir, "applis-detectees.txt");
        var lines = new List<string>
        {
            "Noms utilisables dans config.json (\"target\": \"Nom\") :",
            "",
        };
        lines.AddRange(_catalog.Apps.Select(a => a.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        await File.WriteAllLinesAsync(path, lines);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
