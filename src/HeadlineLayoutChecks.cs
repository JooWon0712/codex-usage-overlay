using System;
using System.Collections.Generic;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class HeadlineLayoutChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            bool ordered = true, rightAligned = true, longTextFits = true, sameColumns = true;
            using (var bitmap = new Bitmap(700, 100))
            using (var graphics = Graphics.FromImage(bitmap))
            foreach (float scale in new[] { 0.8f, 1f, 1.25f, 1.5f, 2f })
            {
                var bounds = new Rectangle(17, 23, (int)Math.Round(288 * scale), (int)Math.Round(27 * scale));
                var current = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "현재 세션", "100%",
                    "11월 19일 01:09", true, "(3h 16m)");
                var weekly = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "주간 세션", "99.9%",
                    "11월 18일 09:10", true, "(D-DAY · 0h 10m)");
                var hiddenDate = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "주간 세션", "99.9%",
                    null, false, "(D-7)");
                var hiddenCountdown = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "주간 세션", "99.9%",
                    "11월 25일 17:58", true, null);
                var missingDate = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "주간 세션", "100%",
                    "초기화 시각 확인 불가", false, null);
                foreach (var row in new[] { current, weekly, hiddenDate, hiddenCountdown, missingDate })
                {
                    sameColumns &= row.Period.Left == bounds.Left && row.Value.Left == current.Value.Left;
                    ordered &= FieldsDoNotOverlap(bounds, row);
                    Rectangle last = row.Countdown.IsEmpty ? row.Reset : row.Countdown;
                    rightAligned &= last.Right == bounds.Right;
                }
                rightAligned &= hiddenDate.Reset.IsEmpty && hiddenCountdown.Countdown.IsEmpty && missingDate.Countdown.IsEmpty;
                longTextFits &= FitsReset(graphics, current, scale, "11월 19일 01:09", "(3h 16m)")
                    && FitsReset(graphics, weekly, scale, "11월 18일 09:10", "(D-DAY · 0h 10m)");
            }
            checks["headline_detailed_period_and_quota_columns_align_for_full_and_decimal_values"] = sameColumns;
            checks["headline_detailed_fields_fit_without_overlap_at_full_and_minimum_scale"] = ordered;
            checks["headline_detailed_reset_and_countdown_end_at_the_same_right_edge"] = rightAligned;
            checks["headline_detailed_cross_midnight_and_same_day_countdown_strings_fit"] = longTextFits;
            CheckCodexOptions(checks);

            var known = SettingsPreviewHost.CreateSample(95, "ok");
            var older = SettingsPreviewHost.CreateSample(14, "stale");
            var missing = SettingsPreviewHost.CreateSample(95, "ok", false);
            checks["headline_codex_preserves_korean_reset_time_and_previous_schedule_labels"] =
                BadgeRenderer.DetailedCodexResetText(known) == "11월 10일 12:00"
                && BadgeRenderer.DetailedCodexCountdownText(known, true) == "(D-6)"
                && BadgeRenderer.DetailedCodexResetText(older) == "이전 11월 10일 12:00"
                && BadgeRenderer.DetailedCodexCountdownText(older, true) == "(D-6)"
                && BadgeRenderer.DetailedCodexCountdownText(older, false) == "(이전 D-6)"
                && BadgeRenderer.DetailedCodexCountdownText(missing, true) == null;
        }

        private static bool FieldsDoNotOverlap(Rectangle bounds, BadgeRenderer.SessionRowPlacement row)
        {
            return bounds.Contains(row.Period) && bounds.Contains(row.Value) && row.Period.Right <= row.Value.Left
                && (row.Reset.IsEmpty || (bounds.Contains(row.Reset) && row.Value.Right <= row.Reset.Left))
                && (row.Countdown.IsEmpty || (bounds.Contains(row.Countdown) && row.Value.Right <= row.Countdown.Left))
                && !row.Reset.IntersectsWith(row.Countdown)
                && (row.Reset.IsEmpty || row.Countdown.IsEmpty || row.Reset.Right <= row.Countdown.Left
                    || row.Reset.Bottom <= row.Countdown.Top);
        }

        private static bool FitsReset(Graphics graphics, BadgeRenderer.SessionRowPlacement row, float scale, string time, string timer)
        {
            using (var regular = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                return row.Reset.Width >= BadgeRenderer.ResetIconInset(scale) + BadgeRenderer.MeasureTextWidth(graphics, time, regular)
                    && row.Countdown.Width >= BadgeRenderer.MeasureTextWidth(graphics, timer, bold);
        }

        private static void CheckCodexOptions(Dictionary<string, bool> checks)
        {
            bool fixedHeight = true, quotaVisible = true, independentOptions = true, loadingVisible = true;
            var live = SettingsPreviewHost.CreateSample(95, "ok");
            var loading = SettingsPreviewHost.CreateSample(0, "loading");
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            foreach (bool showClaude in new[] { false, true })
            for (int mask = 0; mask < 4; mask++)
            {
                var layout = new BadgeLayoutSettings { ShowClaude = showClaude };
                bool showDate = (mask & 1) != 0;
                bool showCountdown = (mask & 2) != 0;
                layout.Elements.Find(item => item.Key == "weekly-reset").Visible = showDate;
                layout.Elements.Find(item => item.Key == "countdown").Visible = showCountdown;
                layout.Normalize();
                fixedHeight &= layout.Height == (showClaude ? 209 : 78);
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                var quota = BadgeRenderer.ElementBounds(layout.Elements.Find(item => item.Key == "remaining"));
                var bounds = Rectangle.Round(new RectangleF(quota.X * scale, quota.Y * scale, quota.Width * scale, quota.Height * scale));
                using (var glyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, live, layout))
                using (var noData = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, loading, layout))
                using (var graphics = Graphics.FromImage(glyphs))
                {
                    string reset = showDate ? BadgeRenderer.DetailedCodexResetText(live) : null;
                    string countdown = showCountdown ? BadgeRenderer.DetailedCodexCountdownText(live, showDate) : null;
                    var row = BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, "주간 세션", "95%", reset, showDate, countdown);
                    quotaVisible &= HasInk(glyphs, row.Period) && HasInk(glyphs, row.Value);
                    var right = Rectangle.FromLTRB(row.Value.Right, bounds.Top, bounds.Right, bounds.Bottom);
                    independentOptions &= HasInk(glyphs, right) == (showDate || showCountdown)
                        && (showDate ? HasInk(glyphs, row.Reset) : row.Reset.IsEmpty)
                        && (showCountdown ? HasInk(glyphs, row.Countdown) : row.Countdown.IsEmpty);
                    loadingVisible &= HasInk(noData, Rectangle.FromLTRB(bounds.Left, bounds.Top, row.Value.Right, bounds.Bottom));
                }
            }
            checks["headline_detailed_date_and_countdown_are_independent_at_fixed_height"] =
                fixedHeight && quotaVisible && independentOptions;
            checks["headline_detailed_keeps_loading_text_visible_with_any_reset_preference"] = loadingVisible;
        }

        private static bool HasInk(Bitmap bitmap, Rectangle bounds)
        {
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (bitmap.GetPixel(x, y).A != 0) return true;
            return false;
        }
    }
}
