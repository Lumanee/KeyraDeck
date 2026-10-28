using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneDeckPC;

/// <summary>
/// Connexion à OBS Studio par obs-websocket (version 5, inclus dans OBS depuis la version 28).
/// Garde à jour l'état du live, de l'enregistrement, de la scène en cours et des micros coupés,
/// et envoie les commandes des boutons. Se reconnecte tout seul quand OBS est relancé.
/// </summary>
public class ObsClient
{
    // Événements écoutés : Général (1) + Scènes (4) + Sources (8) + Sorties (64).
    private const int EventMask = 1 | 4 | 8 | 64;

    private readonly IntegrationStore _store;
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<string, bool> _inputMuted = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _run;
    private ClientWebSocket? _ws;
    private int _nextId;

    public bool Connected { get; private set; }
    /// <summary>Texte affiché dans les réglages (« Connecté », « OBS n'est pas ouvert »…).</summary>
    public string Status { get; private set; } = "Désactivé";
    public bool Streaming { get; private set; }
    public bool Recording { get; private set; }
    public bool RecordPaused { get; private set; }
    public bool ReplayActive { get; private set; }
    public bool VirtualCam { get; private set; }
    public string CurrentScene { get; private set; } = "";
    public List<string> Scenes { get; private set; } = new();
    public List<string> AudioInputs { get; private set; } = new();

    public event Action? StateChanged;

    public ObsClient(IntegrationStore store)
    {
        _store = store;
        _store.Changed += () => Restart(force: false);
    }

    public void Start() => Restart(force: true);

    private string? _runningFor;

