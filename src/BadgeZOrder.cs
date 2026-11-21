using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class BadgeZOrder
    {
        private static bool IsTopMost(IntPtr window)
        {
            return (Native.GetWindowLongPtr(window, -20).ToInt64() & 0x8) != 0;
        }

        internal static bool IsImmediatelyAbove(IntPtr window, IntPtr target)
        {
            return Native.GetWindow(target, 3) == window && IsTopMost(window) == IsTopMost(target);
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
            // Follow the target's z-order band without activating or moving the target itself.
            if (IsTopMost(badge) && !IsTopMost(target)
                && !Native.SetWindowPos(badge, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10)) return false;
            IntPtr above = InsertAfter(target, badge, window => Native.GetWindow(window, 3), IsTopMost);
            return Native.SetWindowPos(badge, above, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
        }
    }
}
