using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeZOrderChecks
    {
        internal static object Observation;
        public static void Run(Dictionary<string, bool> checks)
        {
            // Our own transparent, off-screen fixture handles only. No normal
            // OverlayContext, account, settings file, or desktop input is used.
            using (var target = Fixture())
            using (var cover = Fixture())
            using (var badge = Fixture())
            {
                IntPtr before = Native.GetForegroundWindow();
                target.Show(); cover.Show(); badge.Show();
                IntPtr t = target.Handle, c = cover.Handle, b = badge.Handle;
                const uint flags = 0x1 | 0x2 | 0x10;
                bool prepared = Native.SetWindowPos(t, IntPtr.Zero, 0, 0, 0, 0, flags)
                    && Native.SetWindowPos(c, IntPtr.Zero, 0, 0, 0, 0, flags)
                    && Native.SetWindowPos(b, new IntPtr(-1), 0, 0, 0, 0, flags);
                var bounds = new Rectangle(-32000, -32000, 320, 82);
                bool placed = prepared && BadgeZOrder.Place(b, t, bounds);
                Observation = new { target=t.ToInt64(), cover=c.ToInt64(), badge=b.ToInt64(), prepared=prepared, placed=placed,
                    aboveTarget=Native.GetWindow(t,3).ToInt64(), aboveBadge=Native.GetWindow(b,3).ToInt64(),
                    aboveCover=Native.GetWindow(c,3).ToInt64(), badgeTopMost=TopMost(b), order=Order(t,c,b) };
                checks["native_adjustment_badge_is_above_target_and_below_cover"] = placed && Ordered(t, c, b);
                checks["native_adjustment_demotes_prior_topmost_state"] = placed && !TopMost(b);
                bool repeated = BadgeZOrder.Place(b, t, bounds);
                checks["native_adjustment_same_bounds_keep_order_without_activating_windows"] = repeated
                    && Ordered(t, c, b) && Native.GetForegroundWindow() == before;
                bool restored = Native.SetWindowPos(b, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
                checks["native_adjustment_can_return_to_live_topmost_and_back"] = restored && TopMost(b)
                    && BadgeZOrder.Place(b, t, bounds) && !TopMost(b) && Ordered(t, c, b);
                checks["native_adjustment_invalid_target_does_not_place_badge"] = !BadgeZOrder.Place(b, IntPtr.Zero, bounds);
                checks["native_adjustment_fixture_never_takes_foreground"] = Native.GetForegroundWindow() == before;
            }
        }

        private sealed class FixtureWindow : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        private static Form Fixture()
        {
            return new FixtureWindow
            {
                ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000),
                ClientSize = new Size(320, 82), Opacity = 0
            };
        }

        private static bool TopMost(IntPtr window)
        {
            return (Native.GetWindowLongPtr(window, -20).ToInt64() & 0x8) != 0;
        }

        private static long[] Order(IntPtr target, IntPtr cover, IntPtr badge)
        {
            var result = new List<long>();
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (window == target || window == cover || window == badge) result.Add(window.ToInt64());
                return true;
            }, IntPtr.Zero);
            return result.ToArray();
        }

        private static bool Ordered(IntPtr target, IntPtr cover, IntPtr badge)
        {
            // Windows may interleave other helper windows. Verify the actual
            // occlusion relation among our three fixtures, not adjacency.
            long[] order = Order(target, cover, badge);
            return order.Length == 3 && order[0] == cover.ToInt64() && order[1] == badge.ToInt64() && order[2] == target.ToInt64();
        }
    }
}
