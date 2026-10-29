using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneDeckPC;

/// <summary>Morceau d'un message : du texte, ou une emote (id Twitch + son nom).</summary>
public record TwitchPart(string T, string? E = null);

/// <summary>Un message du chat, prêt à afficher (pseudo, couleur, morceaux de texte et emotes).</summary>
public record TwitchMessage(string Id, string User, string UserId, string Color, bool Action, List<TwitchPart> Parts, long Time);

/// <summary>
/// Lecture du chat d'une chaîne Twitch, en anonyme (pseudo « justinfan », sans compte ni clé) par le WebSocket IRC de Twitch.
/// Lecture seule : on ne peut rien écrire dans le chat. Se reconnecte tout seul.
/// </summary>
public class TwitchChat
{
    private const int Keep = 50;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly object _lock = new();
    private readonly List<TwitchMessage> _messages = new();
    private readonly Dictionary<string, byte[]> _emotes = new();
    private string _channel = "";
    private CancellationTokenSource? _cts;

    /// <summary>Nouveau message reçu.</summary>
    public event Action<TwitchMessage>? MessageAdded;
    /// <summary>Messages effacés par la modération : un id de message, ou tous ceux d'un utilisateur (userId), ou tout (les deux vides).</summary>
    public event Action<string?, string?>? Deleted;
    /// <summary>L'état de la connexion a changé.</summary>
    public event Action? StatusChanged;

    /// <summary>Chaîne lue (en minuscules, sans #), vide si aucune.</summary>
    public string Channel => _channel;
    /// <summary>off | connecting | ok | error</summary>
    public string State { get; private set; } = "off";
    public string Status { get; private set; } = "";

    public List<TwitchMessage> Recent(int count)
    {
        lock (_lock) return _messages.Skip(Math.Max(0, _messages.Count - count)).ToList();
    }

    /// <summary>« https://www.twitch.tv/Pseudo », « @pseudo » ou « pseudo » → « pseudo ».</summary>
    public static string Normalize(string? input)
    {
        var s = (input ?? "").Trim();
        var m = Regex.Match(s, @"twitch\.tv/([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
        if (m.Success) s = m.Groups[1].Value;
        s = s.TrimStart('@', '#').Trim().ToLowerInvariant();
        return Regex.IsMatch(s, "^[a-z0-9_]{1,25}$") ? s : "";
    }

    /// <summary>Lit cette chaîne (null ou vide = arrête). Ne fait rien si c'est déjà celle-là.</summary>
    public void Watch(string? channel)
    {
        var ch = Normalize(channel);
        if (ch == _channel) return;
        _cts?.Cancel();
        _cts = null;
        _channel = ch;
        lock (_lock) _messages.Clear();
        Deleted?.Invoke(null, null);
        if (ch.Length == 0)
        {
            SetState("off", "");
            return;
        }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => RunAsync(ch, ct));
    }

    private void SetState(string state, string status)
    {
        if (State == state && Status == status) return;
        State = state;
        Status = status;
        StatusChanged?.Invoke();
    }

