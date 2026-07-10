using PhoneNotificationsVR.Core.Settings;
using Valve.VR;

namespace PhoneNotificationsVR.Overlay;

/// <summary>
/// Builds the OpenVR 3x4 transform matrices that place the overlay for each anchor mode, and
/// resolves which tracked device (HMD / left / right controller) the overlay should ride on.
/// </summary>
internal static class OverlayPositioner
{
    /// <summary>Identity rotation with a translation. HmdMatrix34_t is row-major [R|t].</summary>
    public static HmdMatrix34_t Translation(float x, float y, float z) => new()
    {
        m0 = 1, m1 = 0, m2 = 0, m3 = x,
        m4 = 0, m5 = 1, m6 = 0, m7 = y,
        m8 = 0, m9 = 0, m10 = 1, m11 = z,
    };

    /// <summary>The tracked-device index for controller/wrist anchors, or the HMD.</summary>
    public static uint DeviceIndexFor(OverlaySettings o)
    {
        if (o.Anchor is OverlayAnchor.NearController or OverlayAnchor.NearWrist)
        {
            var role = o.Hand == TrackedHand.Left
                ? ETrackedControllerRole.LeftHand
                : ETrackedControllerRole.RightHand;
            var idx = OpenVR.System?.GetTrackedDeviceIndexForControllerRole(role) ?? OpenVR.k_unTrackedDeviceIndexInvalid;
            if (idx != OpenVR.k_unTrackedDeviceIndexInvalid) return idx;
        }
        return OpenVR.k_unTrackedDeviceIndex_Hmd; // fall back to HMD if the controller is off
    }

    /// <summary>
    /// The base transform for the anchor, before the animated slide offset is applied.
    /// <paramref name="slide"/> shifts the card along its local up axis for the slide-in animation.
    /// </summary>
    public static HmdMatrix34_t BaseTransform(OverlaySettings o, float slide)
    {
        return o.Anchor switch
        {
            OverlayAnchor.FollowHeadset => Translation(
                (float)o.OffsetRight,
                (float)o.OffsetUp + slide,
                -(float)o.FollowDistance),

            // Controller-relative: sit a little above and in front of the controller.
            OverlayAnchor.NearController => Translation(
                (float)o.OffsetRight,
                0.06f + (float)o.OffsetUp + slide,
                -0.10f),

            // Wrist: closer and angled like a watch face, tucked just above the controller.
            OverlayAnchor.NearWrist => Translation(
                0.0f,
                0.02f + slide,
                -0.04f),

            // Fixed in world: placed in front of where the player currently is (standing origin).
            OverlayAnchor.FixedInWorld => Translation(
                (float)o.OffsetRight,
                1.6f + (float)o.OffsetUp + slide,   // ~eye height
                -(float)o.FollowDistance),

            _ => Translation(0, 0, -(float)o.FollowDistance),
        };
    }

    public static bool IsAbsolute(OverlaySettings o) => o.Anchor == OverlayAnchor.FixedInWorld;
}
