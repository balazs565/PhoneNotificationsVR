using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;
using PhoneNotificationsVR.Core.Settings;

namespace PhoneNotificationsVR.Overlay;

/// <summary>
/// Renders a notification into a rounded, semi-transparent "card" bitmap in the Quest/Meta style,
/// then converts it to RGBA32 for OpenVR. The same output feeds the desktop preview.
///
/// Layout (matches the spec):
///   [icon]  APP NAME · time
///           Sender
///           Title
///           Message (wrapped, up to 3 lines)
/// </summary>
public sealed class NotificationRenderer : INotificationRenderer
{
    private readonly ISettingsService _settings;

    private const int Width = 720;
    private const int Padding = 28;
    private const int IconSize = 88;

    public NotificationRenderer(ISettingsService settings) => _settings = settings;

    public RenderedCard Render(PhoneNotification n, double opacity = 1.0)
    {
        var o = _settings.Current.Overlay;
        float scale = (float)Math.Clamp(o.FontScale, 0.6, 2.0);

        using var appFont = new Font("Segoe UI Semibold", 20f * scale, FontStyle.Bold);
        using var timeFont = new Font("Segoe UI", 15f * scale);
        using var senderFont = new Font("Segoe UI Semibold", 22f * scale, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI", 19f * scale);
        using var bodyFont = new Font("Segoe UI", 19f * scale);

        var accent = AccentFor(n);

        // First measure the wrapped text to compute the needed height.
        int textLeft = Padding + IconSize + 20;
        int textWidth = Width - textLeft - Padding;

        using var measure = new Bitmap(1, 1);
        using var mg = Graphics.FromImage(measure);
        mg.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int y = Padding;
        int height = Padding;                                   // top padding
        height += (int)Math.Ceiling(appFont.GetHeight(mg)) + 6; // app line
        if (!string.IsNullOrWhiteSpace(n.BestSender))
            height += (int)Math.Ceiling(senderFont.GetHeight(mg)) + 4;

        string title = TitleFor(n);
        if (!string.IsNullOrWhiteSpace(title))
            height += (int)Math.Ceiling(titleFont.GetHeight(mg)) + 4;

        var bodyLines = WrapText(mg, n.Message, bodyFont, textWidth, maxLines: 3);
        height += bodyLines.Count * ((int)Math.Ceiling(bodyFont.GetHeight(mg)) + 2);
        height += Padding;                                      // bottom padding
        height = Math.Max(height, Padding * 2 + IconSize);      // never shorter than the icon

        var bmp = new Bitmap(Width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Transparent);

            // Card background – dark, translucent, rounded, with a colored accent stripe.
            using var bg = new SolidBrush(Color.FromArgb(235, 22, 24, 30));
            using var stripe = new SolidBrush(accent);
            FillRoundedRect(g, new Rectangle(0, 0, Width, height), 26, bg);
            FillRoundedRect(g, new Rectangle(0, 0, 8, height), 4, stripe);

            // Icon: rounded square in the accent color with the app's initial (or 📞 for calls).
            var iconRect = new Rectangle(Padding, (height - IconSize) / 2, IconSize, IconSize);
            using var iconBrush = new SolidBrush(accent);
            FillRoundedRect(g, iconRect, 22, iconBrush);
            DrawIconGlyph(g, iconRect, n, scale);

            // Text block.
            using var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var dim = new SolidBrush(Color.FromArgb(180, 200, 205, 215));
            using var accentText = new SolidBrush(accent);

            y = Padding;
            string appLine = string.IsNullOrWhiteSpace(n.AppName) ? "Phone" : n.AppName;
            g.DrawString(appLine.ToUpperInvariant(), appFont, accentText, textLeft, y);
            // Time on the right of the app line.
            string time = n.Timestamp.ToLocalTime().ToString("HH:mm");
            var timeSize = g.MeasureString(time, timeFont);
            g.DrawString(time, timeFont, dim, Width - Padding - timeSize.Width, y + 4);
            y += (int)appFont.GetHeight(g) + 6;

            if (!string.IsNullOrWhiteSpace(n.BestSender))
            {
                g.DrawString(Ellipsize(g, n.BestSender, senderFont, textWidth), senderFont, white, textLeft, y);
                y += (int)senderFont.GetHeight(g) + 4;
            }
            if (!string.IsNullOrWhiteSpace(title))
            {
                g.DrawString(Ellipsize(g, title, titleFont, textWidth), titleFont, white, textLeft, y);
                y += (int)titleFont.GetHeight(g) + 4;
            }
            foreach (var line in bodyLines)
            {
                g.DrawString(line, bodyFont, dim, textLeft, y);
                y += (int)bodyFont.GetHeight(g) + 2;
            }
        }

        var rgba = ToRgba(bmp, Math.Clamp(opacity, 0, 1));
        int w = bmp.Width, h = bmp.Height;
        bmp.Dispose();
        return new RenderedCard(w, h, rgba);
    }

