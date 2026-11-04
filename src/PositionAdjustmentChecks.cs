using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    // Numeric handles below are fixtures, never native windows. These checks do
    // not call Place/SetWindowPos, activate a window, or inspect the desktop.
    internal static class PositionAdjustmentChecks
    {
        private static readonly IntPtr Target = new IntPtr(101);
        private static readonly IntPtr Badge = new IntPtr(202);
        private static readonly IntPtr Above = new IntPtr(303);

        public static void Run(Dictionary<string, bool> checks)
        {
            checks["badge_z_order_normal_target_keeps_immediately_previous_normal_window"] = Select(false, false, Above, IntPtr.Zero, null) == Above;

            var reads = new List<IntPtr>();
            IntPtr skipped = Select(false, false, Badge, Above, reads);
            checks["badge_z_order_skips_itself_when_already_above_target"] = skipped == Above
                && reads.Count == 2 && reads[0] == Target && reads[1] == Badge;

            checks["badge_z_order_normal_target_at_top_or_topmost_boundary_uses_hwnd_top"] = Select(false, false, IntPtr.Zero, IntPtr.Zero, null) == IntPtr.Zero
                && Select(false, true, Above, IntPtr.Zero, null) == IntPtr.Zero;
            checks["badge_z_order_self_skip_does_not_promote_normal_target_to_topmost"] = Select(false, false, Badge, IntPtr.Zero, null) == IntPtr.Zero
                && Select(false, true, Badge, Above, null) == IntPtr.Zero;
            checks["badge_z_order_topmost_target_without_previous_window_uses_hwnd_topmost"] = Select(true, false, IntPtr.Zero, IntPtr.Zero, null) == new IntPtr(-1)
                && Select(true, false, Badge, IntPtr.Zero, null) == new IntPtr(-1);
            checks["badge_z_order_topmost_target_retains_previous_group_after_self_skip"] = Select(true, true, Above, IntPtr.Zero, null) == Above
                && Select(true, true, Badge, Above, null) == Above;

            bool foregroundCodex = true;
            for (int mask = 0; mask < 16; mask++)
                foregroundCodex &= PositionAdjustmentPolicy.CanShow(true, (mask & 1) != 0, (mask & 2) != 0,
                    (mask & 4) != 0, (mask & 8) != 0);
            checks["position_policy_foreground_codex_preserves_normal_overlay_behavior"] = foregroundCodex;

            bool settingsTable = true;
            for (int mask = 0; mask < 8; mask++)
                settingsTable &= PositionAdjustmentPolicy.CanShow(false, true, (mask & 1) != 0,
                    (mask & 2) != 0, (mask & 4) != 0) == (mask == 7);
            checks["position_policy_settings_requires_position_page_normal_window_and_target"] = settingsTable;
            checks["position_policy_other_settings_page_does_not_show_overlay"] = !PositionAdjustmentPolicy.CanShow(false, true, false, true, true);
            checks["position_policy_minimized_settings_cannot_keep_overlay_visible"] = !PositionAdjustmentPolicy.CanShow(false, true, true, false, true);
            checks["position_policy_missing_codex_target_disables_settings_adjustment"] = !PositionAdjustmentPolicy.CanShow(false, true, true, true, false);

            bool otherApplication = true;
            for (int mask = 0; mask < 8; mask++)
                otherApplication &= !PositionAdjustmentPolicy.CanShow(false, false, (mask & 1) != 0,
                    (mask & 2) != 0, (mask & 4) != 0);
            checks["position_policy_unrelated_foreground_app_never_shows_adjustment_overlay"] = otherApplication;
        }

        private static IntPtr Select(bool targetTopmost, bool aboveTopmost, IntPtr targetPrevious,
            IntPtr badgePrevious, List<IntPtr> reads)
        {
            Func<IntPtr, IntPtr> previous = delegate(IntPtr window)
            {
                if (reads != null) reads.Add(window);
                if (window == Target) return targetPrevious;
                if (window == Badge) return badgePrevious;
                throw new InvalidOperationException("Unexpected fake-window traversal.");
            };
            Func<IntPtr, bool> topmost = delegate(IntPtr window)
            {
                return window == Target ? targetTopmost : window == Above && aboveTopmost;
            };
            return BadgeZOrder.InsertAfter(Target, Badge, previous, topmost);
        }
    }
}
