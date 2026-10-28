using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhoneDeckPC;

/// <summary>Un appareil de Home Assistant (lampe, prise, scène…), pour la liste du configurateur.</summary>
public record HaEntity(string Id, string Name, string State);

/// <summary>
/// Pilote Home Assistant par son API web (adresse + jeton d'accès longue durée).
/// Home Assistant parle à presque toutes les marques (Hue, IKEA, Xiaomi, Tuya, Shelly…), d'où ce choix.
/// L'état des appareils posés sur des boutons est relu toutes les 2 secondes.
/// </summary>
public class HomeAssistantClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly IntegrationStore _store;
    private readonly Dictionary<string, string> _states = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _run;
    private string? _runningFor;

    public bool Connected { get; private set; }
    public string Status { get; private set; } = "Désactivé";
    public event Action? StateChanged;

    public HomeAssistantClient(IntegrationStore store)
    {
        _store = store;
        _store.Changed += () => Restart(force: false);
    }

    public void Start() => Restart(force: true);

    /// <summary>Appareils à surveiller (ceux qui ont un bouton).</summary>
    public void Watch(IEnumerable<string> entities)
    {
        var set = new HashSet<string>(entities.Where(e => !string.IsNullOrWhiteSpace(e)), StringComparer.OrdinalIgnoreCase);
        lock (_states) _watched = set;
    }

    /// <summary>Allumé, ouvert, en lecture… : le bouton s'affiche « activé ».</summary>
    public bool IsOn(string entity)
    {
        string? st;
        lock (_states) _states.TryGetValue(entity, out st);
        return st is "on" or "open" or "opening" or "playing" or "unlocked" or "home" or "heat" or "cool" or "heat_cool" or "auto" or "cleaning";
    }

    private void Restart(bool force)
    {
        var s = _store.Settings.HomeAssistant;
        var key = $"{s.Enabled}|{s.Url}|{s.Token}";
        if (!force && key == _runningFor) return;
        _runningFor = key;
        _run?.Cancel();
        Connected = false;
        if (!s.Enabled) { Status = "Désactivé"; return; }
        if (string.IsNullOrWhiteSpace(s.Url) || string.IsNullOrWhiteSpace(s.Token)) { Status = "Il manque l'adresse ou le jeton"; return; }
        var cts = new CancellationTokenSource();
        _run = cts;
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        Status = "Connexion…";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var res = await SendAsync(HttpMethod.Get, "/api/", null, ct);
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new Exception("Jeton refusé par Home Assistant");
                res.EnsureSuccessStatusCode();
                Connected = true;
                Status = "Connecté";
                await PollAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                var was = Connected;
                Connected = false;
                Status = e is HttpRequestException or TaskCanceledException ? "Home Assistant injoignable : vérifie l'adresse" : e.Message;
                if (was) StateChanged?.Invoke();
            }
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { return; }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<string> watched;
            lock (_states) watched = _watched.ToList();
            var changed = false;
            foreach (var id in watched)
            {
                using var res = await SendAsync(HttpMethod.Get, "/api/states/" + Uri.EscapeDataString(id), null, ct);
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var st = doc.RootElement.GetProperty("state").GetString() ?? "";
                lock (_states)
                {
                    if (!_states.TryGetValue(id, out var old) || old != st) { _states[id] = st; changed = true; }
                }
            }
            if (changed) StateChanged?.Invoke();
            await Task.Delay(2000, ct);
        }
    }

    /// <summary>Tous les appareils de Home Assistant (pour choisir dans le configurateur).</summary>
    public async Task<List<HaEntity>> EntitiesAsync()
    {
        if (!_store.Settings.HomeAssistant.Enabled) return new();
        using var res = await SendAsync(HttpMethod.Get, "/api/states", null, CancellationToken.None);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<HaEntity>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.GetProperty("entity_id").GetString() ?? "";
            var domain = id.Split('.')[0];
            if (domain is not ("light" or "switch" or "fan" or "cover" or "scene" or "script" or "media_player" or "input_boolean"
                or "button" or "input_button" or "lock" or "climate" or "vacuum" or "automation" or "humidifier" or "siren")) continue;
            var name = e.TryGetProperty("attributes", out var at) && at.TryGetProperty("friendly_name", out var fn) ? fn.GetString() ?? id : id;
            list.Add(new HaEntity(id, name, e.GetProperty("state").GetString() ?? ""));
        }
        return list.OrderBy(x => x.Id.Split('.')[0]).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Commande d'un bouton : toggle (par défaut), on ou off.</summary>
    public async Task<string?> RunAsync(string? command, string? entity)
    {
        var s = _store.Settings.HomeAssistant;
        if (!s.Enabled) return "Home Assistant n'est pas connecté : active-le dans Réglages > Connexions.";
        if (string.IsNullOrWhiteSpace(entity)) return "Choisis l'appareil dans le configurateur.";
        var domain = entity.Split('.')[0];
        var cmd = (command ?? "toggle").ToLowerInvariant();
        var (svcDomain, service) = domain switch
        {
            "scene" or "script" => (domain, "turn_on"),
            "button" or "input_button" => (domain, "press"),
            "lock" => (domain, cmd == "on" ? "unlock" : cmd == "off" ? "lock" : IsOn(entity) ? "lock" : "unlock"),
            "cover" => (domain, cmd == "on" ? "open_cover" : cmd == "off" ? "close_cover" : "toggle"),
            "vacuum" => (domain, cmd == "off" ? "return_to_base" : cmd == "on" ? "start" : IsOn(entity) ? "return_to_base" : "start"),
            "media_player" => (domain, cmd == "on" ? "media_play" : cmd == "off" ? "media_pause" : "media_play_pause"),
            _ => ("homeassistant", cmd == "on" ? "turn_on" : cmd == "off" ? "turn_off" : "toggle"),
        };
        try
        {
            var body = JsonSerializer.Serialize(new { entity_id = entity });
            using var res = await SendAsync(HttpMethod.Post, $"/api/services/{svcDomain}/{service}", body, CancellationToken.None);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) return "Home Assistant refuse le jeton.";
            if (!res.IsSuccessStatusCode) return $"Home Assistant : erreur {(int)res.StatusCode} pour {entity}.";
            return null;
        }
        catch (Exception e)
        {
            return "Home Assistant injoignable (" + e.Message + ")";
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        var s = _store.Settings.HomeAssistant;
        var baseUrl = s.Url.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) baseUrl = "http://" + baseUrl;
        var req = new HttpRequestMessage(method, baseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token.Trim());
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return Http.SendAsync(req, ct);
    }
}
