using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Settings;
using PhoneNotificationsVR.Overlay;

// ---------------------------------------------------------------------------------------------------
// Generates promotional images by calling the app's real NotificationRenderer. The individual cards are
// therefore identical to what SteamVR shows. The "hero" composite places those real cards over a
// generated backdrop (clearly a design backdrop, not a captured game frame) so nothing is misrepresented.
// ---------------------------------------------------------------------------------------------------

string outDir = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "assets");
Directory.CreateDirectory(outDir);

var renderer = new NotificationRenderer(new FixedSettings());

// Sample notifications straight from the spec's examples.
var samples = new (string file, PhoneNotification n)[]
{
    ("card-call", new PhoneNotification
    {
        Uid = 1, AppIdentifier = "com.apple.mobilephone", AppName = "Phone",
        Title = "John Smith", Category = NotificationCategory.IncomingCall,
        Timestamp = At(21, 04),
    }),
    ("card-whatsapp", new PhoneNotification
    {
        Uid = 2, AppIdentifier = "net.whatsapp.WhatsApp", AppName = "WhatsApp",
        Title = "Alice", Message = "Are you free tonight?", Category = NotificationCategory.Social,
        Timestamp = At(21, 02),
    }),
    ("card-discord", new PhoneNotification
    {
        Uid = 3, AppIdentifier = "com.hammerandchisel.discord", AppName = "Discord",
        Title = "Mike", Message = "Join the VC — we’re waiting on you!", Category = NotificationCategory.Social,
        Timestamp = At(20, 58),
    }),
    ("card-imessage", new PhoneNotification
    {
        Uid = 4, AppIdentifier = "com.apple.MobileSMS", AppName = "Messages",
        Title = "Mom", Message = "Call me when you get a chance", Category = NotificationCategory.Social,
        Timestamp = At(20, 51),
    }),
    ("card-telegram", new PhoneNotification
    {
        Uid = 5, AppIdentifier = "ph.telegra.Telegraph", AppName = "Telegram",
        Title = "Dev Team", Message = "CI build passed — ready to ship", Category = NotificationCategory.Social,
        Timestamp = At(20, 40),
    }),
};

// 1. Individual transparent card PNGs (authentic renderer output).
var bitmaps = new Dictionary<string, Bitmap>();
foreach (var (file, n) in samples)
{
    var bmp = ToBitmap(renderer.Render(n));
    bitmaps[file] = bmp;
    string path = Path.Combine(outDir, file + ".png");
    bmp.Save(path, ImageFormat.Png);
    Console.WriteLine($"wrote {path}  ({bmp.Width}x{bmp.Height})");
}

// 2. Hero banner: three cards over a generated VR-style backdrop.
SaveHero(Path.Combine(outDir, "hero.png"),
    new[] { bitmaps["card-call"], bitmaps["card-whatsapp"], bitmaps["card-discord"] });

// 3. Card gallery: all five cards on a neutral dark canvas.
SaveGallery(Path.Combine(outDir, "cards-gallery.png"),
    samples.Select(s => bitmaps[s.file]).ToArray());

Console.WriteLine("Done.");
return;

// ---------------------------------------------------------------------------------------------------

static DateTimeOffset At(int h, int m) =>
    new(DateTime.Today.AddHours(h).AddMinutes(m), DateTimeOffset.Now.Offset);

// Convert the renderer's RGBA output back into a GDI+ bitmap (BGRA in memory).
static Bitmap ToBitmap(RenderedCard card)
{
    var bmp = new Bitmap(card.Width, card.Height, PixelFormat.Format32bppArgb);
    var rect = new Rectangle(0, 0, card.Width, card.Height);
    var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    try
    {
        int count = card.Width * card.Height * 4;
        var dst = new byte[count];
        for (int i = 0; i < count; i += 4)
        {
            dst[i + 0] = card.Rgba[i + 2]; // B
            dst[i + 1] = card.Rgba[i + 1]; // G
            dst[i + 2] = card.Rgba[i + 0]; // R
            dst[i + 3] = card.Rgba[i + 3]; // A
        }
        Marshal.Copy(dst, 0, data.Scan0, count);
    }
    finally { bmp.UnlockBits(data); }
    return bmp;
}

