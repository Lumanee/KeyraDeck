using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace PhoneDeckPC;

/// <summary>Une notification lue dans le centre de notifications de Windows.</summary>
public record WinNotif(string Id, string App, string Title, string Text, long Time);

/// <summary>
/// Lit les notifications de Windows (Discord, Outlook, WhatsApp…) pour les montrer sur le téléphone.
/// Windows demande l'accord de l'utilisateur (Paramètres &gt; Confidentialité &gt; Notifications).
/// KeyraDeck n'est pas une appli du Store : on relit la liste toutes les 2 secondes, l'événement
/// « nouvelle notification » de Windows n'étant réservé qu'aux applis du Store.
/// </summary>
public sealed class WindowsNotifications
{
    /// <summary>Fil de la fenêtre : Windows veut que la demande d'accord parte de là.</summary>
    public static SynchronizationContext? Ui { get; set; }

    private readonly Dictionary<string, WinNotif> _known = new();
    private bool _primed;
    private bool _asked;
    private DateTime _nextTry = DateTime.MinValue;

    /// <summary>Nouvelle notification apparue dans Windows.</summary>
    public event Action<WinNotif>? Added;
    /// <summary>Notification fermée ou effacée dans Windows (id).</summary>
    public event Action<string>? Removed;

    /// <summary>« ok », « refused », « unsupported », « off » ou « waiting ».</summary>
    public string State { get; private set; } = "off";

    /// <summary>Phrase lisible pour le configurateur.</summary>
    public string Status => State switch
    {
        "ok" => "Notifications Windows reçues",
        "refused" => "Windows refuse l'accès aux notifications",
        "unsupported" => "Ce Windows ne permet pas de lire les notifications",
        "waiting" => "En attente de l'accord de Windows",
        _ => "Désactivé",
    };

    /// <summary>Les notifications encore visibles dans Windows, la plus récente en premier.</summary>
    public List<WinNotif> Current()
    {
        lock (_known) return _known.Values.OrderByDescending(n => n.Time).ToList();
    }

    public void Stop()
    {
        State = "off";
        _primed = false;
        lock (_known) _known.Clear();
    }

    /// <summary>Relit le centre de notifications (appelé chaque seconde par le serveur). Ne lève jamais d'erreur.</summary>
    public async Task PollAsync()
    {
        if (DateTime.UtcNow < _nextTry) return;
        _nextTry = DateTime.UtcNow.AddSeconds(2);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) { State = "unsupported"; return; }
        UserNotificationListener listener;
        try { listener = UserNotificationListener.Current; }
        catch { State = "unsupported"; _nextTry = DateTime.UtcNow.AddMinutes(1); return; }

        try
        {
            var access = listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified && !_asked)
            {
                _asked = true;
                State = "waiting";
                access = await RequestAccessAsync(listener);
            }
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                State = "refused";
                _nextTry = DateTime.UtcNow.AddSeconds(10); // l'utilisateur peut l'autoriser dans Paramètres entre-temps
                return;
            }

            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            State = "ok";
            var seen = new HashSet<string>();
            var added = new List<WinNotif>();
            foreach (var n in list)
            {
                var id = "w" + n.Id;
                seen.Add(id);
                lock (_known) if (_known.ContainsKey(id)) continue;
                var read = Read(n, id);
                if (read == null) continue;
                lock (_known) _known[id] = read;
                added.Add(read);
            }
            List<string> gone;
            lock (_known)
            {
                gone = _known.Keys.Where(k => !seen.Contains(k)).ToList();
                foreach (var k in gone) _known.Remove(k);
            }
            // Première lecture : les anciennes notifications vont dans la liste sans bandeau sur le téléphone.
            if (_primed)
            {
                foreach (var n in added.OrderBy(n => n.Time)) Added?.Invoke(n);
                foreach (var id in gone) Removed?.Invoke(id);
            }
            _primed = true;
        }
        catch
        {
            // Accès refusé en cours de route, service arrêté… : on réessaie un peu plus tard.
            State = "unsupported";
            _nextTry = DateTime.UtcNow.AddSeconds(30);
        }
    }

    private static async Task<UserNotificationListenerAccessStatus> RequestAccessAsync(UserNotificationListener listener)
    {
        var ui = Ui;
        if (ui == null)
        {
            try { return await listener.RequestAccessAsync(); }
            catch { return listener.GetAccessStatus(); }
        }
        var tcs = new TaskCompletionSource<UserNotificationListenerAccessStatus>();
        ui.Post(async _ =>
        {
            try { tcs.TrySetResult(await listener.RequestAccessAsync()); }
            catch { tcs.TrySetResult(listener.GetAccessStatus()); }
        }, null);
        return await tcs.Task;
    }

    private static WinNotif? Read(UserNotification n, string id)
    {
        try
        {
            var app = "";
            try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }
            // Nos propres messages (mise à jour…) sont déjà affichés par KeyraDeck.
            if (app.Equals("KeyraDeck", StringComparison.OrdinalIgnoreCase)) return null;
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? new List<string>();
            if (texts.Count == 0 && app.Length == 0) return null;
            var title = texts.Count > 0 ? texts[0] : "";
            var text = string.Join("\n", texts.Skip(1));
            if (title.Length > 200) title = title[..200];
            if (text.Length > 600) text = text[..600];
            return new WinNotif(id, app, title, text, n.CreationTime.ToUnixTimeMilliseconds());
        }
        catch { return null; }
    }
}
