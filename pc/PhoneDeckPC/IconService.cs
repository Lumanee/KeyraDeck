using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PhoneDeckPC;

/// <summary>
/// Fabrique les icônes envoyées au téléphone (PNG 128×128 en base64) :
/// icône extraite d'une appli, image importée, ou petit dessin intégré.
/// </summary>
public class IconService
{
    public const int Size = 128;

    private readonly AppCatalog _catalog;
    private readonly ConfigStore _store;
    private readonly Dictionary<string, string?> _cache = new();

    public IconService(AppCatalog catalog, ConfigStore store)
    {
        _catalog = catalog;
        _store = store;
        _store.Reloaded += ClearCache;
    }

    public void ClearCache()
    {
        lock (_cache) _cache.Clear();
    }

    /// <summary>
    /// Icône d'un bouton. <paramref name="children"/> donne les boutons d'une page
    /// (pour dessiner l'aperçu d'un dossier, façon Android).
    /// </summary>
    public string? ForButton(DeckButton button, Func<string, List<DeckButton>> children, int depth = 0)
    {
        if (_store.Config.IconOverrides.TryGetValue(button.Id, out var custom))
        {
            var img = Cached("file:" + custom, () => FromImageFile(custom));
            if (img != null) return img;
        }

        var spec = string.IsNullOrWhiteSpace(button.Icon) ? "auto" : button.Icon!;
        foreach (var choice in spec.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var result = Resolve(choice, button, children, depth);
            if (result != null) return result;
        }
        return Cached("letter:" + button.Label, () => Draw(g => DrawLetter(g, button.Label)));
    }

    private string? Resolve(string choice, DeckButton button, Func<string, List<DeckButton>> children, int depth)
    {
        if (choice.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            return Cached(choice.ToLowerInvariant(), () => Builtin(choice[8..].ToLowerInvariant()));

        // "glyph:E7A7" : pictogramme de la police d'icônes de Windows (code hexadécimal), utilisé par les préréglages.
        if (choice.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(choice[6..], System.Globalization.NumberStyles.HexNumber, null, out var code) || code is < 0xE000 or > 0xF8FF) return null;
            return Cached(choice.ToUpperInvariant(), () => Glyph((char)code));
        }

        // "mdi:feux" ou "mdi:feux#4CAF50" : pictogramme dessiné (voyants, phares, stand…), couleur facultative.
        if (choice.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase))
        {
            // Suffixe "!+" ou "!-" : pastille verte (+) ou rouge (−) dans le coin, pour distinguer TC + de TC −.
            var spec = choice[4..];
            char sign = ' ';
            if (spec.Length > 2 && spec[^2] == '!' && spec[^1] is '+' or '-' or '−') { sign = spec[^1] == '+' ? '+' : '-'; spec = spec[..^2]; }
            var (name, color) = SplitColor(spec);
            if (!Pictos.All.TryGetValue(name, out var picto)) return null;
            var c = color ?? ParseColor(picto.Color) ?? Color.White;
            return Cached("mdi:" + name.ToLowerInvariant() + "#" + c.ToArgb() + sign, () => Picto(picto.Path, c, sign));
        }

        // "brand:discord" ou "brand:steam#FFFFFF" : logo d'appli (Simple Icons), couleur de la marque par défaut.
        if (choice.StartsWith("brand:", StringComparison.OrdinalIgnoreCase))
        {
            var (name, color) = SplitColor(choice[6..]);
            if (!Brands.All.TryGetValue(name, out var brand)) return null;
            var c = color ?? Readable(ParseColor(brand.Color) ?? Color.White);
            return Cached("brand:" + name.ToLowerInvariant() + "#" + c.ToArgb(), () => Picto(brand.Path, c));
        }

        // "text:DRS" ou "text:MAP#FFB300" : sigle en gras dans un cadre, comme sur un volant de course.
        if (choice.StartsWith("text:", StringComparison.OrdinalIgnoreCase))
        {
            var (text, color) = SplitColor(choice[5..]);
            text = text.Trim();
            if (text.Length is 0 or > 5) return null;
            var c = color ?? Color.White;
            return Cached("text:" + text + "#" + c.ToArgb(), () => Badge(text, c));
        }

        if (choice.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var app = _catalog.Find(choice[4..]);
            return app == null ? null : Cached("shell:" + app.ShellPath, () => FromShell(app.ShellPath));
        }