static void SaveHero(string path, Bitmap[] cards)
{
    const int W = 1600, H = 900;
    using var canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

    // Deep diagonal gradient backdrop.
    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
               Color.FromArgb(255, 28, 22, 54), Color.FromArgb(255, 8, 9, 16), 55f))
        g.FillRectangle(bg, 0, 0, W, H);

    // Soft accent glow blobs for depth.
    DrawGlow(g, new Point(280, 180), 520, Color.FromArgb(70, 94, 132, 241));
    DrawGlow(g, new Point(1350, 760), 620, Color.FromArgb(55, 52, 199, 89));

    // Subtle grid to suggest a virtual space.
    using (var grid = new Pen(Color.FromArgb(16, 255, 255, 255), 1f))
    {
        for (int x = 0; x <= W; x += 64) g.DrawLine(grid, x, 0, x, H);
        for (int y = 0; y <= H; y += 64) g.DrawLine(grid, 0, y, W, y);
    }

    // Headline.
    using var h1 = new Font("Segoe UI", 58f, FontStyle.Bold);
    using var h2 = new Font("Segoe UI", 26f, FontStyle.Regular);
    using var tag = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
    using var white = new SolidBrush(Color.FromArgb(250, 255, 255, 255));
    using var dim = new SolidBrush(Color.FromArgb(200, 190, 196, 210));
    using var accent = new SolidBrush(Color.FromArgb(255, 120, 150, 255));

    g.DrawString("Never miss your", h1, white, 96, 150);
    g.DrawString("phone in VR.", h1, accent, 96, 226);
    g.DrawString("iPhone calls & messages, right inside SteamVR.", h2, dim, 100, 330);

    // Little pill tag.
    var pill = new Rectangle(100, 396, 300, 44);
    using (var pb = new SolidBrush(Color.FromArgb(40, 120, 150, 255)))
        FillRounded(g, pill, 22, pb);
    g.DrawString("STEAMVR OVERLAY • .NET 8", tag, accent, 122, 406);

    // Stack the three real cards on the right with shadows, slightly overlapped.
    int cx = 830, cy = 150, step = 200;
    for (int i = 0; i < cards.Length; i++)
    {
        var c = cards[i];
        float s = 0.92f;
        int w = (int)(c.Width * s), h = (int)(c.Height * s);
        int x = cx + i * 26, y = cy + i * step;
        // shadow
        using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            FillRounded(g, new Rectangle(x + 8, y + 14, w, h), 24, sh);
        g.DrawImage(c, new Rectangle(x, y, w, h));
    }

    canvas.Save(path, ImageFormat.Png);
    Console.WriteLine($"wrote {path}  ({W}x{H})");
}

static void SaveGallery(string path, Bitmap[] cards)
{
    const int W = 1500, pad = 60, gap = 34;
    int inner = W - pad * 2;
    // scale cards to a common width
    float scale = inner / (float)cards[0].Width;
    int totalH = pad * 2 + (cards.Length - 1) * gap;
    foreach (var c in cards) totalH += (int)(c.Height * scale);

    using var canvas = new Bitmap(W, totalH, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, totalH),
               Color.FromArgb(255, 18, 19, 26), Color.FromArgb(255, 10, 11, 16), 90f))
        g.FillRectangle(bg, 0, 0, W, totalH);

    int y = pad;
    foreach (var c in cards)
    {
        int w = inner, h = (int)(c.Height * scale);
        using (var sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            FillRounded(g, new Rectangle(pad + 6, y + 10, w, h), 24, sh);
        g.DrawImage(c, new Rectangle(pad, y, w, h));
        y += h + gap;
    }

    canvas.Save(path, ImageFormat.Png);
    Console.WriteLine($"wrote {path}  ({W}x{totalH})");
}

static void DrawGlow(Graphics g, Point center, int radius, Color color)
{
    using var path = new GraphicsPath();
    path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
    using var pgb = new PathGradientBrush(path)
    {
        CenterColor = color,
        SurroundColors = new[] { Color.FromArgb(0, color) },
    };
    g.FillPath(pgb, path);
}

static void FillRounded(Graphics g, Rectangle r, int radius, Brush brush)
{
    using var path = new GraphicsPath();
    int d = radius * 2;
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    g.FillPath(brush, path);
}

/// <summary>Minimal settings service so the renderer can read FontScale etc. (design defaults).</summary>
file sealed class FixedSettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public event EventHandler<AppSettings>? Changed;
    public void Load() { }
    public void Save() { }
}
