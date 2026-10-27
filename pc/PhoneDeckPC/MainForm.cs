using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PhoneDeckPC;

/// <summary>
/// Fenêtre du PC : elle affiche le configurateur (une page web servie par l'appli elle-même).
/// Si WebView2 n'est pas disponible, la page s'ouvre dans le navigateur.
/// </summary>
public class MainForm : Form
{
    private readonly ConfigStore _store;
    private readonly NotifyIcon _tray = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private bool _exiting;
    private bool _trayHintShown;

    private static Icon AppIcon()
    {
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    public MainForm(ConfigStore store, DeckServer server, bool minimized = false)
    {
        _store = store;
        Text = "PhoneDeck PC";
        Icon = AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(17, 17, 20);
        Controls.Add(_web);

        _tray.Icon = AppIcon();
        _tray.Text = "PhoneDeck PC";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Quitter", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;

        if (server.Api != null)
            server.Api.ExitRequested = () => BeginInvoke(() => { _exiting = true; Close(); });

        Load += async (_, _) => await InitWebAsync();
        // Lancé avec Windows : on reste discret, près de l'horloge.
        if (minimized) { ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Shown += (_, _) => { Hide(); ShowInTaskbar = true; }; }
    }

    private string Url => $"http://localhost:{_store.Config.Port}/";

    private async Task InitWebAsync()
    {
        try
        {
            var dir = Path.Combine(_store.DataDir, "webview");
            var env = await CoreWebView2Environment.CreateAsync(null, dir);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // Les liens externes s'ouvrent dans le navigateur, pas dans la fenêtre.
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            };
            _web.CoreWebView2.Navigate(Url);
        }
        catch (Exception)
        {
            // WebView2 absent : on ouvre le configurateur dans le navigateur habituel.
            Controls.Remove(_web);
            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12),
                Text = "Le configurateur s'est ouvert dans ton navigateur.\n\n" + Url +
                       "\n\n(Pour l'avoir dans cette fenêtre, installe « Microsoft Edge WebView2 Runtime ».)",
            };
            Controls.Add(label);
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
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
}
