using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneDeckPC;

/// <summary>Une personne dans le salon vocal où l'on se trouve.</summary>
public record DiscordMember(string Id, string Name, bool Speaking, bool Muted, bool Deafened);

/// <summary>
/// Connexion à l'appli Discord du PC par son canal local (RPC, tuyau nommé « discord-ipc-0 »).
/// Donne l'état réel du micro et de la sourdine, le salon vocal en cours et qui parle.
/// Discord exige une « application » créée par l'utilisateur (identifiant + secret) et une autorisation
/// donnée une fois dans une fenêtre de Discord ; le jeton obtenu est gardé et renouvelé tout seul.
/// </summary>
public class DiscordClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string[] Scopes = { "rpc", "rpc.voice.read", "rpc.voice.write" };
    private const string RedirectUri = "http://localhost";

    private readonly IntegrationStore _store;
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, DiscordMember> _members = new();
    private readonly HashSet<string> _speaking = new();
    private CancellationTokenSource? _run;
    private NamedPipeClientStream? _pipe;
    private string? _channelId;
    private volatile bool _authorizeRequested;

    public bool Connected { get; private set; }
    public string Status { get; private set; } = "Désactivé";
    public bool Muted { get; private set; }
    public bool Deafened { get; private set; }
    public string ChannelName { get; private set; } = "";
    public string UserName { get; private set; } = "";

    public event Action? StateChanged;

    public DiscordClient(IntegrationStore store)
    {
        _store = store;
        _store.Changed += () => Restart(force: false);
    }

    public void Start() => Restart(force: true);

    /// <summary>Demande l'autorisation dans Discord (bouton « Connecter Discord » des réglages).</summary>
    public void Authorize()
    {
        _authorizeRequested = true;
        Restart(force: true);
    }

    /// <summary>Les personnes du salon vocal, celles qui parlent en premier.</summary>
    public List<DiscordMember> Members()
    {
        lock (_lock)
            return _members.Values.Select(m => m with { Speaking = _speaking.Contains(m.Id) })
                .OrderByDescending(m => m.Speaking).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private string? _runningFor;

    private void Restart(bool force)
    {
        var s = _store.Settings.Discord;
        // Un autre réglage a changé (OBS…) : on ne coupe pas Discord pour rien.
        var key = $"{s.Enabled}|{s.ClientId}|{s.ClientSecret}";
        if (!force && key == _runningFor) return;
        _runningFor = key;
        _run?.Cancel();
        try { _pipe?.Dispose(); } catch { }
        if (!s.Enabled) { SetDisconnected("Désactivé"); return; }
        if (string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrWhiteSpace(s.ClientSecret))
        {
            SetDisconnected("Il manque l'identifiant ou le secret de l'application Discord");
            return;
        }
        var cts = new CancellationTokenSource();
        _run = cts;
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    // ---------- Commandes des boutons ----------

    public async Task<string?> RunAsync(string command)
    {
        if (!_store.Settings.Discord.Enabled) return "Discord n'est pas connecté : active-le dans Réglages > Connexions.";
        if (!Connected) return "Discord : " + Status;
        try
        {
            switch (command.ToLowerInvariant())
            {
                case "mute":
                    // Rétablir le micro enlève aussi la sourdine, comme dans Discord.
                    await CommandAsync("SET_VOICE_SETTINGS", Muted ? (Deafened ? new JsonObject { ["deaf"] = false, ["mute"] = false } : new JsonObject { ["mute"] = false }) : new JsonObject { ["mute"] = true });
                    break;
                case "deafen":
                    await CommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["deaf"] = !Deafened });
                    break;
                case "leave":
                    await CommandAsync("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = null });
                    break;
                default: return "Commande Discord inconnue : " + command;
            }
            return null;
        }
        catch (Exception e)
        {
            return "Discord : " + e.Message;
        }
    }

    // ---------- Connexion ----------

    private async Task LoopAsync(CancellationToken ct)
    {
        var delay = 3;
        while (!ct.IsCancellationRequested)
        {
            if (!Connected) Status = "Connexion…";
            try
            {
                await SessionAsync(ct);
                delay = 3;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (NeedUserException e)
            {
                SetDisconnected(e.Message);
                return; // on attend que l'utilisateur clique sur « Connecter Discord »
            }
            catch (Exception e)
            {
                SetDisconnected(e is TimeoutException or IOException ? "Discord n'est pas ouvert" : e.Message);
            }
            if (ct.IsCancellationRequested) return;
            if (Connected) SetDisconnected("Discord a été fermé");
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { return; }
            delay = Math.Min(delay * 2, 20);
        }
    }

    private sealed class NeedUserException : Exception
    {
        public NeedUserException(string message) : base(message) { }
    }

    private async Task SessionAsync(CancellationToken ct)
    {
        var s = _store.Settings.Discord;
        var pipe = await OpenPipeAsync(ct) ?? throw new TimeoutException();
        _pipe = pipe;
        using var owned = pipe;
        await WriteFrameAsync(pipe, 0, new JsonObject { ["v"] = 1, ["client_id"] = s.ClientId.Trim() });
        var ready = await ReadFrameAsync(pipe, ct);
        if (ready == null || ready.Value.Op == 2)
            throw new NeedUserException("Discord refuse l'identifiant de l'application : vérifie-le");

        var reader = Task.Run(() => ReadLoopAsync(pipe, ct), ct);

        // Jeton : celui qu'on a, renouvelé s'il expire, sinon autorisation dans Discord.
        JsonObject? auth = null;
        if (s.AccessToken != null && s.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            auth = await TryAuthenticateAsync(s.AccessToken);
        if (auth == null && s.RefreshToken != null && await ExchangeAsync("refresh_token", s.RefreshToken))
            auth = await TryAuthenticateAsync(s.AccessToken!);
        if (auth == null)
        {
            if (!_authorizeRequested) throw new NeedUserException("Clique sur « Connecter Discord » puis accepte dans Discord");
            Status = "Accepte la demande dans la fenêtre de Discord…";
            StateChanged?.Invoke();
            JsonObject code;
            try
            {
                code = await CommandAsync("AUTHORIZE", new JsonObject
                {
                    ["client_id"] = s.ClientId.Trim(),
                    ["scopes"] = new JsonArray(Scopes.Select(x => (JsonNode)x).ToArray()),
                }, timeoutSeconds: 120);
            }
            catch (Exception e)
            {
                _authorizeRequested = false;
                throw new NeedUserException("Autorisation refusée dans Discord (" + e.Message + ")");
            }
            _authorizeRequested = false;
            if (!await ExchangeAsync("authorization_code", code["code"]?.GetValue<string>() ?? ""))
                throw new NeedUserException(Status);
            auth = await TryAuthenticateAsync(s.AccessToken!) ?? throw new NeedUserException("Discord refuse le jeton : reconnecte Discord");
        }

        var user = auth["user"];
        UserName = user?["global_name"]?.GetValue<string>() ?? user?["username"]?.GetValue<string>() ?? "";
        Connected = true;
        Status = UserName.Length > 0 ? "Connecté (" + UserName + ")" : "Connecté";

        await CommandAsync("SUBSCRIBE", null, "VOICE_SETTINGS_UPDATE");
        await CommandAsync("SUBSCRIBE", null, "VOICE_CHANNEL_SELECT");
        var vs = await CommandAsync("GET_VOICE_SETTINGS", null);
        Muted = vs["mute"]?.GetValue<bool>() ?? false;
        Deafened = vs["deaf"]?.GetValue<bool>() ?? false;
        await LoadChannelAsync();
        StateChanged?.Invoke();
        await reader;
    }

    private static async Task<NamedPipeClientStream?> OpenPipeAsync(CancellationToken ct)
    {
        // Discord ouvre discord-ipc-0, ou -1, -2… si plusieurs versions tournent (Canary, PTB).
        for (int i = 0; i < 10; i++)
        {
            var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await p.ConnectAsync(300, ct);
                return p;
            }
            catch (OperationCanceledException) { p.Dispose(); throw; }
            catch { p.Dispose(); }
        }
        return null;
    }

    private async Task<JsonObject?> TryAuthenticateAsync(string token)
    {
        try { return await CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = token }); }
        catch { return null; }
    }

    /// <summary>Échange un code (ou un jeton de renouvellement) contre un jeton d'accès, sur le site de Discord.</summary>
    private async Task<bool> ExchangeAsync(string grant, string value)
    {
        var s = _store.Settings.Discord;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = s.ClientId.Trim(),
            ["client_secret"] = s.ClientSecret.Trim(),
            ["grant_type"] = grant,
            ["redirect_uri"] = RedirectUri,
        };
        form[grant == "refresh_token" ? "refresh_token" : "code"] = value;
        try
        {
            using var res = await Http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(form));
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                if (grant != "refresh_token")
                    Status = body.Contains("redirect_uri") ? "Ajoute « http://localhost » dans OAuth2 > Redirects de ton application Discord"
                           : body.Contains("invalid_client") ? "Secret de l'application Discord refusé : vérifie-le"
                           : "Discord refuse l'autorisation (" + (int)res.StatusCode + ")";
                return false;
            }
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            s.AccessToken = r.GetProperty("access_token").GetString();
            if (r.TryGetProperty("refresh_token", out var rt)) s.RefreshToken = rt.GetString();
            s.ExpiresAt = DateTime.UtcNow.AddSeconds(r.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 604800);
            _store.Save(notify: false);
            return true;
        }
        catch (Exception e)
        {
            if (grant != "refresh_token") Status = "Pas d'accès à internet pour Discord (" + e.Message + ")";
            return false;
        }
    }

    /// <summary>Salon vocal actuel : liste des membres, et abonnement à « qui parle ».</summary>
    private async Task LoadChannelAsync()
    {
        var old = _channelId;
        JsonObject ch;
        try { ch = await CommandAsync("GET_SELECTED_VOICE_CHANNEL", null); }
        catch { ch = new JsonObject(); }
        var id = ch["id"]?.GetValue<string>();
        if (old != null && old != id)
        {
            foreach (var evt in new[] { "SPEAKING_START", "SPEAKING_STOP", "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE" })
                try { await CommandAsync("UNSUBSCRIBE", new JsonObject { ["channel_id"] = old }, evt); } catch { }
        }
        lock (_lock)
        {
            _members.Clear();
            _speaking.Clear();
            foreach (var v in ch["voice_states"] as JsonArray ?? new JsonArray())
                if (v is JsonObject o && Member(o) is { } m) _members[m.Id] = m;
        }
        _channelId = id;
        ChannelName = ch["name"]?.GetValue<string>() ?? "";
        if (id != null && id != old)
        {
            foreach (var evt in new[] { "SPEAKING_START", "SPEAKING_STOP", "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE" })
                try { await CommandAsync("SUBSCRIBE", new JsonObject { ["channel_id"] = id }, evt); } catch { }
        }
        StateChanged?.Invoke();
    }

    private static DiscordMember? Member(JsonObject v)
    {
        var user = v["user"];
        var id = user?["id"]?.GetValue<string>();
        if (id == null) return null;
        var name = v["nick"]?.GetValue<string>() ?? user?["global_name"]?.GetValue<string>() ?? user?["username"]?.GetValue<string>() ?? "?";
        var st = v["voice_state"];
        bool B(string k) => st?[k]?.GetValue<bool>() ?? false;
        return new DiscordMember(id, name, false, B("mute") || B("self_mute"), B("deaf") || B("self_deaf"));
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(pipe, ct);
                if (frame == null) return;
                var (op, msg) = frame.Value;
                if (op == 2) return; // Discord ferme la connexion
                if (op == 3) { await WriteFrameAsync(pipe, 4, msg); continue; } // ping → pong
                if (msg == null) continue;
                var nonce = msg["nonce"]?.GetValue<string>();
                if (nonce != null)
                {
                    TaskCompletionSource<JsonObject>? tcs;
                    lock (_pending) _pending.Remove(nonce, out tcs);
                    if (tcs != null)
                    {
                        if (msg["evt"]?.GetValue<string>() == "ERROR")
                            tcs.TrySetException(new Exception(msg["data"]?["message"]?.GetValue<string>() ?? "erreur"));
                        else tcs.TrySetResult(msg["data"] as JsonObject ?? new JsonObject());
                    }
                    continue;
                }
                if (msg["cmd"]?.GetValue<string>() == "DISPATCH")
                    OnEvent(msg["evt"]?.GetValue<string>() ?? "", msg["data"] as JsonObject ?? new JsonObject());
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            lock (_pending)
            {
                foreach (var t in _pending.Values) t.TrySetException(new Exception("connexion perdue"));
                _pending.Clear();
            }
        }
    }

    private void OnEvent(string evt, JsonObject data)
    {
        switch (evt)
        {
            case "VOICE_SETTINGS_UPDATE":
                if (data["mute"] != null) Muted = data["mute"]!.GetValue<bool>();
                if (data["deaf"] != null) Deafened = data["deaf"]!.GetValue<bool>();
                break;
            case "VOICE_CHANNEL_SELECT":
                _ = LoadChannelAsync();
                return;
            case "SPEAKING_START":
            case "SPEAKING_STOP":
            {
                var uid = data["user_id"]?.GetValue<string>();
                if (uid == null) return;
                lock (_lock)
                {
                    if (evt == "SPEAKING_START") _speaking.Add(uid); else _speaking.Remove(uid);
                }
                break;
            }
            case "VOICE_STATE_CREATE":
            case "VOICE_STATE_UPDATE":
                if (Member(data) is { } m) lock (_lock) _members[m.Id] = m;
                break;
            case "VOICE_STATE_DELETE":
                if (Member(data) is { } d) lock (_lock) { _members.Remove(d.Id); _speaking.Remove(d.Id); }
                break;
            default: return;
        }
        StateChanged?.Invoke();
    }

    private async Task<JsonObject> CommandAsync(string cmd, JsonObject? args, string? evt = null, int timeoutSeconds = 8)
    {
        var pipe = _pipe ?? throw new Exception("non connecté");
        var nonce = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[nonce] = tcs;
        var msg = new JsonObject { ["cmd"] = cmd, ["args"] = args ?? new JsonObject(), ["nonce"] = nonce };
        if (evt != null) msg["evt"] = evt;
        await WriteFrameAsync(pipe, 1, msg);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (done != tcs.Task)
        {
            lock (_pending) _pending.Remove(nonce);
            throw new Exception("Discord ne répond pas");
        }
        return await tcs.Task;
    }

    private async Task WriteFrameAsync(Stream pipe, int op, JsonNode? payload)
    {
        var body = Encoding.UTF8.GetBytes(payload?.ToJsonString() ?? "{}");
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        await _sendLock.WaitAsync();
        try
        {
            await pipe.WriteAsync(frame);
            await pipe.FlushAsync();
        }
        finally { _sendLock.Release(); }
    }

    private static async Task<(int Op, JsonObject? Msg)?> ReadFrameAsync(Stream pipe, CancellationToken ct)
    {
        var header = new byte[8];
        if (!await ReadExactAsync(pipe, header, ct)) return null;
        var op = BitConverter.ToInt32(header, 0);
        var len = BitConverter.ToInt32(header, 4);
        if (len is < 0 or > 4 * 1024 * 1024) return null;
        var body = new byte[len];
        if (!await ReadExactAsync(pipe, body, ct)) return null;
        JsonObject? msg = null;
        try { msg = JsonNode.Parse(body) as JsonObject; } catch { }
        return (op, msg);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    private void SetDisconnected(string status)
    {
        var was = Connected;
        Connected = false;
        Status = status;
        Muted = Deafened = false;
        ChannelName = "";
        _channelId = null;
        lock (_lock) { _members.Clear(); _speaking.Clear(); }
        if (was) StateChanged?.Invoke();
    }
}
