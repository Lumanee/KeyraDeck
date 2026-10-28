using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PhoneDeckPC;

/// <summary>
/// Serveur WebSocket auquel le téléphone se connecte (ws://IP:port/ws?token=CODE).
/// Le PC garde la configuration ; le téléphone affiche ce qu'on lui envoie et renvoie les appuis.
/// </summary>
public class DeckServer
{
    private static readonly JsonSerializerOptions Wire = new(ConfigStore.Json) { WriteIndented = false };

    private readonly ConfigStore _store;
    private readonly AppCatalog _catalog;
    private readonly IconService _icons;
    private readonly AudioMixer _audio;
    private readonly StaWorker _sta;
    private readonly List<Client> _clients = new();
    private readonly SemaphoreSlim _layoutLock = new(1, 1);
    private readonly Dictionary<string, bool> _toggles = new();
    private string _lastStates = "";
    private string? _autoPage;
    private readonly SystemStats _systemStats = new();
    private bool _needVolume, _needSystem, _needWeather;
    private readonly WeatherService _weather = new();
    private CancellationTokenSource? _stop;
    private WebApplication? _app;
    private string? _layoutJson;
    private Dictionary<string, DeckButton> _buttons = new();

    /// <summary>Configurateur web (pages et API locales).</summary>
    public ConfigApi? Api { get; set; }

    public event Action? ClientsChanged;
    public event Action<string>? Log;

    public DeckServer(ConfigStore store, AppCatalog catalog, IconService icons, AudioMixer audio, StaWorker sta)
    {
        _store = store;
        _catalog = catalog;
        _icons = icons;
        _audio = audio;
        _sta = sta;
        _store.Changed += () => _ = OnConfigChangedAsync();
    }

    public int Port => _store.Config.Port;

