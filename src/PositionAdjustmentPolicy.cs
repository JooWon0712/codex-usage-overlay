using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class PositionAdjustmentPolicy
    {
        // Both settings pages may keep the actual target badge visible. The
        // foreground and target guards are independent of the selected page.
        internal static bool CanShow(bool codexForeground, bool settingsForeground,
            bool settingsNormal, bool targetAvailable)
        {
            return codexForeground || (settingsForeground && settingsNormal && targetAvailable);
        }
    }

    internal static class BadgeZOrder
    {
        private static bool IsTopMost(IntPtr window)
        {
            return (Native.GetWindowLongPtr(window, -20).ToInt64() & 0x8) != 0;
        }

        internal static IntPtr InsertAfter(IntPtr target, IntPtr badge, Func<IntPtr, IntPtr> previous, Func<IntPtr, bool> isTopMost)
        {
            IntPtr above = previous(target);
            if (above == badge) above = previous(badge);
            if (isTopMost(target)) return above == IntPtr.Zero ? new IntPtr(-1) : above;
            // Inserting after a topmost window would also make our badge topmost.
            // HWND_TOP keeps it at the top of the ordinary group in that case.
            return above == IntPtr.Zero || isTopMost(above) ? IntPtr.Zero : above;
        }

        internal static bool Place(IntPtr badge, IntPtr target, Rectangle bounds)
        {
            if (!Native.IsWindow(badge) || !Native.IsWindow(target)) return false;
            // Remove the normal-mode topmost state before finding the target's
            // current predecessor. Never activate or reorder the target itself.
            if (!Native.SetWindowPos(badge, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10)) return false;
            IntPtr above = InsertAfter(target, badge, window => Native.GetWindow(window, 3), IsTopMost);
            return Native.SetWindowPos(badge, above, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
        }
    }
}
