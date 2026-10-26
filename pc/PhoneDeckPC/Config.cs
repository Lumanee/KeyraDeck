using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneDeckPC;

/// <summary>Contenu de config.json : tout ce que le téléphone affiche.</summary>
public class DeckConfig
{
    public int Port { get; set; } = 8765;
    public string Token { get; set; } = "";
    public string? PcName { get; set; }
    /// <summary>Taille de la grille de la page d'accueil (colonnes × lignes), comme un Stream Deck 5×3.</summary>
    public int Columns { get; set; } = 5;
    public int Rows { get; set; } = 3;
    public DeckTheme Theme { get; set; } = new();
    /// <summary>Ville du widget météo (Open-Meteo).</summary>
    public string WeatherCity { get; set; } = "";
    /// <summary>Dernier téléphone connecté : taille de l'écran en dp (le configurateur copie ses proportions).</summary>
    public DeckDevice? Device { get; set; }
    /// <summary>Taille d'écran (arrondie) pour laquelle la grille a déjà été adaptée ; permet de ne proposer l'adaptation qu'une fois.</summary>
    public string? AdaptedTo { get; set; }
    public List<DeckPage> Pages { get; set; } = new();
    /// <summary>Images choisies depuis le téléphone, par id de bouton (chemin relatif au dossier de config).</summary>
    public Dictionary<string, string> IconOverrides { get; set; } = new();
}

public class DeckDevice
{
    public string Name { get; set; } = "";
    public int W { get; set; }
    public int H { get; set; }
}

/// <summary>Couleurs du téléphone (boîtier, fond, touches…), au format #RRGGBB.</summary>
public class DeckTheme
{
    public string Name { get; set; } = "Anthracite";
    /// <summary>Couleur du boîtier.</summary>
    public string Body { get; set; } = "#2C2C34";
    /// <summary>Fond derrière le boîtier.</summary>
    public string Desk { get; set; } = "#0B0B0E";
    /// <summary>Fond des touches.</summary>
    public string Key { get; set; } = "#1B1B21";
    /// <summary>Couleur d'accent (voyant, boutons, points de pages).</summary>
    public string Accent { get; set; } = "#7C6CF2";
    /// <summary>Couleur d'un bouton « activé » (micro coupé…).</summary>
    public string Active { get; set; } = "#C62828";
}

/// <summary>Un widget posé sur une page, sur plusieurs cases (comme sur Nova Launcher).</summary>
public class DeckWidget
{
    public string Id { get; set; } = "";
    /// <summary>media | volume | clock | system</summary>
    public string Type { get; set; } = "clock";
    /// <summary>Modèle choisi (ex. "system-2x2") : chaque modèle a une taille et une présentation fixes.</summary>
    public string Model { get; set; } = "";
    /// <summary>Case en haut à gauche (0 = première case).</summary>
    public int Slot { get; set; }
    public int W { get; set; } = 2;
    public int H { get; set; } = 1;
}

public class DeckPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Si renseigné, les boutons sont générés depuis ce dossier du menu Démarrer (ex. "Launcher").</summary>
    public string? StartMenuFolder { get; set; }
    /// <summary>Page d'accueil supplémentaire (on passe de l'une à l'autre en glissant sur le téléphone). La première page l'est toujours.</summary>
    public bool Root { get; set; }
    /// <summary>Programmes qui activent cette page quand ils sont au premier plan (ex. "reaper", "discord").</summary>
    public List<string>? AutoProcesses { get; set; }
    /// <summary>Grille propre à cette page (sinon : celle de la config pour l'accueil, automatique pour un dossier).</summary>
    public int? Columns { get; set; }
    public int? Rows { get; set; }
    public List<DeckButton> Buttons { get; set; } = new();
    public List<DeckWidget> Widgets { get; set; } = new();
}

public class DeckButton
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>
    /// "auto" (icône de l'appli), "app:Nom", "builtin:youtube", ou un fichier image.
    /// Plusieurs choix séparés par "|" : le premier qui marche est utilisé.
    /// </summary>
    public string? Icon { get; set; }
    /// <summary>Couleur de fond de la tuile, ex. "#2B2D31".</summary>
    public string? Color { get; set; }
    /// <summary>Case dans la grille (0 = en haut à gauche, on compte ligne par ligne). Vide = première case libre.</summary>
    public int? Slot { get; set; }
    public DeckAction Action { get; set; } = new();
}

public class DeckAction
{
    /// <summary>open | url | keys | text | folder | mixer | micmute | mastermute</summary>
    public string Type { get; set; } = "open";
    /// <summary>open : nom d'appli (menu Démarrer), "chrome.exe", chemin complet ou adresse web.</summary>
    public string? Target { get; set; }
    public string? Args { get; set; }
    /// <summary>open : adresse web ouverte si l'appli est introuvable.</summary>
    public string? FallbackUrl { get; set; }
    /// <summary>folder : id de la page à ouvrir.</summary>
    public string? Page { get; set; }
    /// <summary>keys : raccourci(s), ex. "Ctrl+Shift+M" ou "Ctrl+C, wait:200, Ctrl+V".</summary>
    public string? Keys { get; set; }
    /// <summary>text : texte tapé au clavier, tel quel.</summary>
    public string? Text { get; set; }
    /// <summary>Bouton à bascule : il devient rouge quand on l'active (utile pour un raccourci Discord, par exemple).
    /// Le PC ne peut pas savoir si Discord est vraiment coupé : l'état est celui des appuis.</summary>
    public bool Toggle { get; set; }
}

