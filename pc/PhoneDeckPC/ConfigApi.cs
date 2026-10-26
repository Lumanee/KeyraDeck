using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using QRCoder;

namespace PhoneDeckPC;

/// <summary>
/// Le configurateur : une page web (affichée dans la fenêtre du PC) et une petite API.
/// Uniquement accessible depuis le PC lui-même, jamais depuis le réseau.
/// </summary>
public class ConfigApi
{
    private static readonly JsonSerializerOptions Wire = new(ConfigStore.Json) { WriteIndented = false };

    private readonly ConfigStore _store;
    private readonly AppCatalog _catalog;
    private readonly StaWorker _sta;
    private readonly DeckServer _server;
    private readonly Dictionary<string, byte[]?> _appIcons = new();
    private readonly Updater _updater = new();

    /// <summary>Demande à la fenêtre de se fermer (après le téléchargement d'une mise à jour).</summary>
    public Action? ExitRequested { get; set; }

    public ConfigApi(ConfigStore store, AppCatalog catalog, StaWorker sta, DeckServer server)
    {
        _store = store;
        _catalog = catalog;
        _sta = sta;
        _server = server;
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        if (!IsLocal(ctx))
        {
            await ctx.Response.WriteAsync("PhoneDeck PC fonctionne. Connecte l'appli Android avec le QR code.");
            return;
        }

        var path = ctx.Request.Path.Value ?? "/";
        var method = ctx.Request.Method;
        try
        {
            switch (path)
            {
                case "/":
                case "/config":
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.Headers.CacheControl = "no-store";
                    await ctx.Response.WriteAsync(LoadPage());
                    return;

                case "/api/info":
                    await Json(ctx, new
                    {
                        pcName = _store.PcName,
                        build = Updater.CurrentBuild,
                        device = _store.Config.Device,
                        adaptedTo = _store.Config.AdaptedTo,
                        token = _store.Config.Token,
                        port = _store.Config.Port,
                        addresses = LocalNetwork.Addresses(),
                        clients = _server.ClientNames,
                        version = _server.LayoutVersion,
                        configPath = _store.ConfigPath,
                        error = _store.LastError,
                    });
                    return;

                case "/api/qr.png":
                {
                    var ip = ctx.Request.Query["ip"].ToString();
                    if (string.IsNullOrEmpty(ip)) ip = LocalNetwork.Addresses().First();
                    using var gen = new QRCodeGenerator();
                    using var data = gen.CreateQrCode(LocalNetwork.ConnectUrl(ip, _store), QRCodeGenerator.ECCLevel.M);
                    ctx.Response.ContentType = "image/png";
                    await ctx.Response.Body.WriteAsync(new PngByteQRCode(data).GetGraphic(8));
                    return;
                }

                case "/api/config" when method == "GET":
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(JsonSerializer.Serialize(_store.Config, Wire));
                    return;

                case "/api/config" when method == "PUT":
                {
                    var cfg = await JsonSerializer.DeserializeAsync<DeckConfig>(ctx.Request.Body, ConfigStore.Json);
                    if (cfg == null || cfg.Pages.Count == 0) throw new InvalidOperationException("Configuration vide");
                    cfg.Columns = Math.Clamp(cfg.Columns, 1, 12);
                    cfg.Rows = Math.Clamp(cfg.Rows, 1, 12);
                    _store.Replace(cfg);
                    await Json(ctx, new { ok = true });
                    return;
                }

                case "/api/layout":
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(await _server.GetLayoutJsonAsync());
                    return;

                case "/api/apps":
                {
                    if (ctx.Request.Query.ContainsKey("refresh") || _catalog.Apps.Count == 0) await _catalog.RefreshAsync();
                    var apps = _catalog.Apps
                        .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                        .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(a => new { name = a.Name, path = a.ShellPath });
                    await Json(ctx, apps);
                    return;
                }

                case "/api/appicon":
                {
                    var target = ctx.Request.Query["path"].ToString();
                    byte[]? png;
                    lock (_appIcons) _appIcons.TryGetValue(target, out png);
                    if (png == null)
                    {
                        var b64 = await _sta.Run(() => IconService.FromShell(target));
                        png = b64 == null ? null : Convert.FromBase64String(b64);
                        lock (_appIcons) _appIcons[target] = png;
                    }
                    if (png == null) { ctx.Response.StatusCode = 404; return; }
                    ctx.Response.ContentType = "image/png";
                    ctx.Response.Headers.CacheControl = "max-age=3600";
                    await ctx.Response.Body.WriteAsync(png);
                    return;
                }

                case "/api/icon" when method == "POST":
                {
                    var id = ctx.Request.Query["buttonId"].ToString();
                    if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("buttonId manquant");
                    using var ms = new MemoryStream();
                    await ctx.Request.Body.CopyToAsync(ms);
                    if (ms.Length == 0 || ms.Length > 10_000_000) throw new InvalidOperationException("Image vide ou trop lourde");
                    await _server.SetCustomIconAsync(id, ms.ToArray(), ExtensionFor(ctx.Request.ContentType));
                    await Json(ctx, new { ok = true });
                    return;
                }

                case "/api/icon" when method == "DELETE":
                    await _server.ResetIconAsync(ctx.Request.Query["buttonId"].ToString());
                    await Json(ctx, new { ok = true });
                    return;

                case "/api/press" when method == "POST":
                {
                    var (found, error) = await _server.ExecuteAsync(ctx.Request.Query["buttonId"].ToString());
                    await Json(ctx, new { ok = found && error == null, error = found ? error : "Enregistre d'abord ce bouton" });
                    return;
                }

                case "/api/adapted" when method == "POST":
                    _store.Config.AdaptedTo = ctx.Request.Query["key"].ToString();
                    _store.Save();
                    await Json(ctx, new { ok = true });
                    return;

                case "/api/processes":
                    await Json(ctx, await Task.Run(ForegroundWatcher.OpenApps));
                    return;

                case "/api/settings" when method == "GET":
                    await Json(ctx, new { startWithWindows = StartupRegistration.IsEnabled(), build = Updater.CurrentBuild });
                    return;

                case "/api/settings" when method == "POST":
                {
                    using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                    if (doc.RootElement.TryGetProperty("startWithWindows", out var sw))
                        StartupRegistration.Set(sw.GetBoolean());
                    await Json(ctx, new { ok = true });
                    return;
                }

                case "/api/update/check":
                    await Json(ctx, await _updater.CheckAsync());
                    return;

                case "/api/update/apply" when method == "POST":
                {
                    var error = await _updater.DownloadAndPrepareAsync();
                    await Json(ctx, new { ok = error == null, error });
                    if (error == null) _ = Task.Delay(800).ContinueWith(_ => ExitRequested?.Invoke());
                    return;
                }

                case "/api/reload" when method == "POST":
                    _store.ReloadNow();
                    await Json(ctx, new { ok = true, error = _store.LastError });
                    return;

                case "/api/openfolder" when method == "POST":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.DataDir) { UseShellExecute = true });
                    await Json(ctx, new { ok = true });
                    return;
            }
            ctx.Response.StatusCode = 404;
        }
        catch (Exception e)
        {
            ctx.Response.StatusCode = 400;
            await Json(ctx, new { ok = false, error = e.Message });
        }
    }

    /// <summary>Seulement le PC lui-même, et seulement sous le nom « localhost » (protection contre les sites malveillants).</summary>
    private static bool IsLocal(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote)) return false;
        var host = ctx.Request.Host.Host;
        return host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
    }

    private static Task Json(HttpContext ctx, object value)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(value, Wire));
    }

    private static string ExtensionFor(string? contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        _ => ".png",
    };

    private static string LoadPage()
    {
        // En développement, on peut servir la page depuis le disque pour la modifier sans recompiler.
        var dev = Environment.GetEnvironmentVariable("PHONEDECK_WWW");
        if (!string.IsNullOrEmpty(dev) && File.Exists(Path.Combine(dev, "config.html")))
            return File.ReadAllText(Path.Combine(dev, "config.html"));
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("config.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
