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

    /// <summary>Couleur du « châssis » de l'interface : le fond du thème (assombri pour les thèmes clairs). Même formule que config.html.</summary>
    private static Color ChromeColor(string hex)
    {
        try
        {
            var c = ColorTranslator.FromHtml(hex);
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            if (lum > 0.5) c = Color.FromArgb((int)(c.R * 0.12), (int)(c.G * 0.12), (int)(c.B * 0.12));
            return c;
        }
        catch { return Color.Black; }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Barre de titre de Windows à la couleur du thème, sans séparation avec l'interface (Windows 11).</summary>
    private void ApplyChrome()
    {
        try
        {
            if (!IsHandleCreated) return;
            var c = ChromeColor(_store.Config.Theme.Desk);
            BackColor = c;
            _web.DefaultBackgroundColor = c;
            int bgr = c.R | (c.G << 8) | (c.B << 16);
            int white = 0x00FFFFFF, dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);     // thème sombre (boutons de fenêtre clairs)
            DwmSetWindowAttribute(Handle, 35, ref bgr, 4);      // couleur de la barre de titre
            DwmSetWindowAttribute(Handle, 34, ref bgr, 4);      // bordure de la fenêtre
            DwmSetWindowAttribute(Handle, 36, ref white, 4);    // texte du titre
        }
        catch { /* Windows 10 : la barre garde sa couleur */ }
    }

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
        Text = "KeyraDeck";
        Icon = AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(17, 17, 20);
        Controls.Add(_web);

        _tray.Icon = AppIcon();
        _tray.Text = "KeyraDeck";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Quitter", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;

        if (server.Api != null)
            server.Api.ExitRequested = () => BeginInvoke(() => { _exiting = true; Close(); });

        Load += async (_, _) => await InitWebAsync();
        HandleCreated += (_, _) => ApplyChrome();
        void Refresh() { if (IsHandleCreated) BeginInvoke(ApplyChrome); }
        store.Changed += Refresh;
        store.Reloaded += Refresh;
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
                _tray.ShowBalloonTip(3000, "KeyraDeck", "Toujours actif ici. Clic droit > Quitter pour fermer.", ToolTipIcon.Info);
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
