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
    private readonly Integrations _int;
    private readonly List<Client> _clients = new();
    private readonly SemaphoreSlim _layoutLock = new(1, 1);
    private readonly Dictionary<string, bool> _toggles = new();
    private string _lastStates = "";
    private string? _autoPage;
    private readonly SystemStats _systemStats = new();
    private bool _needVolume, _needSystem, _needPerfApp, _needWeather, _needDiscord, _needNowPlaying;
    private int _statesQueued;
    private readonly NowPlaying _nowPlaying = new();
    private string _lastNowPlaying = "";
    private string _lastArtKey = "";
    private readonly SemaphoreSlim _nowPlayingLock = new(1, 1);
    private readonly WeatherService _weather = new();
    private CancellationTokenSource? _stop;
    private WebApplication? _app;
    private string? _layoutJson;
    private Dictionary<string, DeckButton> _buttons = new();

    /// <summary>Configurateur web (pages et API locales).</summary>
    public ConfigApi? Api { get; set; }

    public event Action? ClientsChanged;
    public event Action<string>? Log;

    public DeckServer(ConfigStore store, AppCatalog catalog, IconService icons, AudioMixer audio, StaWorker sta, Integrations integrations)
    {
        _int = integrations;
        // OBS, Discord ou Home Assistant a changé : on prévient le téléphone (regroupé, « qui parle » peut changer très vite).
        _int.StateChanged += () =>
        {
            if (Interlocked.Exchange(ref _statesQueued, 1) == 1) return;
            _ = Task.Delay(80).ContinueWith(_ => { Interlocked.Exchange(ref _statesQueued, 0); return BroadcastStatesAsync(force: false); });
        };
        _store = store;
        _catalog = catalog;
        _icons = icons;
        _audio = audio;
        _sta = sta;
        _store.Changed += () => _ = OnConfigChangedAsync();
    }

    public int Port => _store.Config.Port;

    /// <summary>
    /// Port réservé au câble USB : adb relie le port du téléphone à celui-ci. Il n'écoute que sur le PC lui-même
    /// et n'ouvre que la connexion du deck (/ws, avec le code) : jamais le configurateur.
    /// </summary>
    public int UsbPort => _store.Config.Port + 1;

    public IReadOnlyList<string> ClientNames
    {
        get { lock (_clients) return _clients.Select(c => c.Usb ? c.Name + " (câble USB)" : c.Name).ToList(); }
    }

    /// <summary>Un téléphone est connecté par le câble USB.</summary>
    public bool UsbConnected
    {
        get { lock (_clients) return _clients.Any(c => c.Usb); }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.ListenAnyIP(Port);
            o.Listen(System.Net.IPAddress.Loopback, UsbPort);
        });
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
        var viaUsb = ctx.Connection.LocalPort == UsbPort;
        if (viaUsb && ctx.Request.Path != "/ws")
        {
            ctx.Response.StatusCode = 404;
            return;
        }
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
        var client = new Client(ws, string.IsNullOrWhiteSpace(name) ? "Téléphone" : name) { Usb = viaUsb };
        lock (_clients) _clients.Add(client);
        ClientsChanged?.Invoke();
        Log?.Invoke($"{client.Name} connecté{(client.Usb ? " par câble USB" : "")}");
        try
        {
            await ReceiveLoopAsync(client, ctx.RequestAborted);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        finally
        {
            lock (_clients) _clients.Remove(client);
            // Téléphone parti avec une touche enfoncée (Ctrl, Maj…) : on la relâche pour ne pas la laisser coincée.
            foreach (var vk in client.HeldKeys)
                try { KeySender.KeyEvent(vk, false); } catch { }
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
                if (_needNowPlaying) await client.SendRawAsync(NowPlayingJson(_nowPlaying.Current, withArt: true));
                break;

            case "getScreenBg":
            {
                // Fond d'une zone écran : envoyé en binaire (une vidéo peut peser plusieurs Mo), sans bloquer la réception.
                var name = root.GetProperty("file").GetString() ?? "";
                var path = _store.ScreenBgPath(name);
                if (path != null) _ = Task.Run(async () =>
                {
                    try { await client.SendFileAsync(name, await File.ReadAllBytesAsync(path)); }
                    catch (Exception e) { Log?.Invoke("Fond d'écran non envoyé : " + e.Message); }
                });
                break;
            }

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

            case "nowPlaying":
            {
                // Boutons du widget « Morceau en cours » : agit sur le lecteur affiché (Spotify, navigateur…).
                var action = root.GetProperty("action").GetString() ?? "";
                if (action is "prev" or "next" or "toggle")
                {
                    await _nowPlaying.CommandAsync(action);
                    await Task.Delay(250); // laisse le lecteur changer de morceau avant de relire
                    await BroadcastNowPlayingAsync();
                }
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

            case "key":
            {
                // Clavier PC complet du téléphone : une touche enfoncée ou relâchée (traité dans l'ordre, sans Task.Run).
                var vk = root.GetProperty("vk").GetInt32();
                var down = root.GetProperty("down").GetBoolean();
                if (!KeySender.IsPhysicalKey(vk)) break;
                if (down) client.HeldKeys.Add(vk); else client.HeldKeys.Remove(vk);
                KeySender.KeyEvent(vk, down);
                break;
            }

            case "discord":
            {
                // Micro / sourdine touchés sur le widget « Salon vocal », ou le menu d'un bouton Discord.
                var cmd = root.GetProperty("command").GetString() ?? "";
                if (cmd is "mute" or "deafen" or "screen" or "leave")
                {
                    var error = await _int.Discord.RunAsync(cmd);
                    if (error != null) await client.SendAsync(new { type = "toast", message = error });
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

    /// <summary>Donne aux copies de boutons (copier/coller, dupliquer) l'image personnalisée de l'original.</summary>
    public async Task CopyCustomIconsAsync(IEnumerable<(string From, string To)> pairs)
    {
        var any = false;
        foreach (var (from, to) in pairs)
        {
            if (string.IsNullOrEmpty(to) || !_store.Config.IconOverrides.TryGetValue(from, out var file)) continue;
            _store.Config.IconOverrides[to] = file;
            any = true;
        }
        if (!any) return;
        _store.Save();
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
            "keys" => await Task.Run(() => KeySender.Send(action.Keys ?? "", action.KeyMode)),
            "text" => await Task.Run(() => KeySender.TypeText(action.Text ?? "")),
            "micmute" => await Task.Run(() => { _audio.ToggleMic(); return (string?)null; }),
            "mastermute" => await Task.Run(() => { _audio.ToggleMaster(); return (string?)null; }),
            "obs" => await _int.Obs.RunAsync(action.Command ?? "", action.Target),
            "discord" when string.Equals(action.Command, "panel", StringComparison.OrdinalIgnoreCase) => null, // menu ouvert sur le téléphone
            "discord" => await _int.Discord.RunAsync(action.Command ?? ""),
            "ha" => await _int.HomeAssistant.RunAsync(action.Command, action.Target),
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
                case "obs":
                {
                    var obs = _int.Obs;
                    states[id] = (a.Command ?? "").ToLowerInvariant() switch
                    {
                        "stream" => obs.Streaming,
                        "record" => obs.Recording,
                        "pause" => obs.RecordPaused,
                        "replay" => obs.ReplayActive,
                        "virtualcam" => obs.VirtualCam,
                        "scene" => obs.Connected && string.Equals(obs.CurrentScene, a.Target, StringComparison.OrdinalIgnoreCase),
                        "mute" => obs.Connected && obs.IsInputMuted(a.Target ?? ""),
                        _ => false,
                    };
                    break;
                }
                case "discord":
                    states[id] = (a.Command ?? "").ToLowerInvariant() switch
                    {
                        "mute" => _int.Discord.Muted || _int.Discord.Deafened,
                        "deafen" => _int.Discord.Deafened,
                        _ => false,
                    };
                    break;
                case "ha":
                    states[id] = _int.HomeAssistant.Connected && _int.HomeAssistant.IsOn(a.Target ?? "");
                    break;
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
        float? volume = null; bool? muted = null; int? cpu = null, ram = null, gpu = null, disk = null;
        AppStats? fg = null;
        WeatherData? weather = _needWeather ? _weather.Current(_store.Config.WeatherCity) : null;
        if (_needVolume) { volume = _audio.MasterVolume(); muted = _audio.MasterMuted(); }
        if (_needSystem || _needPerfApp) { var (c, r, g, d) = _systemStats.Read(); cpu = c; ram = r; gpu = g; disk = d; }
        if (_needPerfApp) fg = _systemStats.ReadForeground();
        object? app = fg == null ? null : new { name = fg.Name, cpu = fg.Cpu, ram = fg.Ram, ramMb = fg.RamMb, gpu = fg.Gpu, fps = fg.Fps };
        var dc = _int.Discord;
        object? discord = !_needDiscord ? null : new
        {
            connected = dc.Connected,
            status = dc.Status,
            channel = dc.ChannelName,
            inCall = dc.InCall,
            muted = dc.Muted,
            deafened = dc.Deafened,
            members = dc.Members().Take(12).Select(m => new { name = m.Name, speaking = m.Speaking, muted = m.Muted, deafened = m.Deafened }).ToList(),
        };
        Live = new
        {
            cpu, ram, gpu, disk, app, discord,
            volume = volume == null ? (int?)null : (int)Math.Round(volume.Value * 100),
            muted,
            weather = weather == null ? null : new { temp = weather.Temp, code = weather.Code, wind = weather.Wind, humidity = weather.Humidity, city = weather.City },
            nowPlaying = !_needNowPlaying || _nowPlaying.Current is not { } np ? null
                : new { title = np.Title, artist = np.Artist, app = np.App, playing = np.Playing, pos = np.Pos, dur = np.Dur, artKey = np.ArtKey },
        };
        return JsonSerializer.Serialize(new
        {
            type = "states",
            active = states.Where(p => p.Value).Select(p => p.Key).ToList(),
            volume = volume == null ? (int?)null : (int)Math.Round(volume.Value * 100),
            muted,
            cpu,
            ram,
            gpu,
            disk,
            app,
            weather = weather == null ? null : new { temp = weather.Temp, code = weather.Code, wind = weather.Wind, humidity = weather.Humidity, city = weather.City },
            discord,
        }, Wire);
    }

    // ---------- Morceau en cours ----------

    /// <summary>La pochette n'est jointe que si elle a changé (ou pour un téléphone qui vient d'arriver).</summary>
    private static string NowPlayingJson(NowPlayingData? np, bool withArt) => JsonSerializer.Serialize(np == null
        ? new { type = "nowPlaying", title = (string?)null, artist = (string?)null, album = (string?)null, app = (string?)null, playing = false, pos = 0, dur = 0, artKey = "", art = (string?)null }
        : new { type = "nowPlaying", title = (string?)np.Title, artist = (string?)np.Artist, album = (string?)np.Album, app = (string?)np.App, playing = np.Playing, pos = np.Pos, dur = np.Dur, artKey = np.ArtKey, art = withArt && np.Art != null ? Convert.ToBase64String(np.Art) : null }, Wire);

    /// <summary>Pochette du morceau en cours, pour le configurateur.</summary>
    public byte[]? NowPlayingArt => _nowPlaying.Current?.Art;

    private async Task BroadcastNowPlayingAsync()
    {
        if (!_needNowPlaying) return;
        await _nowPlayingLock.WaitAsync();
        try
        {
            var np = await _nowPlaying.RefreshAsync();
            var json = NowPlayingJson(np, withArt: false);
            if (json == _lastNowPlaying) return;
            _lastNowPlaying = json;
            var artChanged = (np?.ArtKey ?? "") != _lastArtKey;
            _lastArtKey = np?.ArtKey ?? "";
            if (artChanged) json = NowPlayingJson(np, withArt: true);
            List<Client> clients;
            lock (_clients) clients = _clients.ToList();
            foreach (var c in clients) { try { await c.SendRawAsync(json); } catch { } }
        }
        catch { }
        finally { _nowPlayingLock.Release(); }
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
            await BroadcastNowPlayingAsync();
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
            // Pas de bouton sur la zone écran : elle n'accueille que des widgets.
            var screen = page.Screen;
            if (screen != null)
                for (int dy = 0; dy < Math.Max(1, screen.H); dy++)
                    for (int dx = 0; dx < Math.Max(1, screen.W); dx++)
                        blocked.Add(screen.Slot + dy * cols + dx);
            // Grilles séparées : leurs cases ne reçoivent pas de bouton de la page, leurs boutons sont placés dans leur propre grille.
            var grids = (page.Grids ?? new()).Where(g => !string.IsNullOrEmpty(g.Id)).GroupBy(g => g.Id).Select(g => g.First()).ToList();
            foreach (var g in grids)
                for (int dy = 0; dy < Math.Max(1, g.H); dy++)
                    for (int dx = 0; dx < Math.Max(1, g.W); dx++)
                        blocked.Add(g.Slot + dy * cols + dx);
            var gridIds = grids.Select(g => g.Id).ToHashSet();
            string? GridOf(DeckButton b) => b.Grid != null && gridIds.Contains(b.Grid) ? b.Grid : null;
            var slots = AssignSlots(buttons.Where(b => GridOf(b) == null).ToList(), blocked);
            var gridRows = new Dictionary<string, int>();
            foreach (var g in grids)
            {
                var inner = AssignSlots(buttons.Where(b => GridOf(b) == g.Id).ToList());
                foreach (var (id, s) in inner) slots[id] = s;
                var gc = Math.Clamp(g.Columns, 1, 12);
                var last = inner.Values.DefaultIfEmpty(-1).Max();
                gridRows[g.Id] = Math.Clamp(Math.Max(g.Rows, last / gc + 1), 1, 24);
            }
            // La grille grandit si des boutons ou widgets ne rentrent pas (plutôt que de les cacher).
            var lastCell = Math.Max(buttons.Where(b => GridOf(b) == null).Select(b => slots[b.Id]).DefaultIfEmpty(-1).Max(), -1);
            var needed = lastCell < 0 ? 1 : lastCell / cols + 1;
            foreach (var w in page.Widgets) needed = Math.Max(needed, w.Slot / cols + Math.Max(1, w.H));
            if (screen != null) needed = Math.Max(needed, screen.Slot / cols + Math.Max(1, screen.H));
            foreach (var g in grids) needed = Math.Max(needed, g.Slot / cols + Math.Max(1, g.H));
            rows = Math.Max(rows ?? 1, needed);
            return new
            {
                id = page.Id,
                name = page.Name,
                root = isRoot,
                columns = cols,
                rows,
                screen = screen == null ? null : new
                {
                    slot = screen.Slot, w = Math.Max(1, screen.W), h = Math.Max(1, screen.H),
                    bg = _store.ScreenBgPath(screen.Bg) != null ? screen.Bg : null,
                    dim = Math.Clamp(screen.Dim, 0, 80),
                },
                widgets = page.Widgets.Select(w => new { id = w.Id, type = w.Type, model = w.Model, slot = w.Slot, w = Math.Max(1, w.W), h = Math.Max(1, w.H) }).ToList(),
                grids = grids.Count == 0 ? null : grids.Select(g => new
                {
                    id = g.Id, name = g.Name, slot = g.Slot, w = Math.Max(1, g.W), h = Math.Max(1, g.H),
                    columns = Math.Clamp(g.Columns, 1, 12), rows = gridRows[g.Id],
                }).ToList(),
                buttons = buttons.Select(b =>
                {
                    lookup[b.Id] = b;
                    var kind = b.Action.Type.ToLowerInvariant() switch
                    {
                        "folder" => "folder",
                        "mixer" => "mixer",
                        "page" => "page",
                        "discord" when string.Equals(b.Action.Command, "panel", StringComparison.OrdinalIgnoreCase) => "discordPanel",
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
                        grid = GridOf(b),
                        kind,
                        page = kind is "folder" or "page" ? b.Action.Page : null,
                        // Comme un dossier Android : on referme après avoir lancé une appli,
                        // mais on reste ouvert pour les raccourcis (micro, sourdine…).
                        closeFolder = b.Action.Type.Equals("open", StringComparison.OrdinalIgnoreCase)
                                   || b.Action.Type.Equals("url", StringComparison.OrdinalIgnoreCase),
                        vibrate = b.Vibrate,
                        sound = b.Sound,
                    };
                }).ToList(),
            };
        }).ToList();

        _needVolume = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "volume"));
        _needSystem = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "system"));
        _needPerfApp = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "perfapp"));
        if (!_needPerfApp) _systemStats.StopFps();
        _needWeather = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "weather"));
        // Les boutons Discord aussi : le menu d'un bouton montre le vrai état du micro et de la sourdine.
        _needDiscord = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "discord"))
                    || lookup.Values.Any(b => b.Action.Type.Equals("discord", StringComparison.OrdinalIgnoreCase));
        _int.HomeAssistant.Watch(lookup.Values.Where(b => b.Action.Type.Equals("ha", StringComparison.OrdinalIgnoreCase)).Select(b => b.Action.Target ?? ""));
        // Le pavé média « avec titre » affiche aussi le morceau en cours.
        _needNowPlaying = cfg.Pages.Any(p => p.Widgets.Any(w => w.Type == "nowplaying" || (w.Type == "media" && w.Model.EndsWith("t"))));
        _lastNowPlaying = ""; // un nouveau widget reçoit tout de suite le morceau en cours
        _lastArtKey = "";
        var json = JsonSerializer.Serialize(new
        {
            type = "layout",
            pcName = _store.PcName,
            theme = cfg.Theme,
            feedback = cfg.Feedback,
            orientation = cfg.Orientation is "portrait" or "landscape" ? cfg.Orientation : "auto",
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
        /// <summary>Touches du clavier PC actuellement enfoncées par ce téléphone.</summary>
        public HashSet<int> HeldKeys { get; } = new();
        /// <summary>Connecté par le câble USB plutôt que par le Wi-Fi.</summary>
        public bool Usb { get; init; }
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Client(WebSocket socket, string name)
        {
            Socket = socket;
            Name = name;
        }

        public Task SendAsync(object payload) => SendRawAsync(JsonSerializer.Serialize(payload, Wire));

        /// <summary>Un fichier en un seul message binaire : son nom, un saut de ligne, puis son contenu.</summary>
        public async Task SendFileAsync(string name, byte[] data)
        {
            var head = Encoding.UTF8.GetBytes(name + "\n");
            var bytes = new byte[head.Length + data.Length];
            head.CopyTo(bytes, 0);
            data.CopyTo(bytes, head.Length);
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

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