    private static string TitleFor(PhoneNotification n)
    {
        if (n.Category == NotificationCategory.IncomingCall) return "📞 Incoming Call";
        if (n.Category == NotificationCategory.MissedCall) return "📵 Missed Call";
        // If the phone gave a distinct subtitle, show it as the title line.
        return string.Equals(n.Subtitle, n.BestSender, StringComparison.Ordinal) ? string.Empty : n.Subtitle;
    }

    private static Color AccentFor(PhoneNotification n)
    {
        if (n.IsCall) return Color.FromArgb(255, 52, 199, 89);     // green – calls
        return n.AppIdentifier switch
        {
            "net.whatsapp.WhatsApp" => Color.FromArgb(255, 37, 211, 102),
            "com.hammerandchisel.discord" => Color.FromArgb(255, 88, 101, 242),
            "com.facebook.Messenger" => Color.FromArgb(255, 0, 132, 255),
            "ph.telegra.Telegraph" => Color.FromArgb(255, 42, 171, 238),
            "com.apple.MobileSMS" => Color.FromArgb(255, 52, 199, 89),
            _ => Color.FromArgb(255, 94, 132, 241),                // default blue-violet
        };
    }

    private static void DrawIconGlyph(Graphics g, Rectangle rect, PhoneNotification n, float scale)
    {
        string glyph = n.IsCall ? "📞" // 📞
            : (string.IsNullOrWhiteSpace(n.AppName) ? "?" : n.AppName.Trim()[..1].ToUpperInvariant());

        using var font = new Font(n.IsCall ? "Segoe UI Emoji" : "Segoe UI Semibold", 40f * scale, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, rect, sf);
    }

    private static List<string> WrapText(Graphics g, string text, Font font, int maxWidth, int maxLines)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var words = text.Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (g.MeasureString(candidate, font).Width > maxWidth && current.Length > 0)
            {
                lines.Add(current);
                current = word;
                if (lines.Count == maxLines - 1) break;
            }
            else current = candidate;
        }
        if (current.Length > 0 && lines.Count < maxLines) lines.Add(current);

        // Ellipsize the last line if we truncated.
        if (lines.Count == maxLines)
            lines[^1] = Ellipsize(g, lines[^1] + "…", font, maxWidth);
        return lines;
    }

    private static string Ellipsize(Graphics g, string text, Font font, int maxWidth)
    {
        if (g.MeasureString(text, font).Width <= maxWidth) return text;
        while (text.Length > 1 && g.MeasureString(text + "…", font).Width > maxWidth)
            text = text[..^1];
        return text + "…";
    }

    private static void FillRoundedRect(Graphics g, Rectangle r, int radius, Brush brush)
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

    /// <summary>Convert the 32bppArgb bitmap (BGRA in memory) to RGBA, applying master opacity.</summary>
    private static byte[] ToRgba(Bitmap bmp, double opacity)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int count = bmp.Width * bmp.Height * 4;
            var src = new byte[count];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, src, 0, count);

            var dst = new byte[count];
            for (int i = 0; i < count; i += 4)
            {
                // GDI+ memory order is B,G,R,A → OpenVR wants R,G,B,A.
                dst[i + 0] = src[i + 2];                       // R
                dst[i + 1] = src[i + 1];                       // G
                dst[i + 2] = src[i + 0];                       // B
                dst[i + 3] = (byte)(src[i + 3] * opacity);     // A × opacity
            }
            return dst;
        }
        finally { bmp.UnlockBits(data); }
    }
}
