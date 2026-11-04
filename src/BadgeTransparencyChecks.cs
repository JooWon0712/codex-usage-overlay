using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeTransparencyChecks
    {
        private static readonly int[] PercentSteps = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

        public static void Run(Dictionary<string, bool> checks)
        {
            checks["badge_transparency_percent_has_exact_background_alpha"] = BadgeRenderer.BackgroundAlpha(0, false) == 255
                && BadgeRenderer.BackgroundAlpha(25, false) == 191 && BadgeRenderer.BackgroundAlpha(50, false) == 128
                && BadgeRenderer.BackgroundAlpha(100, false) == 0;
            checks["badge_transparency_alpha_clamps_invalid_stored_percent"] = BadgeRenderer.BackgroundAlpha(Int32.MinValue, false) == 255
                && BadgeRenderer.BackgroundAlpha(-1, false) == 255 && BadgeRenderer.BackgroundAlpha(101, false) == 0
                && BadgeRenderer.BackgroundAlpha(Int32.MaxValue, false) == 0;
            bool contrast = true;
            foreach (int percent in new[] { Int32.MinValue, 0, 25, 50, 100, Int32.MaxValue })
                contrast &= BadgeRenderer.BackgroundAlpha(percent, true) == 255;
            checks["badge_transparency_high_contrast_keeps_background_opaque"] = contrast;

            var started = DateTimeOffset.Parse("2026-11-04T10:59:37Z", CultureInfo.InvariantCulture);
            var zone = TimeZoneInfo.CreateCustomTimeZone("alpha-fixture-korea", TimeSpan.FromHours(9), "fixture", "fixture");
            UsagePresentation presentation = UsagePresentation.CreatePreview(started, zone);
            // This read only selects the renderer's existing accessibility policy.
            // The checks never create a window or change the system setting.
            bool highContrast = SystemInformation.HighContrast;
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                    CheckBitmaps(checks, theme, scale, presentation, highContrast);
            CheckEveryGlyphPixel(checks, started, zone, highContrast);
            CheckLightOptions(checks, started, zone, highContrast);
        }

        private static void CheckEveryGlyphPixel(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            bool formats = true, coverage = true, colors = true, outline = true, bounds = true,
                invariant = true, composited = true, backgrounds = true;
            bool backingBounds = true, backingRows = true, opaqueGlyphs = true, backedColors = true,
                backedComposite = true, sharedHeadline = true;
            int combinations = 0;
            UsagePresentation[] states = GlyphStates(started, zone);
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                    foreach (bool date in new[] { false, true })
                        foreach (UsagePresentation presentation in states)
                        {
                            var baselineLayout = new BadgeLayoutSettings();
                            baselineLayout.Elements.Find(element => element.Key == "weekly-reset").Visible = date;
                            baselineLayout.Normalize();
                            var size = new Size((int)Math.Round(baselineLayout.Width * scale), (int)Math.Round(baselineLayout.Height * scale));
                            Color mainColor = presentation.LowWeeklyRemaining.HasValue ? theme.DangerText : theme.Text;
                            using (Bitmap glyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, presentation, baselineLayout))
                            using (Bitmap baselineBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, baselineLayout))
                            {
                                formats &= glyphBitmap.Size == size && baselineBitmap.Size == size
                                    && glyphBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                    && baselineBitmap.PixelFormat == PixelFormat.Format32bppPArgb;
                                RawBitmap glyph = ReadPixels(glyphBitmap), baseline = ReadPixels(baselineBitmap);
                                formats &= glyph.IsPremultiplied() && baseline.IsPremultiplied();
                                bool usesBacking = BadgeRenderer.UsesTextBacking(theme, highContrast);
                                List<Rectangle> rows = usesBacking ? BackingRows(glyph, size, scale, baselineLayout) : null;
                                bool[] allowed = usesBacking ? BackingBounds(rows, size) : OutlineBounds(glyph, size, scale);
                                if (usesBacking)
                                {
                                    backingRows &= rows.Count == (date ? 3 : 2) && ClearBetweenRows(baseline, rows, size);
                                    sharedHeadline &= SharedHeadlineBacking(glyphBitmap, glyph, baseline, scale, presentation, baselineLayout);
                                }
                                int lowCoverage = 0, highCoverage = 0, partialOutline = 0, opaqueCenters = 0;
                                for (int offset = 0; offset < glyph.Data.Length; offset += 4)
                                {
                                    int alpha = glyph.Data[offset + 3], outlinedAlpha = baseline.Data[offset + 3];
                                    if (alpha > 0)
                                    {
                                        coverage &= outlinedAlpha >= alpha;
                                        if (alpha < 128) lowCoverage++;
                                        if (alpha > 128 && alpha < 255) highCoverage++;
                                        colors &= OriginalGlyphContributionMatches(glyph.Data, baseline.Data, offset, theme.Surface);
                                        if (usesBacking)
                                        {
                                            opaqueGlyphs &= outlinedAlpha == 255;
                                            backedColors &= OriginalGlyphContributionMatches(glyph.Data, baseline.Data, offset, theme.Surface);
                                        }
                                        if (alpha == 255)
                                        {
                                            opaqueCenters++;
                                            for (int channel = 0; channel < 4; channel++)
                                                colors &= baseline.Data[offset + channel] == glyph.Data[offset + channel];
                                        }
                                    }
                                    else if (outlinedAlpha > 0)
                                    {
                                        if (outlinedAlpha < 255) partialOutline++;
                                        if (!usesBacking) outline &= OriginalGlyphContributionMatches(glyph.Data, baseline.Data, offset, theme.Surface);
                                        else backedColors &= OriginalGlyphContributionMatches(glyph.Data, baseline.Data, offset, theme.Surface);
                                    }
                                    if (!allowed[offset / 4])
                                        for (int channel = 0; channel < 4; channel++)
                                        {
                                            if (usesBacking) backingBounds &= baseline.Data[offset + channel] == 0;
                                            else bounds &= baseline.Data[offset + channel] == 0;
                                        }
                                    if (outlinedAlpha == 0)
                                        bounds &= baseline.Data[offset] == 0 && baseline.Data[offset + 1] == 0 && baseline.Data[offset + 2] == 0;
                                }
                                coverage &= lowCoverage > 0 && highCoverage > 0;
                                if (!usesBacking) outline &= partialOutline > 0;
                                var heading = new Rectangle((int)Math.Round(10 * scale), (int)Math.Round(6 * scale),
                                    (int)Math.Round(214 * scale), (int)Math.Round(25 * scale));
                                colors &= opaqueCenters > 0 && glyph.FindOpaqueText(heading, mainColor).X >= 0
                                    && baseline.FindOpaqueText(heading, mainColor).X >= 0;

                                foreach (int percent in PercentSteps)
                                {
                                    combinations++;
                                    BadgeLayoutSettings layout = baselineLayout.Copy();
                                    layout.BackgroundTransparency = percent;
                                    using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, layout))
                                    using (Bitmap fullBitmap = BadgeRenderer.CreateBitmap(size, scale, theme, presentation, layout))
                                    {
                                        formats &= textBitmap.Size == size && fullBitmap.Size == size
                                            && textBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                            && fullBitmap.PixelFormat == PixelFormat.Format32bppPArgb;
                                        RawBitmap text = ReadPixels(textBitmap), full = ReadPixels(fullBitmap);
                                        formats &= text.IsPremultiplied() && full.IsPremultiplied();
                                        if (text.Data.Length != baseline.Data.Length || full.Data.Length != baseline.Data.Length)
                                        { invariant = composited = backgrounds = false; continue; }
                                        var background = new Point(size.Width / 2, size.Height - (int)Math.Ceiling(4 * scale));
                                        byte[] sample = { (byte)full.Channel(background, 0), (byte)full.Channel(background, 1),
                                            (byte)full.Channel(background, 2), (byte)full.Channel(background, 3) };
                                        int expectedAlpha = BadgeRenderer.BackgroundAlpha(percent, highContrast);
                                        backgrounds &= text.Channel(background, 3) == 0 && Math.Abs(sample[3] - expectedAlpha) <= 1
                                            && BackgroundChannelsMatch(full, background, theme.Surface, expectedAlpha);
                                        int inset = (int)Math.Ceiling(12 * scale);
                                        for (int offset = 0; offset < text.Data.Length; offset += 4)
                                        {
                                            for (int channel = 0; channel < 4; channel++)
                                                invariant &= text.Data[offset + channel] == baseline.Data[offset + channel];
                                            if (text.Data[offset + 3] > 0)
                                                composited &= SourceOverMatches(text.Data, full.Data, offset, sample);
                                            else
                                            {
                                                int pixel = offset / 4, x = pixel % size.Width, y = pixel / size.Width;
                                                if (x >= inset && x < size.Width - inset && y >= inset && y < size.Height - inset)
                                                    backgrounds &= Math.Abs(full.Data[offset + 3] - expectedAlpha) <= 1;
                                                if (expectedAlpha == 0)
                                                    for (int channel = 0; channel < 4; channel++) backgrounds &= full.Data[offset + channel] == 0;
                                            }
                                            if (usesBacking && glyph.Data[offset + 3] > 0)
                                            {
                                                opaqueGlyphs &= full.Data[offset + 3] == 255;
                                                for (int channel = 0; channel < 4; channel++)
                                                    backedComposite &= full.Data[offset + channel] == baseline.Data[offset + channel];
                                            }
                                        }
                                        if (usesBacking) backedComposite &= CompositeKeepsEveryGlyph(fullBitmap, glyph, baseline);
                                    }
                                }
                            }
                        }
            checks["badge_transparency_all_states_glyph_text_and_full_layers_use_premultiplied_format"] = formats;
            checks["badge_transparency_original_low_and_high_aa_coverage_is_never_deleted"] = coverage;
            checks["badge_transparency_original_glyph_color_contribution_and_opaque_centers_are_preserved"] = colors;
            checks["badge_transparency_fixed_surface_outline_has_soft_partial_alpha"] = outline;
            checks["badge_transparency_outline_stays_within_one_dip_radius_plus_aa_without_extra_background"] = bounds;
            checks["badge_transparency_complete_text_layer_rgba_is_invariant_at_all_percent_values"] = invariant;
            checks["badge_transparency_full_bitmap_uses_source_over_for_every_glyph_and_outline_pixel"] = composited;
            checks["badge_transparency_all_states_preserve_background_alpha_outside_text_mask"] = backgrounds;
            checks["badge_transparency_all_eleven_steps_cover_528_state_size_theme_combinations"] = combinations == 528;
            checks["badge_light_backing_is_limited_to_actual_ink_rows_and_padding"] = backingBounds;
            checks["badge_light_backing_keeps_row_gaps_and_date_off_space_clear"] = backingRows;
            checks["badge_light_every_original_glyph_including_low_aa_has_opaque_backing"] = opaqueGlyphs;
            checks["badge_light_original_aa_blends_only_with_fixed_paper_gray"] = backedColors;
            checks["badge_light_every_glyph_rgba_is_fixed_across_all_steps_and_three_backdrops"] = backedComposite;
            checks["badge_light_main_and_inline_countdown_share_one_backing"] = sharedHeadline;
        }

        // Inspect the ink, rather than font rectangles: chips must stay local to
        // their text even when a long line is clipped and ellipsized.
        private static List<Rectangle> BackingRows(RawBitmap glyph, Size size, float scale, BadgeLayoutSettings layout)
        {
            var rows = new List<Rectangle>();
            foreach (BadgeElement element in layout.Elements)
            {
                if (!element.Visible || element.Key == "countdown") continue;
                var band = Rectangle.Round(new RectangleF(0, element.Y * scale, size.Width,
                    BadgeLayoutSettings.ElementHeight(element) * scale));
                Rectangle ink = glyph.InkBounds(band);
                if (ink.IsEmpty) continue;
                ink.Inflate((int)Math.Ceiling(3 * scale), (int)Math.Ceiling(2 * scale));
                rows.Add(Rectangle.Intersect(ink, new Rectangle(Point.Empty, size)));
            }
            return rows;
        }

        private static bool[] BackingBounds(List<Rectangle> rows, Size size)
        {
            var allowed = new bool[size.Width * size.Height];
            foreach (Rectangle row in rows)
            {
                // GDI+ antialiasing may cover the physical pixel touching an
                // otherwise integral path edge; it must not escape farther.
                Rectangle antialiased = row;
                antialiased.Inflate(1, 1);
                antialiased.Intersect(new Rectangle(Point.Empty, size));
                for (int y = antialiased.Top; y < antialiased.Bottom; y++)
                    for (int x = antialiased.Left; x < antialiased.Right; x++) allowed[y * size.Width + x] = true;
            }
            return allowed;
        }

        private static bool ClearBetweenRows(RawBitmap text, List<Rectangle> rows, Size size)
        {
            for (int index = 1; index < rows.Count; index++)
            {
                if (rows[index - 1].Bottom + 1 >= rows[index].Top - 1) return false;
                for (int y = rows[index - 1].Bottom + 1; y < rows[index].Top - 1; y++)
                    for (int x = 0; x < size.Width; x++)
                        if (text.Channel(new Point(x, y), 3) != 0) return false;
            }
            for (int x = 0; x < size.Width; x++)
                if (text.Channel(new Point(x, size.Height - 1), 3) != 0) return false;
            return rows.Count > 0;
        }

        private static bool SharedHeadlineBacking(Bitmap glyphBitmap, RawBitmap glyph, RawBitmap text, float scale,
            UsagePresentation presentation, BadgeLayoutSettings layout)
        {
            if (!layout.Elements.Find(element => element.Key == "countdown").Visible) return true;
            BadgeRenderer.HeadlinePlacement placement;
            using (Graphics graphics = Graphics.FromImage(glyphBitmap))
                placement = BadgeRenderer.CalculateHeadline(graphics, new Rectangle(Point.Empty, glyphBitmap.Size), scale, presentation, layout);
            Rectangle main = glyph.InkBounds(placement.Main), countdown = glyph.InkBounds(placement.Countdown);
            if (main.IsEmpty || countdown.IsEmpty || countdown.Left <= main.Right) return false;
            var gap = new Point((main.Right + countdown.Left) / 2,
                (Math.Min(main.Top, countdown.Top) + Math.Max(main.Bottom, countdown.Bottom)) / 2);
            return glyph.Channel(gap, 3) == 0 && text.Channel(gap, 3) == 255;
        }

        private static bool CompositeKeepsEveryGlyph(Bitmap bitmap, RawBitmap glyph, RawBitmap reference)
        {
            foreach (Color backdrop in new[] { Color.Black, Color.White, Color.FromArgb(38, 105, 157) })
                using (var composite = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(composite))
                    {
                        graphics.Clear(backdrop);
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.DrawImageUnscaled(bitmap, Point.Empty);
                    }
                    RawBitmap actual = ReadPixels(composite);
                    for (int offset = 0; offset < glyph.Data.Length; offset += 4)
                        if (glyph.Data[offset + 3] > 0)
                            for (int channel = 0; channel < 4; channel++)
                                if (actual.Data[offset + channel] != reference.Data[offset + channel]) return false;
                }
            return true;
        }

        private static void CheckLightOptions(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            checks["badge_light_backing_policy_keeps_dark_and_high_contrast_on_existing_path"] =
                BadgeRenderer.UsesTextBacking(VisualTheme.Light, false)
                && !BadgeRenderer.UsesTextBacking(VisualTheme.Dark, false)
                && !BadgeRenderer.UsesTextBacking(VisualTheme.Light, true)
                && !BadgeRenderer.UsesTextBacking(VisualTheme.Dark, true)
                && !BadgeRenderer.UsesTextBacking(null, false);
            checks["badge_light_backing_preserves_selected_paper_gray_and_text_palette"] =
                VisualTheme.Light.Surface.ToArgb() == Color.FromArgb(0xe4, 0xe5, 0xe6).ToArgb()
                && VisualTheme.Light.Text.ToArgb() == Color.FromArgb(0x2a, 0x2f, 0x36).ToArgb()
                && VisualTheme.Light.Success.ToArgb() == Color.FromArgb(0x30, 0x5f, 0x4c).ToArgb()
                && VisualTheme.Light.Muted.ToArgb() == Color.FromArgb(0x54, 0x5e, 0x69).ToArgb()
                && VisualTheme.Light.Accent.ToArgb() == Color.FromArgb(0x52, 0x62, 0x72).ToArgb()
                && VisualTheme.Light.DangerText.ToArgb() == Color.FromArgb(0x92, 0x26, 0x3a).ToArgb();

            var failure = new UsageState
            {
                status = "stale", reason = "request_timeout",
                observedAt = started.AddMinutes(-4).ToString("o", CultureInfo.InvariantCulture),
                attemptedAt = started.ToString("o", CultureInfo.InvariantCulture)
            };
            foreach (int duration in new[] { QuotaPeriods.FiveHourMinutes, 60, 180, QuotaPeriods.WeeklyMinutes })
                failure.windows.Add(new LimitWindow { limitId = "codex", window = "primary",
                    windowDurationMins = duration, remainingPercent = 96 });
            UsagePresentation longFailure = UsagePresentation.Create(failure, started, zone);
            UsagePresentation[] states = { UsagePresentation.CreatePreview(started, zone), longFailure,
                GlyphStates(started, zone)[3] };
            bool options = true, clear = true, backing = true, clipping = true;
            foreach (float scale in new[] { 1f, 1.5f, 2f })
                foreach (bool date in new[] { false, true })
                    foreach (bool countdown in new[] { false, true })
                        foreach (UsagePresentation presentation in states)
                        {
                            var layout = new BadgeLayoutSettings { BackgroundTransparency = 100 };
                            layout.Elements.Find(element => element.Key == "weekly-reset").Visible = date;
                            layout.Elements.Find(element => element.Key == "countdown").Visible = countdown;
                            layout.Normalize();
                            options &= layout.Height == (date ? 82 : 58)
                                && layout.Elements.Find(element => element.Key == "remaining").Visible
                                && layout.Elements.Find(element => element.Key == "updated").Visible;
                            var size = new Size((int)Math.Round(320 * scale), (int)Math.Round(layout.Height * scale));
                            using (Bitmap glyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Light, presentation, layout))
                            using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, VisualTheme.Light, presentation, layout))
                            using (Bitmap fullBitmap = BadgeRenderer.CreateBitmap(size, scale, VisualTheme.Light, presentation, layout))
                            {
                                RawBitmap glyph = ReadPixels(glyphBitmap), text = ReadPixels(textBitmap), full = ReadPixels(fullBitmap);
                                options &= glyphBitmap.Size == size && textBitmap.Size == size && fullBitmap.Size == size;
                                if (BadgeRenderer.UsesTextBacking(VisualTheme.Light, highContrast))
                                {
                                    List<Rectangle> rows = BackingRows(glyph, size, scale, layout);
                                    clear &= rows.Count == (date ? 3 : 2) && ClearBetweenRows(text, rows, size);
                                    backing &= SharedHeadlineBacking(glyphBitmap, glyph, text, scale, presentation, layout);
                                    for (int offset = 0; offset < glyph.Data.Length; offset += 4)
                                        if (glyph.Data[offset + 3] > 0)
                                        {
                                            backing &= text.Data[offset + 3] == 255 && full.Data[offset + 3] == 255
                                                && OriginalGlyphContributionMatches(glyph.Data, text.Data, offset, VisualTheme.Light.Surface);
                                            for (int channel = 0; channel < 4; channel++)
                                                backing &= text.Data[offset + channel] == full.Data[offset + channel];
                                        }
                                    backing &= CompositeKeepsEveryGlyph(fullBitmap, glyph, text);
                                }
                                using (Graphics graphics = Graphics.FromImage(glyphBitmap))
                                using (var font = new Font("Segoe UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                                {
                                    BadgeRenderer.HeadlinePlacement headline = BadgeRenderer.CalculateHeadline(graphics,
                                        new Rectangle(Point.Empty, size), scale, presentation, layout);
                                    if (presentation == longFailure)
                                        clipping &= BadgeRenderer.MeasureTextWidth(graphics, presentation.MainText, font) > headline.Main.Width;
                                    var header = new Rectangle(0, 0, size.Width, (int)Math.Round(31 * scale));
                                    Rectangle ink = glyph.InkBounds(header);
                                    clipping &= !ink.IsEmpty && ink.Left >= (int)Math.Round(10 * scale)
                                        && ink.Right <= (int)Math.Round(310 * scale);
                                }
                            }
                        }
            checks["badge_light_all_four_date_countdown_options_keep_fixed_or_compact_height"] = options;
            checks["badge_light_all_four_options_keep_clear_row_gaps_and_no_retired_date_space"] = clear;
            checks["badge_light_long_failure_and_unknown_reset_keep_aa_opaque_and_backdrop_independent"] = backing;
            checks["badge_light_long_failure_uses_ellipsis_allocation_without_header_overflow"] = clipping
                && longFailure.HasQueryFailure && longFailure.ResetDateText.IndexOf("확인 불가", StringComparison.Ordinal) >= 0;
        }

        private static bool OriginalGlyphContributionMatches(byte[] glyph, byte[] outlined, int offset, Color surface)
        {
            int addedAlpha = outlined[offset + 3] - glyph[offset + 3];
            if (addedAlpha < 0) return false;
            return Math.Abs(outlined[offset] - glyph[offset] - (surface.B * addedAlpha + 127) / 255) <= 1
                && Math.Abs(outlined[offset + 1] - glyph[offset + 1] - (surface.G * addedAlpha + 127) / 255) <= 1
                && Math.Abs(outlined[offset + 2] - glyph[offset + 2] - (surface.R * addedAlpha + 127) / 255) <= 1;
        }

        private static bool SourceOverMatches(byte[] source, byte[] actual, int offset, byte[] background)
        {
            int inverse = 255 - source[offset + 3];
            for (int channel = 0; channel < 4; channel++)
                if (Math.Abs(actual[offset + channel] - source[offset + channel] - (background[channel] * inverse + 127) / 255) > 2)
                    return false;
            return true;
        }

        private static bool[] OutlineBounds(RawBitmap glyph, Size size, float scale)
        {
            var allowed = new bool[size.Width * size.Height];
            double reach = Math.Max(1d, scale) + 1d;
            int radius = (int)Math.Ceiling(reach);
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    if (glyph.Data[(y * size.Width + x) * 4 + 3] == 0) continue;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                            if (dx * dx + dy * dy <= reach * reach)
                            {
                                int tx = x + dx, ty = y + dy;
                                if (tx >= 0 && tx < size.Width && ty >= 0 && ty < size.Height) allowed[ty * size.Width + tx] = true;
                            }
                }
            return allowed;
        }
        private static UsagePresentation[] GlyphStates(DateTimeOffset started, TimeZoneInfo zone)
        {
            var low = new UsageState { status = "ok", observedAt = started.ToString("o", CultureInfo.InvariantCulture) };
            low.windows.Add(new LimitWindow { limitId = "codex", window = "primary", windowDurationMins = QuotaPeriods.WeeklyMinutes,
                remainingPercent = 14, resetsAt = started.AddDays(7).ToUnixTimeSeconds() });
            var stale = new UsageState { status = "stale", observedAt = started.AddMinutes(-4).ToString("o", CultureInfo.InvariantCulture),
                attemptedAt = started.ToString("o", CultureInfo.InvariantCulture), reason = "request_timeout", windows = low.windows };
            var unavailable = new UsageState { status = "unavailable", reason = "usage_unavailable", attemptedAt = started.ToString("o", CultureInfo.InvariantCulture) };
            return new[] { UsagePresentation.CreatePreview(started, zone), UsagePresentation.Create(low, started, zone),
                UsagePresentation.Create(stale, started, zone), UsagePresentation.Create(unavailable, started, zone) };
        }

        private static void CheckBitmaps(Dictionary<string, bool> checks, VisualTheme theme, float scale,
            UsagePresentation presentation, bool highContrast)
        {
            bool format = true, backgrounds = true, corners = true, text = true, premultiplied = true, composed = true;
            foreach (bool date in new[] { true, false })
                foreach (int percent in PercentSteps)
                {
                    var layout = new BadgeLayoutSettings { BackgroundTransparency = percent };
                    layout.Elements.Find(element => element.Key == "weekly-reset").Visible = date;
                    layout.Normalize();
                    var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                    using (Bitmap bitmap = BadgeRenderer.CreateBitmap(size, scale, theme, presentation, layout))
                    {
                        format &= bitmap.PixelFormat == PixelFormat.Format32bppPArgb && bitmap.Size == size;
                        RawBitmap raw = ReadPixels(bitmap);
                        var background = new Point(size.Width / 2, size.Height - (int)Math.Ceiling(4 * scale));
                        int expected = highContrast ? 255 : (int)Math.Round(255d * (100 - percent) / 100d);
                        backgrounds &= Math.Abs(raw.Channel(background, 3) - expected) <= 1;
                        corners &= raw.Channel(Point.Empty, 3) == 0 && raw.Channel(new Point(size.Width - 1, 0), 3) == 0
                            && raw.Channel(new Point(0, size.Height - 1), 3) == 0
                            && raw.Channel(new Point(size.Width - 1, size.Height - 1), 3) == 0;
                        BadgeElement main = layout.Elements.Find(element => element.Key == "remaining");
                        var heading = new Rectangle((int)Math.Round(main.X * scale), (int)Math.Round(main.Y * scale),
                            (int)Math.Round(main.Width * scale), (int)Math.Round(BadgeLayoutSettings.ElementHeight(main) * scale));
                        Point solidText = raw.FindOpaqueText(heading, theme.Text);
                        text &= solidText.X >= 0;
                        premultiplied &= raw.IsPremultiplied() && BackgroundChannelsMatch(raw, background, theme.Surface, expected);
                        // Compare actual alpha composition on three backdrops,
                        // including the fully transparent and fully opaque limits.
                        foreach (Color backdrop in new[] { Color.Black, Color.White, Color.FromArgb(38, 105, 157) })
                            composed &= CompositeMatches(bitmap, raw, background, solidText, backdrop, theme.Text);
                    }
                }
            string prefix = "badge_transparency_" + (theme.IsDark ? "dark_" : "light_") + (int)(scale * 100) + "_";
            checks[prefix + "both_heights_use_32bit_premultiplied_bitmaps"] = format;
            checks[prefix + "background_alpha_tracks_all_eleven_percent_values"] = backgrounds;
            checks[prefix + "rounded_outer_corners_are_fully_transparent"] = corners;
            checks[prefix + "heading_keeps_opaque_glyph_pixels_even_without_background"] = text;
            checks[prefix + "raw_rgb_is_premultiplied_once_and_zero_alpha_has_zero_rgb"] = premultiplied;
            checks[prefix + "black_white_and_colored_backdrops_composite_without_text_fading"] = composed;
        }

        private static bool BackgroundChannelsMatch(RawBitmap raw, Point point, Color surface, int alpha)
        {
            return Math.Abs(raw.Channel(point, 0) - (surface.B * alpha + 127) / 255) <= 1
                && Math.Abs(raw.Channel(point, 1) - (surface.G * alpha + 127) / 255) <= 1
                && Math.Abs(raw.Channel(point, 2) - (surface.R * alpha + 127) / 255) <= 1;
        }

        private static bool CompositeMatches(Bitmap bitmap, RawBitmap raw, Point background, Point text,
            Color backdrop, Color foreground)
        {
            if (text.X < 0) return false;
            using (var composite = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(composite))
                {
                    graphics.Clear(backdrop);
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.DrawImageUnscaled(bitmap, Point.Empty);
                }
                Color pixel = composite.GetPixel(background.X, background.Y);
                int inverse = 255 - raw.Channel(background, 3);
                bool backgroundMatches = pixel.A == 255
                    && Math.Abs(pixel.R - raw.Channel(background, 2) - (backdrop.R * inverse + 127) / 255) <= 2
                    && Math.Abs(pixel.G - raw.Channel(background, 1) - (backdrop.G * inverse + 127) / 255) <= 2
                    && Math.Abs(pixel.B - raw.Channel(background, 0) - (backdrop.B * inverse + 127) / 255) <= 2;
                Color glyph = composite.GetPixel(text.X, text.Y);
                return backgroundMatches && glyph.A == 255 && Math.Abs(glyph.R - foreground.R) <= 1
                    && Math.Abs(glyph.G - foreground.G) <= 1 && Math.Abs(glyph.B - foreground.B) <= 1;
            }
        }

        private static RawBitmap ReadPixels(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var bytes = new byte[bitmap.Width * bitmap.Height * 4];
                for (int row = 0; row < bitmap.Height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), bytes, row * bitmap.Width * 4, bitmap.Width * 4);
                return new RawBitmap(bitmap.Size, bytes);
            }
            finally { bitmap.UnlockBits(data); }
        }

        private sealed class RawBitmap
        {
            private readonly Size size;
            private readonly byte[] bytes;
            public RawBitmap(Size size, byte[] bytes) { this.size = size; this.bytes = bytes; }
            public byte[] Data { get { return bytes; } }
            public int Channel(Point point, int channel) { return bytes[(point.Y * size.Width + point.X) * 4 + channel]; }
            public Rectangle InkBounds(Rectangle bounds)
            {
                bounds = Rectangle.Intersect(bounds, new Rectangle(Point.Empty, size));
                int left = size.Width, right = -1, top = size.Height, bottom = -1;
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                    for (int x = bounds.Left; x < bounds.Right; x++)
                        if (Channel(new Point(x, y), 3) > 0)
                        {
                            left = Math.Min(left, x); right = Math.Max(right, x);
                            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                        }
                return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }
            public bool IsPremultiplied()
            {
                for (int pixel = 0; pixel < bytes.Length; pixel += 4)
                    if (bytes[pixel] > bytes[pixel + 3] || bytes[pixel + 1] > bytes[pixel + 3] || bytes[pixel + 2] > bytes[pixel + 3])
                        return false;
                return true;
            }
            public Point FindOpaqueText(Rectangle bounds, Color text)
            {
                bounds = Rectangle.Intersect(bounds, new Rectangle(Point.Empty, size));
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                    for (int x = bounds.Left; x < bounds.Right; x++)
                    {
                        var point = new Point(x, y);
                        if (Channel(point, 3) == 255 && Channel(point, 2) == text.R
                            && Channel(point, 1) == text.G && Channel(point, 0) == text.B) return point;
                    }
                return new Point(-1, -1);
            }
        }
    }
}
