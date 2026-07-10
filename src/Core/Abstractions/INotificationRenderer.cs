using PhoneNotificationsVR.Core.Models;

namespace PhoneNotificationsVR.Core.Abstractions;

/// <summary>A pre-rendered notification card as a raw RGBA32 image ready to upload to an OpenVR texture.</summary>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="Rgba">Row-major RGBA (4 bytes/pixel). OpenVR's SetOverlayRaw expects this byte order.</param>
public readonly record struct RenderedCard(int Width, int Height, byte[] Rgba);

/// <summary>
/// Renders a <see cref="PhoneNotification"/> into a bitmap card. Kept separate from the overlay so
/// the exact same renderer feeds both the VR texture and the desktop "Overlay preview".
/// </summary>
public interface INotificationRenderer
{
    /// <summary>Render a card at the given opacity (0..1) for animation frames.</summary>
    RenderedCard Render(PhoneNotification notification, double opacity = 1.0);
}
