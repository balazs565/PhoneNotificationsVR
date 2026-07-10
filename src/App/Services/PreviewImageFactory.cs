using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhoneNotificationsVR.Core.Abstractions;
using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.App.Services;

/// <summary>Turns the renderer's RGBA card into a WPF <see cref="ImageSource"/> for the live preview.</summary>
public sealed class PreviewImageFactory
{
    private readonly INotificationRenderer _renderer;
    public PreviewImageFactory(INotificationRenderer renderer) => _renderer = renderer;

    public ImageSource Create(PhoneNotification notification)
    {
        var card = _renderer.Render(notification);

        // Renderer output is RGBA; WPF's Bgra32 expects B,G,R,A, so swap R and B into a new buffer.
        var bgra = new byte[card.Rgba.Length];
        for (int i = 0; i < card.Rgba.Length; i += 4)
        {
            bgra[i + 0] = card.Rgba[i + 2]; // B
            bgra[i + 1] = card.Rgba[i + 1]; // G
            bgra[i + 2] = card.Rgba[i + 0]; // R
            bgra[i + 3] = card.Rgba[i + 3]; // A
        }

        var bmp = new WriteableBitmap(card.Width, card.Height, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, card.Width, card.Height), bgra, card.Width * 4, 0);
        bmp.Freeze(); // make it cross-thread safe
        return bmp;
    }
}
