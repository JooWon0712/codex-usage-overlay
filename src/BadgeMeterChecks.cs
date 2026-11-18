using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeMeterChecks
    {
        internal static void Run(Dictionary<string, bool> checks)
        {
            var now = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = now.ToString("o"),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, resetsAt = now.AddMinutes(20).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, resetsAt = now.AddDays(4).ToUnixTimeSeconds() }
            };
            var claude = ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc);
            var state = new UsageState { status = "ok", observedAt = now.ToString("o") };
            var weekly = new LimitWindow { limitId = "codex", windowDurationMins = QuotaPeriods.WeeklyMinutes, remainingPercent = 50 };
            state.windows.Add(weekly);
            var normal = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, claude);
            snapshot.fiveHour = new ClaudeUsageWindow { usedPercentage = 0 };
            var missingTime = UsagePresentation.Create(state, now, TimeZoneInfo.Utc,
                ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc));
            var shortlyBeforeMidnight = now.AddHours(14).AddMinutes(50);
            snapshot.fiveHour.resetsAt = shortlyBeforeMidnight.AddMinutes(20).ToUnixTimeSeconds();
            var midnightReset = UsagePresentation.Create(state, now, TimeZoneInfo.Utc,
                ClaudeUsageDisplay.FromSnapshot(snapshot, shortlyBeforeMidnight, TimeZoneInfo.Utc));
            weekly.remainingPercent = 12;
            var low = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, claude);
            weekly.remainingPercent = 0;
            var empty = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, ClaudeUsageDisplay.Awaiting);
            var previous = UsagePresentation.Create(state, now.AddMinutes(4), TimeZoneInfo.Utc);
            state.status = "stale";
            state.reason = "request_timeout";
            var failed = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, ClaudeUsageDisplay.Awaiting);
            state.status = "ok";
            state.reason = null;
            state.windows.Add(new LimitWindow { limitId = "codex", windowDurationMins = QuotaPeriods.WeeklyMinutes, remainingPercent = 84 });
            var conflicting = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
            checks["badge_meter_uses_known_weekly_remaining_and_distinguishes_zero_from_missing"] = normal.CodexBarRemaining == 50
                && low.CodexBarRemaining == 12 && low.Tone == UsageTone.Low
                && empty.CodexBarRemaining == 0 && failed.CodexBarRemaining == null && conflicting.CodexBarRemaining == null
                && failed.HasQueryFailure && claude.FiveHourRemaining == 76 && claude.WeeklyRemaining == 59;
            checks["badge_compact_identifies_low_exhausted_previous_and_failed_codex_observations"] =
                BadgeRenderer.CompactCodexAmount(normal) == "50%"
                && BadgeRenderer.CompactCodexAmount(low) == "12% · 부족"
                && BadgeRenderer.CompactCodexAmount(empty) == "0% · 소진"
                && BadgeRenderer.CompactCodexAmount(previous) == "0% · 이전"
                && BadgeRenderer.CompactCodexAmount(failed) == "조회 실패";

            bool serviceColors = true, warningColor = true, trackLength = true, transparent = true;
            bool missing = true, uniformSurface = true, fixedCodexMeter = true;
            bool highContrast = SystemInformation.HighContrast;
            foreach (bool dark in new[] { false, true })
            foreach (int opacity in new[] { 0, 50, 100 })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                var layout = new BadgeLayoutSettings { ShowClaude = true, BackgroundTransparency = opacity };
                layout.Normalize();
                VisualTheme theme = VisualTheme.For(dark, highContrast);
                bool brightMeters = dark || opacity >= 50;
                Color blue = highContrast ? theme.Text : brightMeters ? Color.FromArgb(0x9b, 0xc4, 0xed) : Color.FromArgb(0x30, 0x59, 0x78);
                Color purple = highContrast ? theme.Text : brightMeters ? Color.FromArgb(0xc3, 0xab, 0xdd) : Color.FromArgb(0x66, 0x52, 0x7e);
                Color rose = highContrast ? theme.Text : brightMeters ? Color.FromArgb(0xff, 0x9c, 0xb2) : Color.FromArgb(0x92, 0x34, 0x49);
                Color track = highContrast ? theme.Surface : brightMeters ? Color.FromArgb(0x3a, 0x46, 0x54) : Color.FromArgb(0xbf, 0xc7, 0xd1);
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                int codexY = layout.CodexMeterY + layout.MeterHeight / 2;
                int claudeY = layout.CodexHeight + BadgeLayoutSettings.DetailedClaudeMeterY + layout.MeterHeight / 2;
                int claudeWeeklyY = claudeY + BadgeLayoutSettings.DetailedClaudePeriodStride;
                int left = layout.ContentPadding + 30;
                int nearLeft = layout.ContentPadding + 5;
                using (var actual = BadgeRenderer.CreateSurfaceBitmap(size, scale, theme, normal, layout))
                using (var warning = BadgeRenderer.CreateSurfaceBitmap(size, scale, theme, low, layout))
                using (var zero = BadgeRenderer.CreateSurfaceBitmap(size, scale, theme, empty, layout))
                using (var unavailable = BadgeRenderer.CreateSurfaceBitmap(size, scale, theme, failed, layout))
                {
                    serviceColors &= At(actual, left, codexY, scale) == blue.ToArgb()
                        && At(actual, left, claudeY, scale) == purple.ToArgb() && At(actual, left, claudeWeeklyY, scale) == purple.ToArgb();
                    warningColor &= At(warning, nearLeft, codexY, scale) == rose.ToArgb() && At(warning, left, claudeWeeklyY, scale) == purple.ToArgb();
                    trackLength &= At(actual, 3 * layout.Width / 4, codexY, scale) == track.ToArgb()
                        && At(warning, layout.Width / 4, codexY, scale) == track.ToArgb() && At(zero, nearLeft, codexY, scale) == track.ToArgb()
                        && At(actual, 2 * layout.Width / 3, claudeY, scale) == purple.ToArgb()
                        && At(actual, 2 * layout.Width / 3, claudeWeeklyY, scale) == track.ToArgb();
                    int unavailableBody = At(unavailable, left, 33, scale);
                    missing &= At(unavailable, left, codexY, scale) == unavailableBody
                        && At(unavailable, left, claudeY, scale) == unavailableBody
                        && At(unavailable, left, claudeWeeklyY, scale) == unavailableBody;
                    int bodyColor = At(actual, left, 33, scale);
                    uniformSurface &= At(actual, left, 10, scale) == bodyColor
                        && At(warning, left, 10, scale) == bodyColor
                        && At(actual, left, layout.CodexHeight + 23, scale) == bodyColor;
                    var hiddenReset = layout.Copy();
                    hiddenReset.Elements.Find(element => element.Key == "weekly-reset").Visible = false;
                    hiddenReset.Normalize();
                    var shorterSize = new Size(size.Width, (int)Math.Round(hiddenReset.Height * scale));
                    using (var shorter = BadgeRenderer.CreateSurfaceBitmap(shorterSize, scale, theme, normal, hiddenReset))
                        fixedCodexMeter &= At(shorter, left, codexY, scale) == blue.ToArgb()
                            && At(actual, left, layout.CodexMeterY - 3, scale) == bodyColor
                            && At(actual, left, layout.CodexMeterY + layout.MeterHeight + 2, scale) == bodyColor;
                    if (opacity == 100 && !highContrast)
                        transparent &= actual.GetPixel((int)(left * scale), (int)(10 * scale)).A == 0
                            && actual.GetPixel((int)(left * scale), (int)(codexY * scale)).A == 255;
                }
            }
            checks["badge_meter_codex_blue_claude_purple_in_both_themes"] = serviceColors;
            checks["badge_meter_only_codex_becomes_rose_when_weekly_limit_is_low"] = warningColor;
            checks["badge_meter_fill_reflects_remaining_and_zero_has_only_a_track"] = trackLength;
            checks["badge_meter_missing_data_omits_track_but_transparent_background_keeps_valid_marks"] = missing && transparent;
            checks["badge_detailed_service_headings_share_one_surface_even_for_low_usage"] = uniformSurface;
            checks["badge_detailed_codex_meter_stays_below_the_inline_reading_when_reset_is_hidden"] = fixedCodexMeter;
            CheckCodexDetail(checks, UsagePresentation.CreatePreview(now, TimeZoneInfo.Utc),
                SettingsPreviewHost.CreateSample(100, "ok", false));
            CheckClaudeDetail(checks, normal, missingTime, midnightReset, failed);
            CheckSameDayResetRows(checks, now);
            CheckCompactResetIcons(checks, UsagePresentation.CreatePreview(now.AddHours(14), TimeZoneInfo.Utc));
            CheckCompactPercentAlignment(checks, now);
            CheckServiceIcons(checks, normal);
        }

        private static void CheckCompactPercentAlignment(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = now.ToString("o"),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 12 },
                weekly = new ClaudeUsageWindow { usedPercentage = 12 }
            };
            var state = new UsageState { status = "ok", observedAt = now.ToString("o") };
            state.windows.Add(new LimitWindow { limitId = "codex", windowDurationMins = QuotaPeriods.WeeklyMinutes, remainingPercent = 88 });
            var usage = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc));
            var layout = new BadgeLayoutSettings { Compact = true, ShowClaude = true };
            layout.Normalize();
            bool aligned = true;
            using (var icons = new ServiceIcons(IconSample(Color.DeepSkyBlue), IconSample(Color.DarkOrange)))
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                int codexTop = (int)Math.Round(layout.HeaderHeight * scale);
                int codexBottom = (int)Math.Round(layout.CodexHeight * scale);
                int claudeBottom = (int)Math.Round((layout.Height - BadgeLayoutSettings.CompactClaudeBottomPadding) * scale);
                int split = codexBottom + (claudeBottom - codexBottom) / 2;
                int periodLeft = (int)Math.Round(layout.ContentPadding * scale) + (int)Math.Round(96 * scale);
                using (var pixels = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, usage, layout, icons))
                using (var graphics = Graphics.FromImage(pixels))
                {
                    int percentLeft = periodLeft + BadgeRenderer.CompactPeriodWidth(graphics, scale);
                    int left = percentLeft - (int)Math.Round(3 * scale);
                    int right = percentLeft + (int)Math.Round(9 * scale);
                    int codexInk = FirstInkColumn(pixels, Rectangle.FromLTRB(left, codexTop, right, codexBottom));
                    int fiveHourInk = FirstInkColumn(pixels, Rectangle.FromLTRB(left, codexBottom, right, split));
                    int weeklyInk = FirstInkColumn(pixels, Rectangle.FromLTRB(left, split, right, claudeBottom));
                    aligned &= codexInk >= percentLeft && Math.Abs(codexInk - percentLeft) <= Math.Ceiling(2 * scale)
                        && codexInk == fiveHourInk && fiveHourInk == weeklyInk;
                }
            }
            checks["badge_compact_aligns_codex_and_both_claude_percentages_in_one_column"] = aligned;
        }

        private static void CheckCompactResetIcons(Dictionary<string, bool> checks, UsagePresentation usage)
        {
            bool iconsShown = true, separateHeader = true, clearClaudeBottom = true, widestValuesFit = true;
            using (var icons = new ServiceIcons(IconSample(Color.DeepSkyBlue), IconSample(Color.DarkOrange)))
            foreach (bool showClaude in new[] { false, true })
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                var layout = new BadgeLayoutSettings { Compact = true, ShowClaude = showClaude };
                layout.Normalize();
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                var badge = new Rectangle(Point.Empty, size);
                int codexTop = (int)Math.Round(layout.HeaderHeight * scale);
                int codexBottom = (int)Math.Round(layout.CodexHeight * scale);
                var chevron = BadgeFormatButton.CalculateBounds(badge, layout, scale);
                int right = size.Width - (int)Math.Round(BadgeLayoutSettings.DetailedPadding * scale);
                using (var glyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, usage, layout, icons))
                using (var graphics = Graphics.FromImage(glyphs))
                using (var font = new Font("Segoe UI", BadgeLayoutSettings.ResetFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var quotaFont = new Font("Segoe UI", 12 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    int serviceWidth = (int)Math.Round((showClaude ? 96 : 58) * scale);
                    int periodLeft = size.Width - right + serviceWidth;
                    int periodWidth = BadgeRenderer.CompactPeriodWidth(graphics, scale);
                    int quotaRight = periodLeft + periodWidth + BadgeRenderer.MeasureTextWidth(graphics, "99.9%", quotaFont);
                    int resetLeft = right - (int)Math.Ceiling(scale) - BadgeRenderer.ResetIconInset(scale, compact: true)
                        - BadgeRenderer.MeasureTextWidth(graphics, "11월 25일 17:58", font);
                    widestValuesFit &= quotaRight + (int)Math.Round(6 * scale) <= resetLeft
                        && BadgeRenderer.MeasureTextWidth(graphics, "현재 세션", quotaFont) < periodWidth
                        && BadgeRenderer.MeasureTextWidth(graphics, "주간 세션", quotaFont) < periodWidth;
                    iconsShown &= CompactResetIconAndTime(glyphs, graphics, font, scale, codexTop, codexBottom, right, usage.CompactResetText);
                    if (showClaude)
                    {
                        int claudeBottom = (int)Math.Round((layout.Height - BadgeLayoutSettings.CompactClaudeBottomPadding) * scale);
                        int split = codexBottom + (claudeBottom - codexBottom) / 2;
                        iconsShown &= CompactResetIconAndTime(glyphs, graphics, font, scale, codexBottom, split, right, usage.Claude.FiveHourCompactReset)
                            && CompactResetIconAndTime(glyphs, graphics, font, scale, split, claudeBottom, right, usage.Claude.WeeklyCompactReset);
                        clearClaudeBottom &= size.Height > claudeBottom
                            && !HasGlyph(glyphs, Rectangle.FromLTRB(0, claudeBottom, size.Width, size.Height));
                    }
                    separateHeader &= chevron.Bottom == codexTop && HasGlyph(glyphs, chevron)
                        && !HasGlyph(glyphs, Rectangle.FromLTRB(0, 0, chevron.Left, codexTop))
                        && !HasGlyph(glyphs, Rectangle.FromLTRB(chevron.Right, 0, size.Width, codexTop))
                        && HasGlyph(glyphs, Rectangle.FromLTRB(chevron.Left, codexTop, right, codexBottom));
                }
            }
            checks["badge_compact_replaces_reset_labels_with_icons_and_renders_korean_dates"] = iconsShown;
            checks["badge_compact_full_session_names_leave_room_for_decimal_percentages_and_dates_with_icons"] = widestValuesFit;
            checks["badge_compact_chevron_uses_its_own_top_row_and_leaves_room_for_the_reset_time"] = separateHeader;
            checks["badge_compact_leaves_the_extra_padding_below_the_claude_rows_clear"] = clearClaudeBottom;
        }

        private static bool CompactResetIconAndTime(Bitmap glyphs, Graphics graphics, Font font, float scale,
            int top, int bottom, int right, string reset)
        {
            if (reset == null || !reset.Contains("월") || !reset.Contains("일")) return false;
            int iconInset = BadgeRenderer.ResetIconInset(scale, compact: true);
            int timeLeft = right - (int)Math.Ceiling(scale) - BadgeRenderer.MeasureTextWidth(graphics, reset, font);
            var icon = Rectangle.FromLTRB(timeLeft - iconInset, top, timeLeft - (int)Math.Round(4 * scale), bottom);
            var time = Rectangle.FromLTRB(timeLeft, top, right, bottom);
            Rectangle iconInk = GlyphRows(glyphs, icon);
            return !iconInk.IsEmpty && Math.Abs(iconInk.Top + iconInk.Bottom - top - bottom) <= Math.Ceiling(4 * scale)
                && HasGlyph(glyphs, time);
        }

        private static void CheckCodexDetail(Dictionary<string, bool> checks, UsagePresentation known, UsagePresentation missingTime)
        {
            bool periodAndPercent = true, resetInline = true, missingTimeLabeled = true;
            foreach (bool showClaude in new[] { false, true })
            foreach (float scale in new[] { 0.8f, 1f, 1.25f, 1.5f, 2f })
            {
                var layout = new BadgeLayoutSettings { ShowClaude = showClaude };
                layout.Normalize();
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                var bounds = DetailBounds(layout, "remaining", scale);
                bool highContrast = SystemInformation.HighContrast;
                VisualTheme theme = VisualTheme.For(true, highContrast);
                BadgeTextColors colors = BadgeTextColors.Resolve(theme, layout.BackgroundTransparency, highContrast);
                using (var glyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, known, layout))
                using (var missingGlyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, missingTime, layout))
                using (var graphics = Graphics.FromImage(glyphs))
                {
                    var row = BadgeRenderer.CalculateCodexSessionRow(graphics, bounds, scale, known, layout);
                    var missing = BadgeRenderer.CalculateCodexSessionRow(graphics, bounds, scale, missingTime, layout);
                    periodAndPercent &= HasGlyph(glyphs, row.Period, colors.Muted)
                        && HasDetailedPercentage(glyphs, graphics, row.Value, known.CodexRemaining.Value, scale);
                    resetInline &= row.Reset.Top == row.Countdown.Top && HasResetGroup(glyphs, bounds, row, scale, colors);
                    missingTimeLabeled &= HasDetailedPercentage(missingGlyphs, graphics, missing.Value,
                        missingTime.CodexRemaining.Value, scale)
                        && HasGlyph(missingGlyphs, missing.Reset, colors.Muted) && missing.Countdown.IsEmpty;
                }
            }
            checks["badge_detailed_codex_places_the_period_before_the_percentage_at_all_dpis"] = periodAndPercent;
            checks["badge_detailed_codex_renders_a_right_aligned_reset_group_on_the_quota_row"] = resetInline;
            checks["badge_detailed_codex_keeps_a_full_quota_when_only_reset_time_is_missing"] = missingTimeLabeled
                && missingTime.CodexRemaining == 100 && missingTime.CodexResetTimeText == null;
        }

        private static void CheckClaudeDetail(Dictionary<string, bool> checks, UsagePresentation known,
            UsagePresentation missingTime, UsagePresentation midnightReset, UsagePresentation unavailable)
        {
            var layout = new BadgeLayoutSettings { ShowClaude = true };
            layout.Normalize();
            bool percentagesAligned = true, countdownsInline = true, unavailableStaysVisible = true;
            bool missingTimeIsLabeled = true, midnightResetInline = true, labelsInline = true;
            foreach (bool dark in new[] { false, true })
            foreach (float scale in new[] { 0.8f, 1f, 1.25f, 1.5f, 2f })
            {
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                bool highContrast = SystemInformation.HighContrast;
                VisualTheme theme = VisualTheme.For(dark, highContrast);
                BadgeTextColors colors = BadgeTextColors.Resolve(theme, layout.BackgroundTransparency, highContrast);
                using (var availableGlyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, known, layout))
                using (var missingTimeGlyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, missingTime, layout))
                using (var midnightGlyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, midnightReset, layout))
                using (var unavailableGlyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, unavailable, layout))
                using (var graphics = Graphics.FromImage(availableGlyphs))
                {
                    var codex = BadgeRenderer.CalculateCodexSessionRow(graphics, DetailBounds(layout, "remaining", scale), scale, known, layout);
                    foreach (bool fiveHour in new[] { true, false })
                    {
                        var bounds = DetailBounds(layout, fiveHour ? "claude-five-hour" : "claude-weekly", scale);
                        var regular = ClaudeSessionRow(graphics, bounds, scale, known.Claude, fiveHour);
                        var missing = ClaudeSessionRow(graphics, bounds, scale, missingTime.Claude, fiveHour);
                        double quota = (fiveHour ? known.Claude.FiveHourRemaining : known.Claude.WeeklyRemaining).Value;
                        double missingQuota = (fiveHour ? missingTime.Claude.FiveHourRemaining : missingTime.Claude.WeeklyRemaining).Value;
                        percentagesAligned &= regular.Value.Left == codex.Value.Left && regular.Value.Left == missing.Value.Left
                            && HasDetailedPercentage(availableGlyphs, graphics, regular.Value, quota, scale)
                            && HasDetailedPercentage(missingTimeGlyphs, graphics, missing.Value, missingQuota, scale);
                        labelsInline &= HasGlyph(availableGlyphs, regular.Period, colors.Muted)
                            && regular.Period.Top == regular.Value.Top;
                        countdownsInline &= regular.Reset.Top == regular.Countdown.Top
                            && HasResetGroup(availableGlyphs, bounds, regular, scale, colors);
                        var rightEdge = Rectangle.FromLTRB(bounds.Right - (int)Math.Round(28 * scale), bounds.Top, bounds.Right, bounds.Bottom);
                        unavailableStaysVisible &= HasGlyph(unavailableGlyphs, bounds) && !HasGlyph(unavailableGlyphs, rightEdge);
                        if (fiveHour)
                        {
                            missingTimeIsLabeled &= HasGlyph(missingTimeGlyphs, missing.Reset, colors.Muted) && missing.Countdown.IsEmpty;
                            var midnight = ClaudeSessionRow(graphics, bounds, scale, midnightReset.Claude, true);
                            midnightResetInline &= midnight.Reset.Top == midnight.Countdown.Top
                                && HasResetGroup(midnightGlyphs, bounds, midnight, scale, colors);
                        }
                    }
                }
            }
            checks["badge_detailed_aligns_codex_and_claude_percentages_in_one_column"] = percentagesAligned;
            checks["badge_detailed_claude_keeps_both_session_labels_inline"] = labelsInline;
            checks["badge_detailed_claude_keeps_reset_groups_right_aligned_across_dpi"] = countdownsInline;
            checks["badge_detailed_claude_keeps_a_cross_midnight_reset_and_timer_inline"] = midnightResetInline;
            checks["badge_detailed_claude_preserves_unavailable_quotas_without_countdowns"] = unavailableStaysVisible;
            checks["badge_detailed_claude_renders_a_missing_reset_status_with_full_five_hour_quota"] = missingTimeIsLabeled
                && missingTime.Claude.FiveHourRemaining == 100 && missingTime.Claude.FiveHourReset == null
                && missingTime.Claude.FiveHourCountdownText == null && missingTime.Claude.WeeklyCountdownText == "D-4";
        }

        private static bool HasDetailedPercentage(Bitmap glyphs, Graphics graphics, Rectangle quota, double remaining, float scale)
        {
            using (var font = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                string digits = remaining.ToString("0.#", CultureInfo.InvariantCulture);
                int digitWidth = BadgeRenderer.MeasureTextWidth(graphics, digits, font);
                int numberWidth = BadgeRenderer.MeasureTextWidth(graphics, digits + "%", font);
                var leadingDigits = new Rectangle(quota.X, quota.Y, digitWidth, quota.Height);
                var sign = new Rectangle(leadingDigits.Right, quota.Y, numberWidth - digitWidth, quota.Height);
                int digitHeight = GlyphRows(glyphs, leadingDigits).Height;
                int signHeight = GlyphRows(glyphs, sign).Height;
                return sign.Right <= quota.Right && digitHeight >= Math.Floor(8 * scale) && digitHeight <= Math.Ceiling(13 * scale)
                    && Math.Abs(signHeight - digitHeight) <= Math.Ceiling(2 * scale);
            }
        }

        private static void CheckSameDayResetRows(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = now.ToString("o"),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, resetsAt = now.AddMinutes(20).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, resetsAt = now.AddMinutes(10).ToUnixTimeSeconds() }
            };
            var state = new UsageState { status = "ok", observedAt = now.ToString("o") };
            state.windows.Add(new LimitWindow { limitId = "codex", windowDurationMins = QuotaPeriods.WeeklyMinutes,
                remainingPercent = 50, resetsAt = now.AddHours(4).ToUnixTimeSeconds() });
            var claude = ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc);
            var usage = UsagePresentation.Create(state, now, TimeZoneInfo.Utc, claude);
            var layout = new BadgeLayoutSettings { ShowClaude = true };
            layout.Normalize();
            bool fits = true, aligned = true;
            foreach (bool dark in new[] { false, true })
            foreach (float scale in new[] { 0.8f, 1f, 1.25f, 1.5f, 2f })
            {
                bool highContrast = SystemInformation.HighContrast;
                VisualTheme theme = VisualTheme.For(dark, highContrast);
                BadgeTextColors colors = BadgeTextColors.Resolve(theme, layout.BackgroundTransparency, highContrast);
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                Rectangle codex = DetailBounds(layout, "remaining", scale);
                Rectangle fiveHour = DetailBounds(layout, "claude-five-hour", scale);
                Rectangle weekly = DetailBounds(layout, "claude-weekly", scale);
                using (var glyphs = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, usage, layout))
                using (var graphics = Graphics.FromImage(glyphs))
                {
                    CheckResetRow(graphics, glyphs, codex, scale,
                        BadgeRenderer.CalculateCodexSessionRow(graphics, codex, scale, usage, layout),
                        "11월 18일 13:00", "(D-DAY · 4h 00m)", colors, ref fits, ref aligned);
                    CheckResetRow(graphics, glyphs, fiveHour, scale, ClaudeSessionRow(graphics, fiveHour, scale, claude, true),
                        "09:20", "(0h 20m)", colors, ref fits, ref aligned);
                    CheckResetRow(graphics, glyphs, weekly, scale, ClaudeSessionRow(graphics, weekly, scale, claude, false),
                        "11월 18일 09:10", "(D-DAY · 0h 10m)", colors, ref fits, ref aligned);
                }
            }
            checks["badge_detailed_same_day_countdown_preserves_full_clock_and_timer_text"] = fits;
            checks["badge_detailed_same_day_countdown_aligns_inline_text_or_wraps_without_overlap"] = aligned;
        }

        private static void CheckResetRow(Graphics graphics, Bitmap glyphs, Rectangle bounds, float scale,
            BadgeRenderer.SessionRowPlacement row, string time, string timer, BadgeTextColors colors, ref bool fits, ref bool aligned)
        {
            using (var regular = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                int textWidth = BadgeRenderer.MeasureTextWidth(graphics, time, regular);
                int timerWidth = BadgeRenderer.MeasureTextWidth(graphics, timer, bold);
                int timeLeft = row.Reset.Left + BadgeRenderer.ResetIconInset(scale);
                var clock = Rectangle.FromLTRB(timeLeft, row.Reset.Top, row.Reset.Right, row.Reset.Bottom);
                fits &= clock.Width >= textWidth && row.Countdown.Width >= timerWidth && HasResetGroup(glyphs, bounds, row, scale, colors);

                int minuteWidth = BadgeRenderer.MeasureTextWidth(graphics, "00", regular);
                var minuteDigits = new Rectangle(timeLeft + textWidth - minuteWidth, row.Reset.Top, minuteWidth, row.Reset.Height);
                // Compare numeral baselines, counting antialiased pixels and excluding the timer's parentheses.
                int timerMinutes = timer.LastIndexOf('m') - 2;
                int timerPrefix = BadgeRenderer.MeasureTextWidth(graphics, timer.Substring(0, timerMinutes), bold);
                int timerMinuteWidth = BadgeRenderer.MeasureTextWidth(graphics, timer.Substring(timerMinutes, 2), bold);
                var timerDigits = new Rectangle(row.Countdown.Left + timerPrefix, row.Countdown.Top, timerMinuteWidth, row.Countdown.Height);
                Rectangle clockInk = GlyphRows(glyphs, minuteDigits);
                Rectangle timerInk = GlyphRows(glyphs, timerDigits);
                aligned &= !clockInk.IsEmpty && !timerInk.IsEmpty
                    && (row.Reset.Top == row.Countdown.Top
                        ? Math.Abs(clockInk.Bottom - timerInk.Bottom) <= Math.Ceiling(1.5 * scale)
                        : clockInk.Bottom <= timerInk.Top);
            }
        }

        private static bool HasResetGroup(Bitmap glyphs, Rectangle bounds, BadgeRenderer.SessionRowPlacement row, float scale, BadgeTextColors colors)
        {
            if (row.Reset.IsEmpty || row.Countdown.IsEmpty || row.Reset.Width <= BadgeRenderer.ResetIconInset(scale))
                return false;
            int timeLeft = row.Reset.Left + BadgeRenderer.ResetIconInset(scale);
            var icon = Rectangle.FromLTRB(row.Reset.Left, row.Reset.Top, timeLeft, row.Reset.Bottom);
            var time = Rectangle.FromLTRB(timeLeft, row.Reset.Top, row.Reset.Right, row.Reset.Bottom);
            Rectangle iconInk = GlyphRows(glyphs, icon);
            return row.Value.Right <= Math.Min(row.Reset.Left, row.Countdown.Left)
                && (row.Reset.Top == row.Countdown.Top ? row.Reset.Right < row.Countdown.Left : row.Reset.Bottom <= row.Countdown.Top)
                && row.Countdown.Right == bounds.Right
                && !iconInk.IsEmpty && Math.Abs(iconInk.Top + iconInk.Bottom - icon.Top - icon.Bottom) <= Math.Ceiling(4 * scale)
                && HasGlyph(glyphs, time, colors.Muted) && HasGlyph(glyphs, row.Countdown, colors.Text);
        }

        private static BadgeRenderer.SessionRowPlacement ClaudeSessionRow(Graphics graphics, Rectangle bounds, float scale,
            ClaudeUsageDisplay usage, bool fiveHour)
        {
            double remaining = (fiveHour ? usage.FiveHourRemaining : usage.WeeklyRemaining).Value;
            string reset = fiveHour ? usage.FiveHourReset : usage.WeeklyReset;
            string countdown = fiveHour ? usage.FiveHourCountdownText : usage.WeeklyCountdownText;
            return BadgeRenderer.CalculateSessionRow(graphics, bounds, scale, fiveHour ? "현재 세션" : "주간 세션",
                remaining.ToString("0.#", CultureInfo.InvariantCulture) + "%", reset ?? "초기화 시각 확인 불가", reset != null,
                countdown == null ? null : "(" + countdown + ")");
        }

        private static Rectangle DetailBounds(BadgeLayoutSettings layout, string key, float scale)
        {
            RectangleF bounds = BadgeRenderer.ElementBounds(layout.Elements.Find(element => element.Key == key));
            return Rectangle.Round(new RectangleF(bounds.X * scale, bounds.Y * scale, bounds.Width * scale, bounds.Height * scale));
        }

        private static bool HasGlyph(Bitmap bitmap, Rectangle bounds, Color? color = null)
        {
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A != 0 && (!color.HasValue || pixel.ToArgb() == color.Value.ToArgb()))
                    return true;
            }
            return false;
        }

        private static int FirstInkColumn(Bitmap bitmap, Rectangle bounds)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (HasGlyph(bitmap, new Rectangle(x, bounds.Top, 1, bounds.Height))) return x;
            return -1;
        }

        private static Rectangle GlyphRows(Bitmap bitmap, Rectangle bounds, Color? color = null)
        {
            int top = bounds.Bottom, bottom = bounds.Top;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                if (!HasGlyph(bitmap, new Rectangle(bounds.Left, y, bounds.Width, 1), color)) continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
            return bottom > top ? Rectangle.FromLTRB(bounds.Left, top, bounds.Right, bottom) : Rectangle.Empty;
        }

        private static void CheckServiceIcons(Dictionary<string, bool> checks, UsagePresentation usage)
        {
            bool placed = true, variants = true, optional = true, headerUnchanged = true;
            using (var icons = new ServiceIcons(IconSample(Color.DeepSkyBlue), IconSample(Color.DarkOrange), IconSample(Color.MediumVioletRed)))
            foreach (bool compact in new[] { false, true })
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                var layout = new BadgeLayoutSettings { Compact = compact, ShowClaude = true };
                layout.Normalize();
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                var heading = layout.Elements.Find(element => element.Key == "updated");
                int codexY = compact ? (layout.HeaderHeight + layout.CodexHeight) / 2 : heading.Y + BadgeLayoutSettings.ElementHeight(heading) / 2;
                int claudeY = layout.CodexHeight + (compact ? 12 : 23);
                int iconX = layout.ContentPadding + 6;
                using (var dark = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, usage, layout, icons))
                using (var light = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Light, usage, layout, icons))
                using (var textOnly = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Dark, usage, layout))
                {
                    placed &= At(dark, iconX, codexY, scale) == Color.DeepSkyBlue.ToArgb()
                        && At(dark, iconX, claudeY, scale) == Color.DarkOrange.ToArgb();
                    variants &= At(light, iconX, codexY, scale) == Color.MediumVioletRed.ToArgb()
                        && At(light, iconX, claudeY, scale) == Color.DarkOrange.ToArgb();
                    optional &= At(textOnly, iconX, codexY, scale) != Color.DeepSkyBlue.ToArgb()
                        && At(textOnly, iconX, claudeY, scale) != Color.DarkOrange.ToArgb();
                    var chevron = BadgeFormatButton.CalculateBounds(new Rectangle(Point.Empty, size), layout, scale);
                    for (int y = chevron.Top; y < chevron.Bottom; y++)
                        for (int x = chevron.Left; x < chevron.Right; x++)
                            headerUnchanged &= dark.GetPixel(x, y) == textOnly.GetPixel(x, y);
                }
            }
            checks["badge_service_icons_appear_before_both_service_names_across_formats_and_dpi"] = placed;
            checks["badge_service_icons_choose_the_matching_codex_variant_for_each_theme"] = variants;
            checks["badge_service_icons_are_optional_and_leave_the_chevron_pixels_unchanged"] = optional && headerUnchanged;
            using (var source = new Bitmap(32, 32))
            using (var encoded = new MemoryStream())
            {
                source.SetResolution(48, 48);
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.FillRectangle(Brushes.DeepSkyBlue, 20, 20, 12, 12);
                }
                source.Save(encoded, ImageFormat.Png);
                encoded.Position = 0;
                using (var decoded = ServiceIcons.DecodePng(encoded))
                    checks["badge_service_icon_decoder_keeps_pixels_independent_of_source_dpi"] = decoded != null
                        && decoded.Size == source.Size && decoded.GetPixel(25, 25).ToArgb() == Color.DeepSkyBlue.ToArgb()
                        && decoded.GetPixel(10, 10).A == 0;
            }
        }

        private static Bitmap IconSample(Color color)
        {
            var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
            return bitmap;
        }

        private static int At(Bitmap bitmap, int x, int y, float scale)
        {
            return bitmap.GetPixel((int)(x * scale), (int)(y * scale)).ToArgb();
        }
    }
}
