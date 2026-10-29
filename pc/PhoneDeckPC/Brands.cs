using System.Reflection;
using System.Text.Json;

namespace PhoneDeckPC;

/// <summary>
/// Logos d'applis (icônes « brand: » des boutons) : Steam, Discord, Spotify, Chrome… Tracés de Simple Icons (CC0),
/// sur une grille de 24×24, avec la couleur de la marque. La liste est générée par tools/brands/build.py.
/// </summary>
public static class Brands
{
    public sealed record Brand(string Name, string Title, string Color, string[] Categories, string Words, string Path);

    private static readonly Lazy<Dictionary<string, Brand>> _all = new(Load);
    public static IReadOnlyDictionary<string, Brand> All => _all.Value;

    /// <summary>Le fichier tel quel, pour le configurateur (/api/brands).</summary>
    public static byte[] Json()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Brands.json")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static Dictionary<string, Brand> Load()
    {
        var list = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(Json());
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var b = new Brand(e.GetProperty("n").GetString()!, e.GetProperty("t").GetString()!, e.GetProperty("c").GetString()!,
                e.GetProperty("k").EnumerateArray().Select(k => k.GetString()!).ToArray(), e.GetProperty("w").GetString()!, e.GetProperty("p").GetString()!);
            list[b.Name] = b;
        }
        return list;
    }
}
