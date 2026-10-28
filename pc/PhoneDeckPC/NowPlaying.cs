using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using Windows.Media.Control;

namespace PhoneDeckPC;

/// <summary>Ce qui joue en ce moment (pochette en JPEG, prête à être envoyée).</summary>
public record NowPlayingData(string Title, string Artist, string Album, string App, bool Playing, int Pos, int Dur, string ArtKey, byte[]? Art);

/// <summary>
/// Morceau en cours via les contrôles média de Windows (le panneau qui s'affiche quand on touche le volume) :
/// marche avec Spotify, le lecteur Windows, YouTube dans le navigateur… sans aucune clé ni compte.
/// </summary>
public class NowPlaying
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTime _nextManagerTry = DateTime.MinValue;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _trackKey = "";
    private string _artKey = "";
    private byte[]? _art;

    /// <summary>Dernière valeur lue (null = rien ne joue ou Windows trop ancien).</summary>
    public NowPlayingData? Current { get; private set; }

    /// <summary>Relit l'état de Windows (appelé chaque seconde quand un widget l'affiche). Ne lève jamais d'erreur.</summary>
    public async Task<NowPlayingData?> RefreshAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return Current = null;
        try
        {
            if (_manager == null)
            {
                if (DateTime.UtcNow < _nextManagerTry) return Current = null;
                _nextManagerTry = DateTime.UtcNow.AddSeconds(10);
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            var session = _manager.GetCurrentSession();
            _session = session;
            if (session == null) return Current = null;

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var app = AppName(session.SourceAppUserModelId);
            var title = props?.Title ?? "";
            var artist = props?.Artist ?? "";
            if (artist.Length == 0) artist = props?.AlbumArtist ?? "";

            // La pochette ne se relit qu'au changement de morceau.
            var trackKey = $"{session.SourceAppUserModelId}\n{title}\n{artist}";
            if (trackKey != _trackKey)
            {
                _trackKey = trackKey;
                _art = props?.Thumbnail == null ? null : await ReadArtAsync(props.Thumbnail);
                _artKey = _art == null ? "" : Convert.ToHexString(SHA1.HashData(_art))[..12];
            }
            else if (_art == null && props?.Thumbnail != null)
            {
                // Certains lecteurs (Spotify) donnent la pochette une seconde après le titre.
                _art = await ReadArtAsync(props.Thumbnail);
                _artKey = _art == null ? "" : Convert.ToHexString(SHA1.HashData(_art))[..12];
            }

            int pos = 0, dur = 0;
            if (timeline != null && timeline.EndTime > timeline.StartTime)
            {
                dur = (int)(timeline.EndTime - timeline.StartTime).TotalSeconds;
                var p = timeline.Position - timeline.StartTime;
                // Windows ne met pas la position à jour en continu : on avance nous-mêmes depuis la dernière mise à jour.
                if (playing && timeline.LastUpdatedTime.Year > 2000) p += DateTimeOffset.Now - timeline.LastUpdatedTime;
                pos = Math.Clamp((int)p.TotalSeconds, 0, dur);
            }

            if (title.Length == 0 && artist.Length == 0) return Current = null;
            return Current = new NowPlayingData(title, artist, props?.AlbumTitle ?? "", app, playing, pos, dur, _artKey, _art);
        }
        catch
        {
            _manager = null; // on retentera plus tard
            _session = null;
            return Current = null;
        }
    }

    /// <summary>Précédent / lecture-pause / suivant sur le lecteur affiché. Sans lecteur : touches média classiques.</summary>
    public async Task CommandAsync(string action)
    {
        var session = _session;
        try
        {
            if (session != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                var ok = action switch
                {
                    "prev" => await session.TrySkipPreviousAsync(),
                    "next" => await session.TrySkipNextAsync(),
                    "toggle" => await session.TryTogglePlayPauseAsync(),
                    _ => true,
                };
                if (ok) return;
            }
        }
        catch { }
        var key = action switch { "prev" => "Prev", "next" => "Next", "toggle" => "PlayPause", _ => "" };
        if (key.Length > 0) KeySender.Send(key);
    }

    private static async Task<byte[]?> ReadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var input = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            ms.Position = 0;
            using var src = Image.FromStream(ms);
            // Carré de 300 px, recadré au centre (les pochettes YouTube sont en 16/9).
            const int size = 300;
            var side = Math.Min(src.Width, src.Height);
            var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, size, size), crop, GraphicsUnit.Pixel);
            }
            using var outMs = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var prms = new EncoderParameters(1);
            prms.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            bmp.Save(outMs, jpeg, prms);
            return outMs.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Nom lisible du lecteur à partir de l'identifiant Windows (ex. "Spotify.exe" → "Spotify").</summary>
    private static string AppName(string? id)
    {
        id ??= "";
        var low = id.ToLowerInvariant();
        if (low.Contains("spotify")) return "Spotify";
        if (low.Contains("zunemusic") || low.Contains("media.player")) return "Lecteur multimédia";
        if (low.Contains("msedge")) return "Edge";
        if (low.Contains("chrome")) return "Chrome";
        if (low.Contains("firefox") || low == "308046b0af4a39cb") return "Firefox";
        if (low.Contains("opera")) return "Opera";
        if (low.Contains("brave")) return "Brave";
        if (low.Contains("deezer")) return "Deezer";
        if (low.Contains("applemusic") || low.Contains("itunes")) return "Apple Music";
        if (low.Contains("vlc")) return "VLC";
        var name = id.Split('!')[0];
        name = Path.GetFileNameWithoutExtension(name);
        var us = name.IndexOf('_');
        if (us > 0) name = name[..us];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name;
    }
}