        if (choice.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var a = button.Action;
            switch (a.Type.ToLowerInvariant())
            {
                case "folder":
                    if (depth > 1 || a.Page == null) return Cached("builtin:folder", () => Builtin("folder"));
                    var kids = children(a.Page).Take(4)
                        .Select(k => ForButton(k, children, depth + 1)).ToList();
                    return kids.Count == 0 ? Cached("builtin:folder", () => Builtin("folder")) : FolderPreview(kids);
                case "page":
                    return Cached("builtin:goto", () => Builtin("goto"));
                case "mixer":
                    return Cached("builtin:volume", () => Builtin("volume"));
                case "keys":
                case "text":
                    return Cached("builtin:keyboard", () => Builtin("keyboard"));
                case "micmute":
                    return Cached("builtin:mic", () => Builtin("mic"));
                case "mastermute":
                    return Cached("builtin:volume", () => Builtin("volume"));
                case "url":
                    return Cached("builtin:web", () => Builtin("web"));
                case "obs":
                {
                    var code = (a.Command ?? "").ToLowerInvariant() switch
                    {
                        "stream" => '\uE714', "record" => '\uE7C8', "pause" => '\uE769', "replay" => '\uE72C',
                        "virtualcam" => '\uE8B8', "mute" => '\uE720', _ => '\uE7F4',
                    };
                    return Cached("glyph:obs:" + (int)code, () => Glyph(code));
                }
                case "discord":
                    return (a.Command ?? "").ToLowerInvariant() switch
                    {
                        "deafen" => Cached("builtin:headphones", () => Builtin("headphones")),
                        "leave" => Cached("glyph:E711", () => Glyph('\uE711')),
                        "screen" => Cached("glyph:E7F4", () => Glyph('\uE7F4')),
                        "panel" => Cached("builtin:discord", () => Builtin("discord")),
                        _ => Cached("builtin:mic", () => Builtin("mic")),
                    };
                case "ha":
                {
                    var code = (a.Target ?? "").Split('.')[0] switch
                    {
                        "light" => '\uE706', "scene" => '\uE790', "script" => '\uE756', "media_player" => '\uE768',
                        "cover" or "lock" or "climate" => '\uE80F', _ => '\uE7E8',
                    };
                    return Cached("glyph:ha:" + (int)code, () => Glyph(code));
                }
                default:
                    if (a.Target == null) return null;
                    if (AppCatalog.IsUrl(a.Target)) return Cached("builtin:web", () => Builtin("web"));
                    var src = _catalog.IconSourceFor(a.Target);
                    return src == null ? null : Cached("shell:" + src, () => FromShell(src));
            }
        }

