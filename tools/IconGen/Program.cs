using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// ---------------------------------------------------------------------------------------------------
// Generates the app icon: a rounded blue-violet tile with a white notification bell and a green
// "alert" badge — matching the app's accent palette. Emits a multi-resolution app.ico plus a 256px PNG.
// ---------------------------------------------------------------------------------------------------

string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
string icoPath = Path.Combine(root, "src", "App", "app.ico");
string pngPath = Path.Combine(root, "assets", "icon.png");
Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

int[] sizes = { 256, 128, 64, 48, 32, 16 };
var pngs = sizes.ToDictionary(s => s, RenderPng);

// 256px PNG for the README / release page.
File.WriteAllBytes(pngPath, pngs[256]);
Console.WriteLine($"wrote {pngPath}");

// Assemble the .ico (PNG-encoded entries — supported by Windows Vista+).
using (var fs = new FileStream(icoPath, FileMode.Create))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((short)0);            // reserved
    bw.Write((short)1);            // type: icon
    bw.Write((short)sizes.Length); // image count

    int offset = 6 + 16 * sizes.Length;
    foreach (int s in sizes)
    {
        bw.Write((byte)(s >= 256 ? 0 : s)); // width  (0 == 256)
        bw.Write((byte)(s >= 256 ? 0 : s)); // height
        bw.Write((byte)0);                  // palette
        bw.Write((byte)0);                  // reserved
        bw.Write((short)1);                 // color planes
        bw.Write((short)32);                // bits per pixel
        bw.Write(pngs[s].Length);           // size of image data
        bw.Write(offset);                   // offset of image data
        offset += pngs[s].Length;
    }
    foreach (int s in sizes) bw.Write(pngs[s]);
}
Console.WriteLine($"wrote {icoPath}  ({sizes.Length} sizes: {string.Join(", ", sizes)})");
Console.WriteLine("Done.");
return;

// ---------------------------------------------------------------------------------------------------

static byte[] RenderPng(int size)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        DrawIcon(g, size);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static void DrawIcon(Graphics g, int S)
{
    float f(float v) => v * S; // fractional → pixels

    // --- Rounded tile with a diagonal blue-violet gradient ---
    var tile = new RectangleF(f(0.055f), f(0.055f), f(0.89f), f(0.89f));
    float radius = f(0.235f);
    using (var path = Rounded(tile, radius))
    using (var bg = new LinearGradientBrush(tile,
               Color.FromArgb(255, 118, 139, 245),   // top-left
               Color.FromArgb(255, 67, 88, 200),      // bottom-right
               60f))
        g.FillPath(bg, path);

    // Soft top-left highlight for a little depth.
    using (var hp = Rounded(tile, radius))
    using (var glow = new PathGradientBrush(hp)
    {
        CenterColor = Color.FromArgb(55, 255, 255, 255),
        SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
        CenterPoint = new PointF(f(0.34f), f(0.30f)),
    })
        g.FillPath(glow, hp);

    // --- White notification bell (hand-built path, font-independent) ---
    float cx = f(0.47f);
    using var bell = new GraphicsPath();
    bell.AddBezier(
        new PointF(cx - f(0.20f), f(0.60f)),                       // bottom-left of body
        new PointF(cx - f(0.22f), f(0.42f)),
        new PointF(cx - f(0.16f), f(0.25f)),
        new PointF(cx, f(0.235f)));                                // top-centre
    bell.AddBezier(
        new PointF(cx, f(0.235f)),
        new PointF(cx + f(0.16f), f(0.25f)),
        new PointF(cx + f(0.22f), f(0.42f)),
        new PointF(cx + f(0.20f), f(0.60f)));                      // bottom-right of body
    // Gentle downward rim curve back to the left.
    bell.AddBezier(
        new PointF(cx + f(0.20f), f(0.60f)),
        new PointF(cx + f(0.10f), f(0.635f)),
        new PointF(cx - f(0.10f), f(0.635f)),
        new PointF(cx - f(0.20f), f(0.60f)));
    bell.CloseFigure();

    using var white = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
    g.FillPath(white, bell);

    // Rim bar (rounded), clapper, and top nub.
    var rim = new RectangleF(cx - f(0.27f), f(0.595f), f(0.54f), f(0.06f));
    using (var rp = Rounded(rim, rim.Height / 2f))
        g.FillPath(white, rp);
    g.FillEllipse(white, cx - f(0.05f), f(0.66f), f(0.10f), f(0.10f));   // clapper
    g.FillEllipse(white, cx - f(0.035f), f(0.185f), f(0.07f), f(0.07f)); // top nub

    // --- Green "alert" badge, top-right, ringed with the tile color for separation ---
    float bx = f(0.70f), by = f(0.30f), br = f(0.135f);
    using (var ring = new SolidBrush(Color.FromArgb(255, 60, 78, 180)))
        g.FillEllipse(ring, bx - br - f(0.028f), by - br - f(0.028f), (br + f(0.028f)) * 2, (br + f(0.028f)) * 2);
    using (var green = new SolidBrush(Color.FromArgb(255, 52, 199, 89)))
        g.FillEllipse(green, bx - br, by - br, br * 2, br * 2);
}

static GraphicsPath Rounded(RectangleF r, float radius)
{
    var path = new GraphicsPath();
    float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
    if (d < 1f) { path.AddRectangle(r); return path; } // too small to round at this resolution
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