public class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string DataDir { get; }
    public string ConfigPath { get; }
    public string IconsDir { get; }
    public DeckConfig Config { get; private set; } = new();
    public string? LastError { get; private set; }

    /// <summary>Déclenché quand config.json change (modifié à la main ou par le téléphone).</summary>
    public event Action? Changed;

    /// <summary>Déclenché quand config.json est relu depuis le disque (les icônes peuvent avoir changé).</summary>
    public event Action? Reloaded;

    private readonly FileSystemWatcher? _watcher;
    private DateTime _ignoreUntil = DateTime.MinValue;
    private int _reloadTicket;

    public ConfigStore(string? dataDir = null)
    {
        DataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneDeckPC");
        ConfigPath = Path.Combine(DataDir, "config.json");
        IconsDir = Path.Combine(DataDir, "icons");
        Directory.CreateDirectory(IconsDir);

        Load();
        if (string.IsNullOrWhiteSpace(Config.Token))
        {
            Config.Token = NewToken();
            Save();
        }

        try
        {
            _watcher = new FileSystemWatcher(DataDir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => ScheduleReload();
            _watcher.Created += (_, _) => ScheduleReload();
            _watcher.Renamed += (_, _) => ScheduleReload();
        }
        catch { /* pas de rechargement auto, tant pis */ }
    }

    public string PcName => string.IsNullOrWhiteSpace(Config.PcName) ? Environment.MachineName : Config.PcName!;

    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Config = DefaultConfig.Create();
            Save();
            return;
        }
        try
        {
            var text = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<DeckConfig>(text, Json) ?? DefaultConfig.Create();
            LastError = null;
        }
        catch (Exception e)
        {
            // On garde l'ancienne config et on signale l'erreur dans la fenêtre.
            LastError = "config.json invalide : " + e.Message;
        }
    }

    public void Save()
    {
        lock (this)
        {
            _ignoreUntil = DateTime.UtcNow.AddSeconds(1);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, Json));
        }
    }

    public void ReloadNow()
    {
        Load();
        Reloaded?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Remplace toute la configuration (depuis le configurateur), l'enregistre et prévient le téléphone.</summary>
    public void Replace(DeckConfig config)
    {
        // Le code d'appairage et le port ne se changent pas depuis le configurateur.
        config.Token = Config.Token;
        config.Port = Config.Port;
        // Les images personnalisées sont gérées côté PC (le configurateur ne les connaît pas à jour).
        config.IconOverrides = Config.IconOverrides;
        config.Device = Config.Device;
        config.AdaptedTo = Config.AdaptedTo;
        Config = config;
        LastError = null;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Notifie un changement fait en mémoire (déjà sauvegardé).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    private void ScheduleReload()
    {
        if (DateTime.UtcNow < _ignoreUntil) return;
        var ticket = Interlocked.Increment(ref _reloadTicket);
        Task.Delay(400).ContinueWith(_ =>
        {
            // Les éditeurs écrivent souvent plusieurs fois : on ne recharge qu'une fois.
            if (ticket != _reloadTicket) return;
            ReloadNow();
        });
    }

    private static string NewToken()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }
}

public static class DefaultConfig
{
    public static DeckConfig Create() => new()
    {
        Pages =
        {
            new DeckPage
            {
                Id = "accueil",
                Name = "Accueil",
                Buttons =
                {
                    Open("youtube", "YouTube", "chrome.exe", "https://www.youtube.com", "builtin:youtube", fallbackUrl: "https://www.youtube.com"),
                    Open("claude", "Claude", "Claude", null, "app:Claude|builtin:claude", fallbackUrl: "https://claude.ai"),
                    Open("chrome", "Chrome", "chrome.exe", null, "auto", fallbackUrl: "https://www.google.com"),
                    Folder("discord", "Discord", "discord", "app:Discord|builtin:discord"),
                    new DeckButton { Id = "volume", Label = "Volume", Icon = "builtin:volume", Action = new DeckAction { Type = "mixer" } },
                    Open("reaper", "Reaper", "REAPER", null, "auto"),
                    Folder("launchers", "Launchers", "launchers", "auto"),
                },
            },
            new DeckPage
            {
                Id = "discord",
                Name = "Discord",
                Buttons =
                {
                    Open("discord-open", "Ouvrir", "Discord", null, "auto"),
                    Keys("discord-mute", "Micro", "Ctrl+Shift+F9", "builtin:mic", toggle: true),
                    Keys("discord-deafen", "Sourdine", "Ctrl+Shift+F10", "builtin:headphones", toggle: true),
                },
            },
            new DeckPage
            {
                Id = "launchers",
                Name = "Launchers",
                // Si un dossier "Launcher" existe dans le menu Démarrer, ses raccourcis remplacent la liste ci-dessous.
                StartMenuFolder = "Launcher",
                Buttons =
                {
                    Open("xbox", "Xbox", "Xbox", null, "auto"),
                    Open("steam", "Steam", "Steam", null, "auto"),
                    Open("battlenet", "Battle.net", "Battle.net", null, "auto"),
                    Open("epic", "Epic Games", "Epic Games Launcher", null, "auto"),
                },
            },
        },
    };

    private static DeckButton Open(string id, string label, string target, string? args, string icon, string? fallbackUrl = null) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Action = new DeckAction { Type = "open", Target = target, Args = args, FallbackUrl = fallbackUrl },
    };

    private static DeckButton Folder(string id, string label, string page, string icon) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Action = new DeckAction { Type = "folder", Page = page },
    };

    private static DeckButton Keys(string id, string label, string keys, string icon, bool toggle = false) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        Action = new DeckAction { Type = "keys", Keys = keys, Toggle = toggle },
    };
}