    private async Task RunAsync(string channel, CancellationToken ct)
    {
        var wait = 2;
        while (!ct.IsCancellationRequested)
        {
            SetState("connecting", "Connexion au chat de " + channel + "…");
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), ct);
                async Task Send(string line) => await ws.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, ct);
                await Send("CAP REQ :twitch.tv/tags twitch.tv/commands");
                await Send("PASS SCHMOOPIIE");
                await Send("NICK justinfan" + Random.Shared.Next(10000, 99999));
                await Send("JOIN #" + channel);

                var buffer = new byte[16 * 1024];
                var text = new StringBuilder();
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    text.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    var lines = text.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                    text.Clear();
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("PING")) { await Send("PONG" + line[4..]); continue; }
                        if (HandleLine(line, channel)) wait = 2;
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { SetState("error", "Chat Twitch injoignable : " + e.Message); }
            if (ct.IsCancellationRequested) return;
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch { return; }
            wait = Math.Min(wait * 2, 60);
        }
    }

    /// <summary>Traite une ligne IRC. Renvoie true si on est bien dans le salon.</summary>
    private bool HandleLine(string line, string channel)
    {
        // Forme : [@tags] [:prefix] COMMANDE [params] [:texte]
        var tags = new Dictionary<string, string>();
        var rest = line;
        if (rest.StartsWith('@'))
        {
            var sp = rest.IndexOf(' ');
            if (sp < 0) return false;
            foreach (var kv in rest[1..sp].Split(';'))
            {
                var eq = kv.IndexOf('=');
                if (eq > 0) tags[kv[..eq]] = Unescape(kv[(eq + 1)..]);
            }
            rest = rest[(sp + 1)..];
        }
        var prefix = "";
        if (rest.StartsWith(':'))
        {
            var sp = rest.IndexOf(' ');
            if (sp < 0) return false;
            prefix = rest[1..sp];
            rest = rest[(sp + 1)..];
        }
        var trailing = "";
        var colon = rest.IndexOf(" :", StringComparison.Ordinal);
        if (colon >= 0) { trailing = rest[(colon + 2)..]; rest = rest[..colon]; }
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;

        switch (words[0])
        {
            case "ROOMSTATE":
            case "366": // fin de la liste des noms : on est dans le salon
                SetState("ok", "Chat de " + channel);
                return true;
            case "NOTICE":
                // Ex. chaîne suspendue ou inexistante.
                if (trailing.Length > 0 && State != "ok") SetState("error", trailing);
                return false;
            case "RECONNECT":
                throw new Exception("Twitch demande de se reconnecter");
            case "CLEARMSG":
                if (tags.TryGetValue("target-msg-id", out var mid)) Remove(mid, null);
                return true;
            case "CLEARCHAT":
                if (tags.TryGetValue("target-user-id", out var uid)) Remove(null, uid);
                else Remove(null, null);
                return true;
            case "PRIVMSG":
                AddMessage(tags, prefix, trailing);
                return true;
        }
        return false;
    }

    private void Remove(string? id, string? userId)
    {
        lock (_lock)
        {
            if (id != null) _messages.RemoveAll(m => m.Id == id);
            else if (userId != null) _messages.RemoveAll(m => m.UserId == userId);
            else _messages.Clear();
        }
        Deleted?.Invoke(id, userId);
    }

    private void AddMessage(Dictionary<string, string> tags, string prefix, string text)
    {
        var action = text.StartsWith("\u0001ACTION ") && text.EndsWith('\u0001');
        if (action) text = text[8..^1];
        var login = prefix.Split('!')[0];
        var user = tags.GetValueOrDefault("display-name") is { Length: > 0 } dn ? dn : login;
        var msg = new TwitchMessage(
            tags.GetValueOrDefault("id") ?? Guid.NewGuid().ToString("N"),
            user,
            tags.GetValueOrDefault("user-id") ?? login,
            tags.GetValueOrDefault("color") ?? "",
            action,
            Split(text, tags.GetValueOrDefault("emotes") ?? ""),
            tags.TryGetValue("tmi-sent-ts", out var ts) && long.TryParse(ts, out var t) ? t : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        lock (_lock)
        {
            _messages.Add(msg);
            if (_messages.Count > Keep) _messages.RemoveAt(0);
        }
        MessageAdded?.Invoke(msg);
    }

    /// <summary>Découpe le texte en morceaux texte / emote. Twitch donne les positions en caractères Unicode (pas en UTF-16).</summary>
    private static List<TwitchPart> Split(string text, string emotes)
    {
        var runes = text.EnumerateRunes().Select(r => r.ToString()).ToList();
        var spans = new List<(int Start, int End, string Id)>();
        foreach (var e in emotes.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = e.IndexOf(':');
            if (c <= 0) continue;
            var id = e[..c];
            foreach (var range in e[(c + 1)..].Split(','))
            {
                var d = range.Split('-');
                if (d.Length == 2 && int.TryParse(d[0], out var s) && int.TryParse(d[1], out var en) && s >= 0 && en >= s && en < runes.Count)
                    spans.Add((s, en, id));
            }
        }
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var parts = new List<TwitchPart>();
        var pos = 0;
        foreach (var (s, en, id) in spans)
        {
            if (s < pos) continue;
            if (s > pos) parts.Add(new TwitchPart(string.Concat(runes.Skip(pos).Take(s - pos))));
            parts.Add(new TwitchPart(string.Concat(runes.Skip(s).Take(en - s + 1)), id));
            pos = en + 1;
        }
        if (pos < runes.Count) parts.Add(new TwitchPart(string.Concat(runes.Skip(pos))));
        return parts;
    }

    private static string Unescape(string v)
    {
        if (!v.Contains('\\')) return v;
        var sb = new StringBuilder();
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] != '\\' || i == v.Length - 1) { sb.Append(v[i]); continue; }
            var c = v[++i];
            sb.Append(c switch { 's' => ' ', ':' => ';', 'r' => '\r', 'n' => '\n', _ => c });
        }
        return sb.ToString();
    }

    /// <summary>Image d'une emote (PNG ou GIF animé de Twitch), gardée en mémoire. Null si introuvable.</summary>
    public async Task<byte[]?> EmoteAsync(string id)
    {
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_]{1,64}$")) return null;
        lock (_emotes) if (_emotes.TryGetValue(id, out var cached)) return cached;
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/2.0");
            lock (_emotes)
            {
                if (_emotes.Count > 500) _emotes.Clear();
                _emotes[id] = bytes;
            }
            return bytes;
        }
        catch { return null; }
    }
}