        return Cached("file:" + choice, () => FromImageFile(choice));
    }

    private string? Cached(string key, Func<string?> make)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var v)) return v;
        }
        string? value;
        try { value = make(); }
        catch { value = null; }
        lock (_cache) _cache[key] = value;
        return value;
    }

    // ---------- Sources d'images ----------

    /// <summary>Icône d'un fichier ou d'une appli (y compris Store) via le Shell Windows.</summary>
    public static string? FromShell(string parsingName)
    {
        var bmp = ShellImage.Get(parsingName, Size);
        if (bmp == null) return null;
        using (bmp) return ToBase64(bmp);
    }

    private string? FromImageFile(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(_store.DataDir, path);
        if (!File.Exists(full)) return null;
        using var src = LoadUnlocked(full);
        // Recadrage carré au centre, comme une photo de profil.
        var side = Math.Min(src.Width, src.Height);
        var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
        return Draw(g => g.DrawImage(src, new Rectangle(0, 0, Size, Size), crop, GraphicsUnit.Pixel));
    }

    private static Image LoadUnlocked(string path)
    {
        using var fs = File.OpenRead(path);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        ms.Position = 0;
        return new Bitmap(Image.FromStream(ms));
    }

    private static string FolderPreview(List<string?> icons)
    {
        return Draw(g =>
        {
            using var bg = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            using var path = RoundedRect(new RectangleF(4, 4, Size - 8, Size - 8), 26);
            g.FillPath(bg, path);
            const int cell = 50, gap = 8, start = (Size - 2 * cell - gap) / 2;
            for (int i = 0; i < icons.Count; i++)
            {
                if (icons[i] == null) continue;
                using var ms = new MemoryStream(Convert.FromBase64String(icons[i]!));
                using var img = Image.FromStream(ms);
                int x = start + (i % 2) * (cell + gap), y = start + (i / 2) * (cell + gap);
                g.DrawImage(img, new Rectangle(x, y, cell, cell));
            }
        });
    }

    // ---------- Dessins intégrés ----------

    private static string? Builtin(string name) => name switch
    {
        "youtube" => Draw(g =>
        {
            using var red = new SolidBrush(Color.FromArgb(255, 0, 0));
            using var path = RoundedRect(new RectangleF(8, 24, Size - 16, Size - 48), 22);
            g.FillPath(red, path);
            g.FillPolygon(Brushes.White, new[] { new PointF(52, 44), new PointF(52, 84), new PointF(86, 64) });
        }),
        "claude" => Draw(g =>
        {
            // Étoile orange, clin d'œil au logo de Claude.
            using var pen = new Pen(Color.FromArgb(217, 119, 87), 11) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            const float c = Size / 2f;
            for (int i = 0; i < 12; i++)
            {
                var angle = i * Math.PI / 6 + 0.13;
                var r = i % 2 == 0 ? 50f : 40f;
                g.DrawLine(pen, c, c, c + (float)Math.Cos(angle) * r, c + (float)Math.Sin(angle) * r);
            }
        }),
        "goto" => Draw(g =>
        {
            // Grande flèche vers la droite : « aller à une autre page ».
            g.FillPolygon(Brushes.White, new[] { new PointF(18, 50), new PointF(72, 50), new PointF(72, 28), new PointF(112, 64), new PointF(72, 100), new PointF(72, 78), new PointF(18, 78) });
        }),
        "discord" => Glyph('', Color.FromArgb(88, 101, 242)),
        "volume" => Glyph(''),
        "mic" => Glyph(''),
        "micoff" => Glyph(''),
        "headphones" => Glyph(''),
        "folder" => Glyph(''),
        "keyboard" => Glyph(''),
        "web" => Glyph(''),
        "play" => Glyph(''),
        _ => null,
    };

    private static (string Name, Color? Color) SplitColor(string spec)
    {
        var i = spec.LastIndexOf('#');
        return i < 0 ? (spec, null) : (spec[..i], ParseColor(spec[i..]));
    }

    private static Color? ParseColor(string? hex)
    {
        if (hex == null || !System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) return null;
        return Color.FromArgb(Convert.ToInt32(hex[1..], 16) | unchecked((int)0xFF000000));
    }

    /// <summary>Couleur de marque trop sombre pour un fond noir (GitHub, X, Steam…) : blanc à la place.</summary>
    private static Color Readable(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 60 ? Color.White : c;

    /// <summary>Dessine un pictogramme au trait (voir <see cref="Pictos"/>).</summary>
    private static string Picto(string pathData, Color color, char sign = ' ') => Draw(g =>
    {
        using var path = Pictos.ToPath(pathData, Size, 16);
        using var brush = new SolidBrush(color);
        if (sign == ' ') { g.FillPath(brush, path); return; }

        // Avec une pastille : le pictogramme rétrécit en haut à gauche, le signe est grand en bas à droite.
        var state = g.Save();
        g.TranslateTransform(-8, -8);
        g.ScaleTransform(0.8f, 0.8f);
        g.FillPath(brush, path);
        g.Restore(state);
        var badge = new RectangleF(66, 66, 58, 58);
        using var fill = new SolidBrush(sign == '+' ? Color.FromArgb(46, 160, 67) : Color.FromArgb(214, 48, 49));
        using var ring = new Pen(Color.White, 4);
        g.FillEllipse(fill, badge);
        g.DrawEllipse(ring, badge);
        using var bar = new Pen(Color.White, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        const float cx = 95, cy = 95;
        g.DrawLine(bar, cx - 15, cy, cx + 15, cy);
        if (sign == '+') g.DrawLine(bar, cx, cy - 15, cx, cy + 15);
    });

    /// <summary>Sigle (« DRS », « MAP »…) dans un cadre arrondi.</summary>
    private static string Badge(string text, Color color) => Draw(g =>
    {
        using var pen = new Pen(color, 7);
        using var frame = RoundedRect(new RectangleF(12, 30, Size - 24, Size - 60), 14);
        g.DrawPath(pen, frame);
        var px = text.Length switch { 1 => 54, 2 => 48, 3 => 40, 4 => 31, _ => 25 };
        using var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text.ToUpperInvariant(), font, brush, new RectangleF(0, 2, Size, Size), fmt);
    });

    /// <summary>Dessine un pictogramme de la police d'icônes de Windows.</summary>
    private static string? Glyph(char glyph, Color? color = null)
    {
        var fontName = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                     : FontExists("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
        if (fontName == null) return null;
        return Draw(g =>
        {
            using var font = new Font(fontName, 72, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color ?? Color.White);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, Size, Size), fmt);
        });
    }

    private static void DrawLetter(Graphics g, string label)
    {
        var letter = string.IsNullOrWhiteSpace(label) ? "?" : label.Trim()[..1].ToUpperInvariant();
        using var font = new Font("Segoe UI", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(letter, font, Brushes.White, new RectangleF(0, 0, Size, Size), fmt);
    }

    private static bool FontExists(string name)
    {
        using var f = new Font(name, 12);
        return f.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static string Draw(Action<Graphics> paint)
    {
        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            paint(g);
        }
        return ToBase64(bmp);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static string ToBase64(Image img)
    {
        using var ms = new MemoryStream();
        img.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}

/// <summary>Accès à IShellItemImageFactory : la même icône que dans l'Explorateur / le menu Démarrer.</summary>
internal static class ShellImage
{
    public static Bitmap? Get(string parsingName, int size)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var obj);
            var factory = (IShellItemImageFactory)obj;
            var hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm);
            Marshal.ReleaseComObject(obj);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            return FromHBitmap(hbm);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    /// <summary>Copie un HBITMAP en gardant la transparence (Image.FromHbitmap la perd).</summary>
    private static Bitmap? FromHBitmap(IntPtr hbm)
    {
        var bm = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // de haut en bas
            biPlanes = 1,
            biBitCount = 32,
        };
        var bits = new byte[w * h * 4];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbm, 0, (uint)h, bits, ref header, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, hdc); }

        bool hasAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < h; y++)
            Marshal.Copy(bits, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int c, ref BITMAP bm);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
}