    public IReadOnlyList<string> ClientNames
    {
        get { lock (_clients) return _clients.Select(c => c.Name).ToList(); }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(Port));
        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        _app.Run(HandleHttpAsync);
        await _app.StartAsync();
        _stop = new CancellationTokenSource();
        _ = Task.Run(() => StateLoopAsync(_stop.Token));
        Log?.Invoke($"Serveur prêt sur le port {Port}");
    }

    public async Task StopAsync()
    {
        _stop?.Cancel();
        if (_app != null) await _app.StopAsync();
    }

    private async Task HandleHttpAsync(HttpContext ctx)
    {
        if (ctx.Request.Path != "/ws")
        {
            if (Api != null) await Api.HandleAsync(ctx);
            else await ctx.Response.WriteAsync("KeyraDeck fonctionne.");
            return;
        }
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            return;
        }
        var token = ctx.Request.Query["token"].ToString();
        if (!string.Equals(token, _store.Config.Token, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 401;
            Log?.Invoke("Connexion refusée : mauvais code");
            return;
        }

        var name = ctx.Request.Query["device"].ToString();
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var client = new Client(ws, string.IsNullOrWhiteSpace(name) ? "Téléphone" : name);
        lock (_clients) _clients.Add(client);
        ClientsChanged?.Invoke();
        Log?.Invoke($"{client.Name} connecté");
        try
        {
            await ReceiveLoopAsync(client, ctx.RequestAborted);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        finally
        {
            lock (_clients) _clients.Remove(client);
            ClientsChanged?.Invoke();
            Log?.Invoke($"{client.Name} déconnecté");
        }
    }

    private async Task ReceiveLoopAsync(Client client, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (client.Socket.State == WebSocketState.Open)
        {
            var result = await client.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return;
            }
            message.Write(buffer, 0, result.Count);
            if (message.Length > 8 * 1024 * 1024) throw new WebSocketException("Message trop gros");
            if (!result.EndOfMessage) continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            try
            {
                await HandleMessageAsync(client, text);
            }
            catch (Exception e)
            {
                await client.SendAsync(new { type = "toast", message = "Erreur sur le PC : " + e.Message });
            }
        }
    }

    private async Task HandleMessageAsync(Client client, string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "ping":
                await client.SendAsync(new { type = "pong" });
                break;

            case "getLayout":
                await client.SendRawAsync(await GetLayoutJsonAsync());
                await client.SendRawAsync(StatesJson(ComputeStates()));
                if (_autoPage != null) await client.SendAsync(new { type = "focus", page = _autoPage });
                break;

            case "press":
                await PressAsync(client, root.GetProperty("buttonId").GetString() ?? "");
                break;

            case "device":
            {
                var w = root.GetProperty("w").GetInt32();
                var h = root.GetProperty("h").GetInt32();
                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var cut = root.TryGetProperty("cut", out var cu) ? cu.GetInt32() : 0;
                var rad = root.TryGetProperty("r", out var rr) ? rr.GetInt32() : 0;
                var dv = _store.Config.Device;
                if (w >= 200 && h >= 200 && (dv?.W != w || dv?.H != h || dv?.Name != name || dv?.Cut != cut || dv?.R != rad))
                {
                    _store.Config.Device = new DeckDevice { Name = name, W = w, H = h, Cut = cut, R = rad };
                    _store.Save();
                    LayoutVersion++; // le configurateur se rafraîchit
                }
                break;
            }

            case "media":
            {
                var key = root.GetProperty("key").GetString() ?? "";
                if (key is "PlayPause" or "Next" or "Prev" or "Stop" or "Mute" or "VolumeUp" or "VolumeDown")
                    await Task.Run(() => KeySender.Send(key));
                break;
            }

            case "mouse":
            {
                // Souris du téléphone : traité tout de suite et dans l'ordre (pas de Task.Run) pour rester fluide.
                var action = root.GetProperty("action").GetString() ?? "";
                switch (action)
                {
                    case "move":
                        KeySender.MouseMove(Math.Clamp(root.GetProperty("dx").GetInt32(), -500, 500), Math.Clamp(root.GetProperty("dy").GetInt32(), -500, 500));
                        break;
                    case "down": case "up":
                        KeySender.MouseButton((root.GetProperty("button").GetString() ?? "") == "right", action == "down");
                        break;
                    case "scroll":
                        KeySender.MouseWheel(Math.Clamp(root.GetProperty("dy").GetInt32(), -1200, 1200));
                        break;
                }
                break;
            }

            case "type":
            {
                // Clavier du téléphone : du texte, ou une touche / un raccourci simple (Ctrl+C, Enter, Back…).
                if (root.TryGetProperty("text", out var tx))
                {
                    var typed = tx.GetString() ?? "";
                    if (typed.Length is > 0 and <= 200) await Task.Run(() => KeySender.TypeText(typed));
                }
                else if (root.TryGetProperty("key", out var kk))
                {
                    var spec = kk.GetString() ?? "";
                    if (KeySender.IsSafeSpec(spec)) await Task.Run(() => KeySender.Send(spec));
                }
                break;
            }

            case "getMixer":
            {
                var full = root.TryGetProperty("full", out var f) && f.GetBoolean();
                var channels = await Task.Run(() => _audio.GetChannels(full));
                await client.SendAsync(new { type = "mixer", full, channels });
                break;
            }

            case "setVolume":
            {
                var id = root.GetProperty("id").GetString() ?? "";
                var volume = (float)root.GetProperty("volume").GetDouble();
                await Task.Run(() => _audio.SetVolume(id, volume));
                break;
            }

            case "setMute":
            {
                var id = root.GetProperty("id").GetString() ?? "";
                var muted = root.GetProperty("muted").GetBoolean();
                await Task.Run(() => _audio.SetMute(id, muted));
                break;
            }

            case "setIcon":
                await SetCustomIconAsync(
                    root.GetProperty("buttonId").GetString() ?? "",
                    Convert.FromBase64String(root.GetProperty("png").GetString() ?? ""),
                    ".png");
                break;

            case "resetIcon":
                await ResetIconAsync(root.GetProperty("buttonId").GetString() ?? "");
                break;
        }
    }

    /// <summary>Enregistre une image choisie par l'utilisateur pour un bouton.</summary>
    public async Task SetCustomIconAsync(string buttonId, byte[] image, string extension)
    {
        var safe = string.Concat(buttonId.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        var file = $"{safe}-{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_store.IconsDir, file), image);
        _store.Config.IconOverrides[buttonId] = Path.Combine("icons", file);
        _store.Save();
        Log?.Invoke($"Nouvelle image pour « {buttonId} »");
        await OnConfigChangedAsync();
    }

    public async Task ResetIconAsync(string buttonId)
    {
        if (_store.Config.IconOverrides.Remove(buttonId))
        {
            _store.Save();
            await OnConfigChangedAsync();
        }
    }

    private async Task PressAsync(Client client, string buttonId)
    {
        var (found, error) = await ExecuteAsync(buttonId);
        if (!found)
        {
            await client.SendAsync(new { type = "toast", message = "Bouton inconnu, rechargement…" });
            await client.SendRawAsync(await GetLayoutJsonAsync());
            return;
        }
        if (error != null) await client.SendAsync(new { type = "toast", message = error });
    }

    /// <summary>Exécute l'action d'un bouton (depuis le téléphone ou le bouton « Tester » du configurateur).</summary>
    public async Task<(bool Found, string? Error)> ExecuteAsync(string buttonId)
    {
        await GetLayoutJsonAsync(); // s'assure que la liste des boutons est à jour
        if (!_buttons.TryGetValue(buttonId, out var button)) return (false, null);

        var action = button.Action;
        string? error = action.Type.ToLowerInvariant() switch
        {
            "open" => await _catalog.LaunchAsync(action),
            "url" => await _catalog.OpenUrlAsync(action.Target ?? ""),
            "keys" => await Task.Run(() => KeySender.Send(action.Keys ?? "")),
            "text" => await Task.Run(() => KeySender.TypeText(action.Text ?? "")),
            "micmute" => await Task.Run(() => { _audio.ToggleMic(); return (string?)null; }),
            "mastermute" => await Task.Run(() => { _audio.ToggleMaster(); return (string?)null; }),
            _ => null,
        };
        if (error == null && action.Toggle)
        {
            lock (_toggles) _toggles[button.Id] = !_toggles.GetValueOrDefault(button.Id);
        }
        _ = BroadcastStatesAsync(force: true);
        Log?.Invoke(error == null ? $"« {button.Label} »" : error);
        return (true, error);
    }

    // ---------- États des boutons (rouge quand activé) ----------

    private Dictionary<string, bool> ComputeStates()
    {
        var states = new Dictionary<string, bool>();
        bool? mic = null, master = null;
        foreach (var (id, b) in _buttons.ToList())
        {
            var a = b.Action;
            switch (a.Type.ToLowerInvariant())
            {
                case "micmute": states[id] = mic ??= _audio.MicMuted(); break;
                case "mastermute": states[id] = master ??= _audio.MasterMuted(); break;
                default:
                    if (a.Toggle) lock (_toggles) states[id] = _toggles.GetValueOrDefault(id);
                    break;
            }
        }
        return states;
    }

    /// <summary>Envoie une notification à afficher sur le boîtier du téléphone (bandeau puis compteur).</summary>
    public async Task NotifyAsync(string app, string title, string text)
    {
        List<Client> clients;
        lock (_clients) clients = _clients.ToList();
        var json = JsonSerializer.Serialize(new { type = "notify", id = Guid.NewGuid().ToString("N"), app, title, text, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, Wire);
        foreach (var c in clients) { try { await c.SendRawAsync(json); } catch { } }
    }

    /// <summary>Dernières valeurs des widgets (CPU, RAM, volume, météo) : le configurateur les affiche en direct.</summary>
    public object Live { get; private set; } = new { };

    private string StatesJson(Dictionary<string, bool> states)
    {
        float? volume = null; bool? muted = null; int? cpu = null, ram = null;
        WeatherData? weather = _needWeather ? _weather.Current(_store.Config.WeatherCity) : null;
        if (_needVolume) { volume = _audio.MasterVolume(); muted = _audio.MasterMuted(); }
        if (_needSystem) { var (c, r) = _systemStats.Read(); cpu = c; ram = r; }
        Live = new
        {
            cpu, ram,
            volume = volume == null ? (int?)null : (int)Math.Round(volume.Value * 100),
            muted,
            weather = weather == null ? null : new { temp = weather.Temp, code = weather.Code, wind = weather.Wind, humidity = weather.Humidity, city = weather.City },
        };
        return JsonSerializer.Serialize(new
        {
            type = "states",
            active = states.Where(p => p.Value).Select(p => p.Key).ToList(),
            volume = volume == null ? (int?)null : (int)Math.Round(volume.Value * 100),
            muted,
            cpu,
            ram,
            weather = weather == null ? null : new { temp = weather.Temp, code = weather.Code, wind = weather.Wind, humidity = weather.Humidity, city = weather.City },
        }, Wire);
    }

    private async Task BroadcastStatesAsync(bool force)
    {
        try
        {
            List<Client> clients;
            lock (_clients) clients = _clients.ToList();
            var json = StatesJson(await Task.Run(ComputeStates));
            if (clients.Count == 0) return;
            if (!force && json == _lastStates) return;
            _lastStates = json;
            foreach (var c in clients)
            {
                try { await c.SendRawAsync(json); } catch { }
            }
        }
        catch { /* un état raté sera renvoyé à la seconde suivante */ }
    }

    private async Task StateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            await BroadcastStatesAsync(force: false);
            await CheckForegroundAsync();
        }
    }

    /// <summary>Change de page sur le téléphone selon l'appli au premier plan (réglable par page dans le configurateur).</summary>
    private async Task CheckForegroundAsync()
    {
        try
        {
            var pages = _store.Config.Pages;
            if (!pages.Any(p => p.AutoProcesses?.Count > 0)) { _autoPage = null; return; }
            var fg = ForegroundWatcher.CurrentProcessName();
            if (fg == null || fg == "phonedeckpc") return; // fenêtre du configurateur : on ne bouge pas
            var match = pages.Where((p, i) => (i == 0 || p.Root) && p.AutoProcesses != null)
                .FirstOrDefault(p => p.AutoProcesses!.Any(n => ForegroundWatcher.Normalize(n) == fg));
            string? target;
            if (match != null) target = match.Id;
            else if (_autoPage != null) target = pages[0].Id; // on quitte l'appli : retour à la page d'accueil
            else return;
            var newAuto = match?.Id;
            if (newAuto == _autoPage) return;
            _autoPage = newAuto;
            await BroadcastAsync(new { type = "focus", page = target });
        }
        catch { }
    }

    private async Task BroadcastAsync(object payload)
    {
        List<Client> clients;
        lock (_clients) clients = _clients.ToList();
        var json = JsonSerializer.Serialize(payload, Wire);
        foreach (var c in clients)
        {
            try { await c.SendRawAsync(json); } catch { }
        }
    }

    // ---------- Disposition des pages ----------

    /// <summary>Augmente à chaque changement : le configurateur s'en sert pour savoir quand se rafraîchir.</summary>
    public int LayoutVersion { get; private set; }

    private async Task OnConfigChangedAsync()
    {
        _layoutJson = null;
        LayoutVersion++;
        _lastStates = "";
        var json = await GetLayoutJsonAsync();
        List<Client> clients;
        lock (_clients) clients = _clients.ToList();
        foreach (var c in clients)
        {
            try { await c.SendRawAsync(json); } catch { }
        }
        _ = BroadcastStatesAsync(force: true);
        Log?.Invoke(_store.LastError ?? "Configuration rechargée");
    }

    public async Task<string> GetLayoutJsonAsync()
    {
        if (_layoutJson != null) return _layoutJson;
        await _layoutLock.WaitAsync();
        try
        {
            if (_layoutJson != null) return _layoutJson;
            var (json, buttons) = await _sta.Run(BuildLayout);
            _buttons = buttons;
            _layoutJson = json;
            return json;
        }
        finally
        {
            _layoutLock.Release();
        }
    }

    private (string, Dictionary<string, DeckButton>) BuildLayout()
    {
        var cfg = _store.Config;
        var expanded = new Dictionary<string, List<DeckButton>>();
        foreach (var page in cfg.Pages) expanded[page.Id] = Expand(page);
        List<DeckButton> Children(string pageId) => expanded.TryGetValue(pageId, out var b) ? b : new();

        var lookup = new Dictionary<string, DeckButton>();
        var pages = cfg.Pages.Select((page, index) =>
        {
            var buttons = expanded[page.Id];
            var isRoot = index == 0 || page.Root;
            int? columns = page.Columns ?? (isRoot ? cfg.Columns : null);
            int? rows = page.Rows ?? (isRoot ? cfg.Rows : null);
            var cols = Math.Clamp(columns ?? (buttons.Count <= 4 ? 2 : 3), 1, 12);
            var blocked = new HashSet<int>();
            foreach (var w in page.Widgets)
                for (int dy = 0; dy < Math.Max(1, w.H); dy++)
                    for (int dx = 0; dx < Math.Max(1, w.W); dx++)
                        blocked.Add(w.Slot + dy * cols + dx);
            var slots = AssignSlots(buttons, blocked);
            // La grille grandit si des boutons ou widgets ne rentrent pas (plutôt que de les cacher).
            var lastCell = Math.Max(slots.Values.DefaultIfEmpty(-1).Max(), -1);
            var needed = lastCell < 0 ? 1 : lastCell / cols + 1;
            foreach (var w in page.Widgets) needed = Math.Max(needed, w.Slot / cols + Math.Max(1, w.H));
            rows = Math.Max(rows ?? 1, needed);
            return new
            {
                id = page.Id,
                name = page.Name,
                root = isRoot,
                columns = cols,
                rows,
                widgets = page.Widgets.Select(w => new { id = w.Id, type = w.Type, model = w.Model, slot = w.Slot, w = Math.Max(1, w.W), h = Math.Max(1, w.H) }).ToList(),
                buttons = buttons.Select(b =>
                {
                    lookup[b.Id] = b;
                    var kind = b.Action.Type.ToLowerInvariant() switch
                    {
                        "folder" => "folder",
                        "mixer" => "mixer",
                        "page" => "page",
                        _ => "action",
                    };
                    return new
                    {
                        id = b.Id,
                        label = b.Label,
                        hideLabel = b.HideLabel ?? cfg.HideLabels,
                        color = b.Color,
                        icon = SafeIcon(b, Children),
                        slot = slots[b.Id],
                        kind,
                        page = kind is "folder" or "page" ? b.Action.Page : null,
                        // Comme un dossier Android : on referme après avoir lancé une appli,
                        // mais on reste ouvert pour les raccourcis (micro, sourdine…).
                        closeFolder = b.Action.Type.Equals("open", StringComparison.OrdinalIgnoreCase)
                                   || b.Action.Type.Equals("url", StringComparison.OrdinalIgnoreCase),
                    };
                }).ToList(),
            };
        }).ToList();

        _needVolume = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "volume"));
        _needSystem = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "system"));
        _needWeather = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "weather"));
        var json = JsonSerializer.Serialize(new
        {
            type = "layout",
            pcName = _store.PcName,
            theme = cfg.Theme,
            home = cfg.Pages.FirstOrDefault()?.Id ?? "",
            pages,
        }, Wire);
        return (json, lookup);
    }

    /// <summary>Place chaque bouton : ceux qui ont une case la gardent, les autres prennent les premières cases libres.</summary>
    public static Dictionary<string, int> AssignSlots(List<DeckButton> buttons, HashSet<int>? blocked = null)
    {
        var result = new Dictionary<string, int>();
        var used = new HashSet<int>(blocked ?? new HashSet<int>());
        foreach (var b in buttons)
        {
            if (b.Slot is int s && s >= 0 && used.Add(s)) result[b.Id] = s;
        }
        int next = 0;
        foreach (var b in buttons)
        {
            if (result.ContainsKey(b.Id)) continue;
            while (used.Contains(next)) next++;
            used.Add(next);
            result[b.Id] = next;
        }
        return result;
    }

    private string? SafeIcon(DeckButton b, Func<string, List<DeckButton>> children)
    {
        try { return _icons.ForButton(b, children); }
        catch { return null; } // une icône ratée ne doit jamais bloquer l'affichage
    }

    /// <summary>Remplace les boutons d'une page par les raccourcis d'un dossier du menu Démarrer si demandé.</summary>
    private List<DeckButton> Expand(DeckPage page)
    {
        if (!string.IsNullOrWhiteSpace(page.StartMenuFolder))
        {
            var shortcuts = _catalog.ShortcutsInStartMenuFolder(page.StartMenuFolder!);
            if (shortcuts.Count > 0)
            {
                return shortcuts.Select((s, i) => new DeckButton
                {
                    Id = $"{page.Id}-auto-{Slug(s.Name)}",
                    Label = s.Name,
                    Icon = "auto",
                    Action = new DeckAction { Type = "open", Target = s.ShellPath },
                }).ToList();
            }
        }
        return page.Buttons;
    }

    private static string Slug(string s) =>
        string.Concat(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');

    private sealed class Client
    {
        public WebSocket Socket { get; }
        public string Name { get; }
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Client(WebSocket socket, string name)
        {
            Socket = socket;
            Name = name;
        }

        public Task SendAsync(object payload) => SendRawAsync(JsonSerializer.Serialize(payload, Wire));

        public async Task SendRawAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
