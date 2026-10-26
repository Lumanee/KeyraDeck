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
                case "mixer":
                    return Cached("builtin:volume", () => Builtin("volume"));
                case "keys":
                    return Cached("builtin:keyboard", () => Builtin("keyboard"));
                case "url":
                    return Cached("builtin:web", () => Builtin("web"));
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