    private void Restart(bool force)
    {
        var s = _store.Settings.Obs;
        // Un autre réglage a changé (Discord…) : on ne coupe pas OBS pour rien.
        var key = $"{s.Enabled}|{s.Host}|{s.Port}|{s.Password}";
        if (!force && key == _runningFor) return;
        _runningFor = key;
        _run?.Cancel();
        try { _ws?.Abort(); } catch { }
        if (!s.Enabled)
        {
            SetDisconnected("Désactivé");
            return;
        }
        var cts = new CancellationTokenSource();
        _run = cts;
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    public bool IsInputMuted(string name)
    {
        lock (_lock) return _inputMuted.GetValueOrDefault(name);
    }

    // ---------- Commandes des boutons ----------

    /// <summary>Exécute une commande de bouton. Renvoie un message d'erreur, ou null si tout va bien.</summary>
    public async Task<string?> RunAsync(string command, string? target)
    {
        if (!_store.Settings.Obs.Enabled) return "OBS n'est pas connecté : active-le dans Réglages > Connexions.";
        if (!Connected) return "OBS n'est pas ouvert ou refuse la connexion (" + Status + ").";
        try
        {
            switch (command.ToLowerInvariant())
            {
                case "stream": await RequestAsync("ToggleStream"); break;
                case "record": await RequestAsync("ToggleRecord"); break;
                case "pause":
                    if (!Recording) return "Aucun enregistrement en cours.";
                    await RequestAsync("ToggleRecordPause");
                    break;
                case "replay":
                    // Premier appui : lance la mémoire de replay ; ensuite, chaque appui enregistre les dernières secondes.
                    if (ReplayActive) await RequestAsync("SaveReplayBuffer");
                    else await RequestAsync("StartReplayBuffer");
                    break;
                case "virtualcam": await RequestAsync("ToggleVirtualCam"); break;
                case "scene":
                    if (string.IsNullOrWhiteSpace(target)) return "Choisis la scène dans le configurateur.";
                    await RequestAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = target });
                    break;
                case "mute":
                    if (string.IsNullOrWhiteSpace(target)) return "Choisis la source audio dans le configurateur.";
                    await RequestAsync("ToggleInputMute", new JsonObject { ["inputName"] = target });
                    break;
                default: return "Commande OBS inconnue : " + command;
            }
            return null;
        }
        catch (Exception e)
        {
            return "OBS : " + e.Message;
        }
    }

    // ---------- Connexion ----------

    private async Task LoopAsync(CancellationToken ct)
    {
        var delay = 3;
        while (!ct.IsCancellationRequested)
        {
            Status = "Connexion…";
            try
            {
                await SessionAsync(ct);
                delay = 3;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                SetDisconnected(Explain(e));
            }
            if (ct.IsCancellationRequested) return;
            if (Connected) SetDisconnected("OBS a été fermé");
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { return; }
            delay = Math.Min(delay * 2, 15);
        }
    }

    private static string Explain(Exception e) => e switch
    {
        WebSocketException { InnerException: System.Net.Http.HttpRequestException } => "OBS n'est pas ouvert, ou son serveur WebSocket est désactivé",
        WebSocketException => "OBS n'est pas ouvert, ou son serveur WebSocket est désactivé",
        System.Net.Http.HttpRequestException => "OBS n'est pas ouvert, ou son serveur WebSocket est désactivé",
        _ => e.Message,
    };

    private async Task SessionAsync(CancellationToken ct)
    {
        var s = _store.Settings.Obs;
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("obswebsocket.json");
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var host = string.IsNullOrWhiteSpace(s.Host) ? "localhost" : s.Host.Trim();
            await ws.ConnectAsync(new Uri($"ws://{host}:{s.Port}"), timeout.Token);
        }
        _ws = ws;

        // 1. Hello (avec un défi si un mot de passe est demandé) → 2. Identify → 3. Identified.
        var hello = await ReceiveAsync(ws, ct) ?? throw new Exception("OBS ne répond pas");
        var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = EventMask };
        if (hello["d"]?["authentication"] is JsonObject auth)
        {
            if (string.IsNullOrEmpty(s.Password)) throw new Exception("OBS demande un mot de passe : copie-le dans les réglages");
            identify["authentication"] = AuthString(s.Password, auth["salt"]!.GetValue<string>(), auth["challenge"]!.GetValue<string>());
        }
        await SendAsync(ws, new JsonObject { ["op"] = 1, ["d"] = identify });
        var identified = await ReceiveAsync(ws, ct);
        if (identified == null)
        {
            if (ws.CloseStatus == (WebSocketCloseStatus)4009) throw new Exception("Mot de passe OBS refusé");
            throw new Exception("OBS a refusé la connexion (" + ws.CloseStatusDescription + ")");
        }

        var reader = Task.Run(() => ReadLoopAsync(ws, ct), ct);
        Connected = true;
        Status = "Connecté";
        await RefreshAllAsync();
        await reader;
    }

    private async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await ReceiveAsync(ws, ct);
                if (msg == null) return;
                var op = msg["op"]?.GetValue<int>();
                var d = msg["d"] as JsonObject;
                if (d == null) continue;
                if (op == 7)
                {
                    var id = d["requestId"]?.GetValue<string>() ?? "";
                    TaskCompletionSource<JsonObject>? tcs;
                    lock (_pending) { _pending.Remove(id, out tcs); }
                    tcs?.TrySetResult(d);
                }
                else if (op == 5)
                {
                    OnEvent(d["eventType"]?.GetValue<string>() ?? "", d["eventData"] as JsonObject ?? new JsonObject());
                }
            }
        }
        finally
        {
            lock (_pending)
            {
                foreach (var t in _pending.Values) t.TrySetException(new Exception("connexion perdue"));
                _pending.Clear();
            }
        }
    }

    private void OnEvent(string type, JsonObject data)
    {
        bool Active() => data["outputActive"]?.GetValue<bool>() ?? false;
        switch (type)
        {
            case "StreamStateChanged": Streaming = Active(); break;
            case "RecordStateChanged":
                Recording = Active();
                var st = data["outputState"]?.GetValue<string>() ?? "";
                if (st.EndsWith("PAUSED")) RecordPaused = true;
                else if (st.EndsWith("RESUMED") || !Recording) RecordPaused = false;
                break;
            case "ReplayBufferStateChanged": ReplayActive = Active(); break;
            case "VirtualcamStateChanged": VirtualCam = Active(); break;
            case "CurrentProgramSceneChanged": CurrentScene = data["sceneName"]?.GetValue<string>() ?? ""; break;
            case "InputMuteStateChanged":
                lock (_lock) _inputMuted[data["inputName"]?.GetValue<string>() ?? ""] = data["inputMuted"]?.GetValue<bool>() ?? false;
                break;
            case "SceneListChanged": case "SceneNameChanged": case "SceneCreated": case "SceneRemoved":
            case "InputCreated": case "InputRemoved": case "InputNameChanged":
                _ = RefreshListsAsync();
                return;
            case "ExitStarted":
                _ws?.Abort();
                return;
            default: return;
        }
        StateChanged?.Invoke();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var stream = await RequestAsync("GetStreamStatus");
            Streaming = stream["outputActive"]?.GetValue<bool>() ?? false;
            var rec = await RequestAsync("GetRecordStatus");
            Recording = rec["outputActive"]?.GetValue<bool>() ?? false;
            RecordPaused = rec["outputPaused"]?.GetValue<bool>() ?? false;
            try { ReplayActive = (await RequestAsync("GetReplayBufferStatus"))["outputActive"]?.GetValue<bool>() ?? false; } catch { ReplayActive = false; }
            try { VirtualCam = (await RequestAsync("GetVirtualCamStatus"))["outputActive"]?.GetValue<bool>() ?? false; } catch { VirtualCam = false; }
        }
        catch { }
        await RefreshListsAsync();
    }

    private async Task RefreshListsAsync()
    {
        try
        {
            var scenes = await RequestAsync("GetSceneList");
            CurrentScene = scenes["currentProgramSceneName"]?.GetValue<string>() ?? "";
            // OBS donne les scènes de bas en haut : on les remet dans l'ordre de sa liste.
            Scenes = (scenes["scenes"] as JsonArray ?? new JsonArray())
                .Select(x => x?["sceneName"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).Reverse().ToList();

            var inputs = await RequestAsync("GetInputList");
            var audio = new List<string>();
            var muted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in inputs["inputs"] as JsonArray ?? new JsonArray())
            {
                var name = i?["inputName"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    // Seules les sources qui ont du son répondent à GetInputMute.
                    var m = await RequestAsync("GetInputMute", new JsonObject { ["inputName"] = name });
                    muted[name] = m["inputMuted"]?.GetValue<bool>() ?? false;
                    audio.Add(name);
                }
                catch { }
            }
            lock (_lock)
            {
                _inputMuted.Clear();
                foreach (var (k, v) in muted) _inputMuted[k] = v;
            }
            AudioInputs = audio;
        }
        catch { }
        StateChanged?.Invoke();
    }

    private async Task<JsonObject> RequestAsync(string type, JsonObject? data = null)
    {
        var ws = _ws ?? throw new Exception("non connecté");
        var id = Interlocked.Increment(ref _nextId).ToString();
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data != null) d["requestData"] = data;
        await SendAsync(ws, new JsonObject { ["op"] = 6, ["d"] = d });
        var done = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        if (done != tcs.Task)
        {
            lock (_pending) _pending.Remove(id);
            throw new Exception("OBS ne répond pas");
        }
        var res = await tcs.Task;
        var status = res["requestStatus"];
        if (status?["result"]?.GetValue<bool>() != true)
            throw new Exception(status?["comment"]?.GetValue<string>() ?? "requête refusée");
        return res["responseData"] as JsonObject ?? new JsonObject();
    }

    private void SetDisconnected(string status)
    {
        var was = Connected;
        Connected = false;
        Status = status;
        Streaming = Recording = RecordPaused = ReplayActive = VirtualCam = false;
        if (was) StateChanged?.Invoke();
    }

    /// <summary>Calcul d'authentification d'obs-websocket : base64(sha256(base64(sha256(mdp + sel)) + défi)).</summary>
    public static string AuthString(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private async Task SendAsync(ClientWebSocket ws, JsonObject msg)
    {
        var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sendLock.Release(); }
    }

    private static async Task<JsonObject?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buffer, ct); }
            catch (WebSocketException) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            if (ms.Length > 16 * 1024 * 1024) return null;
            if (r.EndOfMessage) break;
        }
        return JsonNode.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)) as JsonObject;
    }
}
