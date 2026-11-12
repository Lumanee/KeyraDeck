using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;

namespace PhoneDeckPC;

/// <summary>
/// Bibliothèque d'images « Documents\KeyraDeck\Icones » : l'utilisateur y range (ou importe depuis un .zip) les packs d'icônes
/// de son choix, par exemple les icônes d'un jeu, et les retrouve dans le configurateur. Rien n'est livré avec l'appli.
/// </summary>
public static class IconLibrary
{
    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg" };
    private const int MaxFiles = 20_000;
    private const long MaxEntryBytes = 5_000_000;
    /// <summary>Taille des images posées sur un bouton : les petites icônes (souvent 60×60) sont agrandies à cette taille.</summary>
    private const int Hd = 256;

    public record Item(string P, string N, string C);

    public static IEnumerable<Item> List(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (!Exts.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            if (++count > MaxFiles) yield break;
            var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
            var cat = Path.GetDirectoryName(rel)?.Replace('\\', '/').Replace("/", " / ") ?? "";
            yield return new Item(rel, Path.GetFileNameWithoutExtension(path), cat);
        }
    }

    /// <summary>Chemin complet d'une image de la bibliothèque, ou null si le chemin sort du dossier ou n'existe pas.</summary>
    public static string? Resolve(string dir, string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || !Exts.Contains(Path.GetExtension(rel).ToLowerInvariant())) return null;
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))); } catch { return null; }
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }

    private static string Clean(string segment)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = string.Concat(segment.Select(c => bad.Contains(c) || c == ':' ? '-' : c)).Trim().TrimEnd('.');
        return s.Length > 100 ? s[..100] : s;
    }

    /// <summary>Range les images d'un .zip dans la bibliothèque (sous-dossiers conservés). Renvoie le nombre d'images ajoutées.</summary>
    public static int Import(string dir, byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var added = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0 || entry.Name.StartsWith('.') || entry.Length > MaxEntryBytes) continue;
            if (!Exts.Contains(Path.GetExtension(entry.Name).ToLowerInvariant())) continue;
            var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => p == "__MACOSX" || p == "." || p == "..")) continue;
            var dest = Path.GetFullPath(Path.Combine(root, Path.Combine(parts.Select(Clean).Where(p => p.Length > 0).ToArray())));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (++added > MaxFiles) break;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var input = entry.Open();
            using var output = File.Create(dest);
            input.CopyTo(output);
        }
        return added;
    }

    /// <summary>L'image agrandie en 256×256 (interpolation bicubique de haute qualité, transparence conservée), au format PNG.</summary>
    public static byte[] Hd256(string file)
    {
        using var src = new Bitmap(file);
        using var dst = new Bitmap(Hd, Hd, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            // Image carrée remplissant le bouton ; sinon, conservée entière au centre.
            var k = Math.Min((double)Hd / src.Width, (double)Hd / src.Height);
            int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY); // évite le liseré transparent sur les bords
            g.DrawImage(src, new Rectangle((Hd - w) / 2, (Hd - h) / 2, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        using var ms = new MemoryStream();
        dst.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
