using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PhoneDeckPC;

/// <summary>
/// Le dossier « Documents\KeyraDeck » : sauvegardes automatiques de la configuration (à chaque modification, portrait et paysage)
/// et pages partagées (export / import, pour la communauté). La config de travail reste dans %APPDATA%\KeyraDeck\config.json.
/// </summary>
public class Backups
{
    /// <summary>Sauvegardes automatiques gardées telles quelles ; au-delà, une seule par jour.</summary>
    private const int KeepRecent = 40;
    private const int KeepDays = 60;
    private const int KeepBeforeRestore = 10;
    /// <summary>Calme demandé avant d'écrire, et écart minimum entre deux sauvegardes automatiques.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);

    public const string Manual = "manuelle";
    public const string BeforeRestore = "avant restauration";

    private static readonly Regex NameRx = new(@"^(\d{4})-(\d{2})-(\d{2}) à (\d{2})h(\d{2})(?: \(([^)]*)\))?(?: \d+)?\.keyradeck-backup\.json$");
    private static readonly Regex ImageRefRx = new(@"^Images/[a-f0-9]{16}\.(png|jpg|jpeg|gif|bmp|ico|webp|mp4|webm)$");
    private static readonly string[] IconExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };
    private static readonly string[] ScreenExtensions = { ".png", ".jpg", ".gif", ".webp", ".mp4", ".webm" };

    private readonly ConfigStore _store;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private DateTime _lastWrite = DateTime.MinValue;
    private string? _lastHash;

    public string Root { get; }
    public string BackupsDir { get; }
    public string ImagesDir { get; }
    public string PagesDir { get; }

    public Backups(ConfigStore store)
    {
        _store = store;
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) docs = Path.Combine(store.DataDir, "Documents");
        Root = Path.Combine(docs, "KeyraDeck");
        BackupsDir = Path.Combine(Root, "Sauvegardes");
        ImagesDir = Path.Combine(BackupsDir, "Images");
        PagesDir = Path.Combine(Root, "Pages");
        try
        {
            Directory.CreateDirectory(ImagesDir);
            Directory.CreateDirectory(PagesDir);
            var newest = Files().FirstOrDefault();
            if (newest != null)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(newest.Path));
                if (doc.RootElement.TryGetProperty("hash", out var h)) _lastHash = h.GetString();
                _lastWrite = newest.Date;
            }
        }
        catch { /* dossier Documents inaccessible : les sauvegardes réessaieront plus tard */ }
        store.Saved += Schedule;
        // Première sauvegarde au démarrage (rien n'est écrit si la config n'a pas changé depuis la dernière).
        Schedule();
    }

    // ---------- Sauvegardes automatiques ----------

    /// <summary>Une modification vient d'être enregistrée : sauvegarde dès que l'utilisateur fait une pause.</summary>
    public void Schedule()
    {
        lock (_gate)
        {
            var due = DateTime.Now + Quiet;
            var earliest = _lastWrite + MinGap;
            if (due < earliest) due = earliest;
            var wait = due - DateTime.Now;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => { try { Snapshot(null); } catch { } }, null, wait, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Écrit une sauvegarde (tag = null : automatique, ignorée si rien n'a changé). Renvoie le nom du fichier.</summary>
    public string? Snapshot(string? tag)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(ImagesDir);
            var (cfg, icons, screens) = Collect(embed: false);
            var hash = Hash(cfg, icons, screens);
            if (tag == null && hash == _lastHash) return null;
            var now = DateTime.Now;
            var baseName = $"{now:yyyy-MM-dd} à {now:HH}h{now:mm}" + (tag != null ? $" ({tag})" : "");
            var name = baseName + ".keyradeck-backup.json";
            for (var i = 2; File.Exists(Path.Combine(BackupsDir, name)); i++) name = $"{baseName} {i}.keyradeck-backup.json";
            var data = new
            {
                keyradeck = "backup", version = 2, build = Updater.CurrentBuild, appVersion = Updater.CurrentVersion, date = now,
                kind = tag ?? "auto", hash, config = cfg, icons, screens,
            };
            var tmp = Path.Combine(BackupsDir, name + ".tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, ConfigStore.Json));
            File.Move(tmp, Path.Combine(BackupsDir, name), true);
            _lastHash = hash;
            _lastWrite = now;
            try { Prune(); } catch { }
            return name;
        }
    }

    /// <summary>Sauvegarde complète dans un seul fichier (images et fonds inclus), pour changer de PC.</summary>
    public object BuildPortable()
    {
        var (cfg, icons, screens) = Collect(embed: true);
        return new { keyradeck = "backup", version = 2, build = Updater.CurrentBuild, appVersion = Updater.CurrentVersion, date = DateTime.Now, config = cfg, icons, screens };
    }

    /// <summary>
    /// Copie de la config sans ce qui appartient à ce PC (code d'appairage, téléphone), avec ses images personnalisées
    /// et ses fonds d'écran : inclus dans le fichier (embed) ou rangés une seule fois dans « Sauvegardes\Images ».
    /// </summary>
    private (DeckConfig cfg, Dictionary<string, object> icons, Dictionary<string, object> screens) Collect(bool embed)
    {
        DeckConfig cfg;
        Dictionary<string, string> overrides;
        lock (_store)
        {
            cfg = JsonSerializer.Deserialize<DeckConfig>(JsonSerializer.Serialize(_store.Config, ConfigStore.Json), ConfigStore.Json)!;
            overrides = new Dictionary<string, string>(_store.Config.IconOverrides);
        }
        cfg.Token = "";
        cfg.PcName = null;
        cfg.Device = null;
        cfg.AdaptedTo = null;
        cfg.IconOverrides = new();
        var icons = new Dictionary<string, object>();
        foreach (var (id, rel) in overrides.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            try
            {
                var path = Path.IsPathRooted(rel) ? rel : Path.Combine(_store.DataDir, rel);
                if (!File.Exists(path)) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var bytes = File.ReadAllBytes(path);
                icons[id] = embed ? new { ext, data = Convert.ToBase64String(bytes) } : new { ext, file = KeepImage(bytes, ext) };
            }
            catch { /* image illisible : le bouton reprendra son icône auto */ }
        }
        var screens = new Dictionary<string, object>();
        var bgs = cfg.Pages.SelectMany(p => new[] { p.Screen?.Bg, p.Land?.Screen?.Bg }).Where(b => b != null).Distinct().OrderBy(b => b, StringComparer.Ordinal);
        foreach (var bg in bgs)
        {
            try
            {
                var path = _store.ScreenBgPath(bg);
                if (path == null) continue;
                if (embed) screens[bg!] = new { data = Convert.ToBase64String(File.ReadAllBytes(path)) };
                else
                {
                    var dest = Path.Combine(ImagesDir, bg!);
                    if (!File.Exists(dest)) File.Copy(path, dest);
                    screens[bg!] = new { file = "Images/" + bg };
                }
            }
            catch { }
        }
        return (cfg, icons, screens);
    }

    /// <summary>Range une image dans « Sauvegardes\Images » sous un nom tiré de son contenu (jamais en double).</summary>
    private string KeepImage(byte[] bytes, string ext)
    {
        var name = Sha16(bytes) + ext;
        var dest = Path.Combine(ImagesDir, name);
        if (!File.Exists(dest)) File.WriteAllBytes(dest, bytes);
        return "Images/" + name;
    }

    private static string Sha16(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();

    private static string Hash(DeckConfig cfg, object icons, object screens) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { cfg, icons, screens }, ConfigStore.Json))))[..24];

    public record BackupFile(string Name, string Path, DateTime Date, string? Tag);

    /// <summary>Les sauvegardes du dossier, de la plus récente à la plus ancienne.</summary>
    private List<BackupFile> Files()
    {
        if (!Directory.Exists(BackupsDir)) return new();
        var list = new List<BackupFile>();
        foreach (var path in Directory.GetFiles(BackupsDir, "*.keyradeck-backup.json"))
        {
            var name = Path.GetFileName(path);
            var m = NameRx.Match(name);
            if (!m.Success) continue;
            try
            {
                var date = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                    int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
                list.Add(new BackupFile(name, path, date, m.Groups[6].Success ? m.Groups[6].Value : null));
            }
            catch { }
        }
        // Même minute : le fichier écrit en dernier d'abord.
        return list.OrderByDescending(f => f.Date).ThenByDescending(f => File.GetLastWriteTimeUtc(f.Path)).ToList();
    }

    /// <summary>Liste affichée dans le configurateur.</summary>
    public List<object> List()
    {
        var result = new List<object>();
        foreach (var f in Files())
        {
            int pages = 0, folders = 0;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f.Path));
                if (doc.RootElement.TryGetProperty("config", out var c) && c.TryGetProperty("pages", out var ps) && ps.ValueKind == JsonValueKind.Array)
                    foreach (var p in ps.EnumerateArray())
                        if (p.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.True) pages++; else folders++;
            }
            catch { continue; }
            result.Add(new { file = f.Name, date = f.Date, tag = f.Tag, pages = Math.Max(1, pages), folders = pages == 0 ? Math.Max(0, folders - 1) : folders });
        }
        return result;
    }

    /// <summary>Contenu d'une sauvegarde du dossier (nom tel que donné par <see cref="List"/>).</summary>
    public string Read(string file)
    {
        if (!NameRx.IsMatch(file)) throw new InvalidOperationException("Sauvegarde introuvable");
        var path = Path.Combine(BackupsDir, file);
        if (!File.Exists(path)) throw new InvalidOperationException("Sauvegarde introuvable");
        return File.ReadAllText(path);
    }

    /// <summary>Octets d'une image référencée par une sauvegarde (« Images/… »), ou null.</summary>
    public byte[]? ReadImage(string? rel)
    {
        if (rel == null || !ImageRefRx.IsMatch(rel)) return null;
        var path = Path.Combine(ImagesDir, rel["Images/".Length..]);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Garde les 40 dernières, puis une par jour pendant 60 jours ; les manuelles restent toujours.</summary>
    private void Prune()
    {
        var files = Files();
        var keep = new HashSet<string>();
        var auto = files.Where(f => f.Tag == null).ToList();
        foreach (var f in auto.Take(KeepRecent)) keep.Add(f.Name);
        foreach (var day in auto.Skip(KeepRecent).Where(f => f.Date > DateTime.Now.AddDays(-KeepDays)).GroupBy(f => f.Date.Date))
            keep.Add(day.First().Name);
        foreach (var f in files.Where(f => f.Tag == BeforeRestore).Take(KeepBeforeRestore)) keep.Add(f.Name);
        foreach (var f in files.Where(f => f.Tag != null && f.Tag != BeforeRestore)) keep.Add(f.Name);
        var removed = false;
        foreach (var f in files.Where(f => !keep.Contains(f.Name)))
        {
            try { File.Delete(f.Path); removed = true; } catch { }
        }
        if (!removed) return;

        // Images que plus aucune sauvegarde n'utilise.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Files())
        {
            try
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(f.Path), "\"Images/([a-f0-9]{16}\\.[a-z0-9]+)\"")) used.Add(m.Groups[1].Value);
            }
            catch { return; } // fichier illisible : on ne supprime rien par prudence
        }
        foreach (var img in Directory.GetFiles(ImagesDir))
        {
            var n = Path.GetFileName(img);
            if (used.Contains(n) || File.GetLastWriteTime(img) > DateTime.Now.AddMinutes(-5)) continue;
            try { File.Delete(img); } catch { }
        }
    }

    // ---------- Pages partagées (fichiers .keyradeck) ----------

    /// <summary>
    /// Écrit une page dans « Documents\KeyraDeck\Pages » : un fichier .keyradeck (zip) avec page.json (portrait + paysage)
    /// et ses images personnalisées et fonds d'écran dans images/. Les boutons portent « _src » = id d'origine (pour retrouver leur image).
    /// </summary>
    public string ExportPage(JsonObject page)
    {
        var files = new Dictionary<string, byte[]>();
        if (page["buttons"] is JsonArray buttons)
            foreach (var b in buttons.OfType<JsonObject>())
            {
                var src = b["_src"] is JsonValue sv && sv.TryGetValue<string>(out var ss) ? ss : null;
                b.Remove("_src");
                b.Remove("image");
                if (src == null) continue;
                string? rel;
                lock (_store) _store.Config.IconOverrides.TryGetValue(src, out rel);
                if (rel == null) continue;
                var path = Path.IsPathRooted(rel) ? rel : Path.Combine(_store.DataDir, rel);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!File.Exists(path) || !IconExtensions.Contains(ext)) continue;
                var bytes = File.ReadAllBytes(path);
                var name = "images/" + Sha16(bytes) + ext;
                files[name] = bytes;
                b["image"] = name;
            }
        foreach (var screen in new[] { page["screen"] as JsonObject, (page["land"] as JsonObject)?["screen"] as JsonObject })
        {
            if (screen == null) continue;
            var bg = screen["bg"] is JsonValue bv && bv.TryGetValue<string>(out var bs) ? bs : null;
            screen.Remove("bg");
            var path = _store.ScreenBgPath(bg);
            if (path == null) continue;
            var name = "images/" + bg;
            if (!files.ContainsKey(name)) files[name] = File.ReadAllBytes(path);
            screen["bg"] = name;
        }

        var title = page["name"] is JsonValue t && t.TryGetValue<string>(out var ts) ? ts : "Page";
        var doc = new JsonObject
        {
            ["keyradeck"] = "page", ["version"] = 2, ["appVersion"] = Updater.CurrentVersion, ["date"] = DateTime.Now, ["page"] = page,
        };
        var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "Page";
        Directory.CreateDirectory(PagesDir);
        var dest = Path.Combine(PagesDir, safe + ".keyradeck");
        var tmp = dest + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("page.json", CompressionLevel.Optimal);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                w.Write(doc.ToJsonString(ConfigStore.Json));
            foreach (var (name, bytes) in files)
            {
                // Les vidéos et images sont déjà compressées.
                using var s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(bytes);
            }
        }
        File.Move(tmp, dest, true);
        return dest;
    }

    /// <summary>Pages du dossier « Documents\KeyraDeck\Pages », pour la galerie « ＋ Page ».</summary>
    public List<object> ListPages()
    {
        var result = new List<object>();
        if (!Directory.Exists(PagesDir)) return result;
        foreach (var path in Directory.GetFiles(PagesDir).Where(IsPageFile).OrderByDescending(File.GetLastWriteTime))
        {
            try
            {
                var page = ReadPageDoc(File.ReadAllBytes(path), out var zip)["page"] as JsonObject;
                zip?.Dispose();
                result.Add(new
                {
                    file = Path.GetFileName(path),
                    name = page?["name"] is JsonValue n && n.TryGetValue<string>(out var ns) ? ns : Path.GetFileNameWithoutExtension(path),
                    buttons = (page?["buttons"] as JsonArray)?.Count ?? 0,
                    land = page?["land"] != null,
                    date = File.GetLastWriteTime(path),
                });
            }
            catch { /* fichier abîmé : pas proposé */ }
        }
        return result;
    }

    private static bool IsPageFile(string path) =>
        path.EndsWith(".keyradeck", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".keyradeck.json", StringComparison.OrdinalIgnoreCase);

    public byte[] ReadPageFile(string file)
    {
        if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !IsPageFile(file)) throw new InvalidOperationException("Fichier introuvable");
        var path = Path.Combine(PagesDir, file);
        if (!File.Exists(path)) throw new InvalidOperationException("Fichier introuvable");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Lit une page partagée (.keyradeck ou ancien .keyradeck.json). Ses images vont dans les dossiers de KeyraDeck :
    /// « image » d'un bouton devient « icons/… » (à donner à /api/icon/assign), « bg » d'une zone écran devient le nom du fond.
    /// </summary>
    public JsonObject ImportPage(byte[] bytes)
    {
        var doc = ReadPageDoc(bytes, out var zip);
        using (zip)
        {
            var page = doc["page"] as JsonObject ?? throw new InvalidOperationException("ce n'est pas une page KeyraDeck");
            byte[]? Entry(string? name, long max)
            {
                if (zip == null || name == null || !name.StartsWith("images/") || name.Contains("..")) return null;
                var e = zip.GetEntry(name);
                if (e == null || e.Length > max) return null;
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.Length > max ? null : ms.ToArray();
            }
            if (page["buttons"] is JsonArray buttons)
                foreach (var b in buttons.OfType<JsonObject>())
                {
                    var name = b["image"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                    b.Remove("image");
                    var ext = Path.GetExtension(name ?? "").ToLowerInvariant();
                    if (!IconExtensions.Contains(ext)) continue;
                    var data = Entry(name, 10_000_000);
                    if (data == null) continue;
                    var file = $"imp-{Sha16(data)}{ext}";
                    var path = Path.Combine(_store.IconsDir, file);
                    if (!File.Exists(path)) File.WriteAllBytes(path, data);
                    b["image"] = "icons/" + file;
                }
            foreach (var screen in new[] { page["screen"] as JsonObject, (page["land"] as JsonObject)?["screen"] as JsonObject })
            {
                if (screen == null) continue;
                var name = screen["bg"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                screen.Remove("bg");
                var ext = Path.GetExtension(name ?? "").ToLowerInvariant();
                if (ext == ".jpeg") ext = ".jpg";
                if (!ScreenExtensions.Contains(ext)) continue;
                var data = Entry(name, 25_000_000);
                if (data == null) continue;
                var file = Sha16(data) + ext;
                var path = Path.Combine(_store.ScreensDir, file);
                if (!File.Exists(path)) File.WriteAllBytes(path, data);
                screen["bg"] = file;
            }
            doc.Remove("page");
            return page;
        }
    }

    private static JsonObject ReadPageDoc(byte[] bytes, out ZipArchive? zip)
    {
        zip = null;
        string text;
        if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = zip.GetEntry("page.json");
            if (entry == null || entry.Length > 5_000_000) { zip.Dispose(); throw new InvalidOperationException("ce n'est pas une page KeyraDeck"); }
            using var r = new StreamReader(entry.Open());
            text = r.ReadToEnd();
        }
        else text = Encoding.UTF8.GetString(bytes);
        JsonObject? doc;
        try { doc = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject; }
        catch { doc = null; }
        var kind = doc?["keyradeck"] is JsonValue k && k.TryGetValue<string>(out var ks) ? ks : null;
        if (doc == null || kind != "page" || doc["page"] is not JsonObject)
        {
            zip?.Dispose();
            throw new InvalidOperationException(kind == "backup"
                ? "c'est une sauvegarde complète : utilise « Restaurer une sauvegarde »" : "ce n'est pas une page KeyraDeck");
        }
        return doc;
    }
}
