using System.Text.Json;

namespace PhoneDeckPC;

/// <summary>
/// Réglages des connexions aux autres logiciels (OBS, Discord, Home Assistant).
/// Rangés à part dans integrations.json : ils contiennent des mots de passe et ne doivent jamais
/// partir avec une page exportée, ni être écrasés par le configurateur quand il enregistre les pages.
/// </summary>
public class IntegrationSettings
{
    public ObsSettings Obs { get; set; } = new();
    public DiscordSettings Discord { get; set; } = new();
    public HomeAssistantSettings HomeAssistant { get; set; } = new();
}

public class ObsSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
}

public class DiscordSettings
{
    public bool Enabled { get; set; }
    /// <summary>« Application ID » de l'appli créée sur discord.com/developers.</summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class HomeAssistantSettings
{
    public bool Enabled { get; set; }
    /// <summary>Adresse de Home Assistant, ex. http://homeassistant.local:8123</summary>
    public string Url { get; set; } = "";
    /// <summary>Jeton d'accès longue durée (Profil > Sécurité).</summary>
    public string Token { get; set; } = "";
}

/// <summary>Lit et écrit integrations.json, et prévient les connexions quand un réglage change.</summary>
public class IntegrationStore
{
    private readonly string _path;
    public IntegrationSettings Settings { get; private set; } = new();
    public event Action? Changed;

    public IntegrationStore(string dataDir)
    {
        _path = Path.Combine(dataDir, "integrations.json");
        try
        {
            if (File.Exists(_path))
                Settings = JsonSerializer.Deserialize<IntegrationSettings>(File.ReadAllText(_path), ConfigStore.Json) ?? new();
        }
        catch { Settings = new(); }
    }

    public void Save(bool notify = true)
    {
        lock (this)
        {
            try { File.WriteAllText(_path, JsonSerializer.Serialize(Settings, ConfigStore.Json)); } catch { }
        }
        if (notify) Changed?.Invoke();
    }
}

/// <summary>Regroupe les trois connexions et les démarre selon les réglages.</summary>
public class Integrations
{
    public IntegrationStore Store { get; }
    public ObsClient Obs { get; }
    public DiscordClient Discord { get; }
    public HomeAssistantClient HomeAssistant { get; }

    /// <summary>Un état a changé (live lancé, micro coupé, lampe allumée…) : le téléphone doit être mis à jour.</summary>
    public event Action? StateChanged;

    public Integrations(string dataDir)
    {
        Store = new IntegrationStore(dataDir);
        Obs = new ObsClient(Store);
        Discord = new DiscordClient(Store);
        HomeAssistant = new HomeAssistantClient(Store);
        Obs.StateChanged += () => StateChanged?.Invoke();
        Discord.StateChanged += () => StateChanged?.Invoke();
        HomeAssistant.StateChanged += () => StateChanged?.Invoke();
    }

    public void Start()
    {
        Obs.Start();
        Discord.Start();
        HomeAssistant.Start();
    }
}
