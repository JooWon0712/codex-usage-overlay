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
        private static readonly Color[] Backdrops = { Color.Black, Color.White, Color.FromArgb(38, 105, 157) };

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
            // No window, account, desktop-pixel sampling or system-setting change.
            bool highContrast = SystemInformation.HighContrast;
            CheckProfiles(checks);
            CheckSoftShadow(checks, started, zone, highContrast);
            UsagePresentation preview = UsagePresentation.CreatePreview(started, zone);
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                    CheckBitmaps(checks, theme, scale, preview, highContrast);
            CheckEveryGlyphPixel(checks, started, zone, highContrast);
            CheckOptions(checks, started, zone, highContrast);
        }

        private static void CheckProfiles(Dictionary<string, bool> checks)
        {
            bool dark = true, light = true, original = true, contrast = true, floor = true, high = true, neutral = true, shadowPolicy = true;
            Color[] source = Palette(VisualTheme.Light);
            source[2] = source[3] = VisualTheme.Light.Muted;
            Color[] darkSource = Palette(VisualTheme.Dark);
            darkSource[2] = darkSource[3] = VisualTheme.Dark.Muted;
            Color[] lightBright = { Color.FromArgb(0xf8,0xfa,0xfc), Color.FromArgb(0xe2,0xe8,0xf0),
                Color.FromArgb(0xe2,0xe8,0xf0), Color.FromArgb(0xe2,0xe8,0xf0), Color.FromArgb(0xff,0xe0,0xa3),
                Color.FromArgb(0xff,0xac,0xb8), Color.FromArgb(0xff,0xc2,0xcd) };
            foreach (int percent in PercentSteps)
            {
                BadgeTextColors darkColors = BadgeTextColors.Resolve(VisualTheme.Dark, percent, false);
                dark &= SamePalette(Palette(darkColors), darkSource) && darkColors.Bright
                    && darkColors.UseOutline && darkColors.UseSoftShadow
                    && darkColors.OutlineColor == Color.FromArgb(0x0d,0x11,0x17);
                BadgeTextColors colors = BadgeTextColors.Resolve(VisualTheme.Light, percent, false);
                neutral &= colors.Success == colors.Muted && colors.Accent == colors.Muted
                    && darkColors.Success == darkColors.Muted && darkColors.Accent == darkColors.Muted;
                shadowPolicy &= colors.UseSoftShadow == (percent >= 50) && darkColors.UseSoftShadow;
                if (percent < 50)
                {
                    light &= !colors.Bright && !colors.UseOutline && !colors.UseSoftShadow;
                    Color minimum = BadgeTextColors.MinimumBody(VisualTheme.Light.Surface, percent);
                    int alpha = (int)Math.Round(255d * (100 - percent) / 100d);
                    floor &= minimum.R == (VisualTheme.Light.Surface.R * alpha + 127) / 255
                        && minimum.G == (VisualTheme.Light.Surface.G * alpha + 127) / 255
                        && minimum.B == (VisualTheme.Light.Surface.B * alpha + 127) / 255;
                    Color[] resolved = Palette(colors);
                    for (int index = 0; index < resolved.Length; index++)
                    {
                        contrast &= resolved[index].A == 255 && BadgeTextColors.Contrast(resolved[index], minimum) >= 4.5;
                        original &= resolved[index].R <= source[index].R && resolved[index].G <= source[index].G
                            && resolved[index].B <= source[index].B;
                        if (BadgeTextColors.Contrast(source[index], minimum) >= 4.5)
                            original &= resolved[index] == source[index];
                    }
                }
                else
                {
                    light &= colors.Bright && colors.UseOutline && colors.UseSoftShadow
                        && colors.OutlineColor == Color.FromArgb(0x19,0x1d,0x24)
                        && SamePalette(Palette(colors), lightBright)
                        && colors.Muted.R >= 220 && colors.Accent == colors.Muted && colors.Success == colors.Muted
                        && colors.Attention.R > colors.Attention.B && colors.DangerText.R > colors.DangerText.G;
                    foreach (Color foreground in Palette(colors))
                        light &= BadgeTextColors.Contrast(foreground,colors.OutlineColor)>=4.5;
                }
                foreach (bool darkSystem in new[] { false, true })
                {
                    VisualTheme system = VisualTheme.For(darkSystem, true);
                    BadgeTextColors systemColors = BadgeTextColors.Resolve(system, percent, true);
                    high &= SamePalette(Palette(systemColors), Palette(system)) && !systemColors.Bright
                        && systemColors.UseOutline && !systemColors.UseSoftShadow && systemColors.OutlineColor == system.Surface;
                }
            }
            BadgeTextColors forty = BadgeTextColors.Resolve(VisualTheme.Light, 40, false);
            BadgeTextColors fifty = BadgeTextColors.Resolve(VisualTheme.Light, 50, false);
            checks["badge_foreground_selected_charcoal_profile_and_soft_shadow_are_invariant_at_all_steps"] = dark;
            checks["badge_foreground_light_uses_dark_text_below_fifty_and_bright_text_from_fifty"] = light;
            checks["badge_foreground_both_neutral_themes_share_secondary_tone_for_normal_information"] = neutral;
            checks["badge_foreground_soft_shadow_is_for_dark_all_steps_and_light_bright_profile"] = shadowPolicy;
            checks["badge_foreground_light_preserves_sufficient_original_colors_and_only_darkens_others"] = original;
            checks["badge_foreground_light_dark_candidates_have_4_5_contrast_against_black_backdrop_floor"] = contrast;
            checks["badge_foreground_floor_is_actual_paper_gray_alpha_over_black"] = floor;
            checks["badge_foreground_forty_to_fifty_switch_changes_only_foreground_profile"] = !forty.Bright && !forty.UseOutline && !forty.UseSoftShadow
                && fifty.Bright && fifty.UseOutline && fifty.UseSoftShadow && forty.Text != fifty.Text
                && VisualTheme.Light.Surface.ToArgb() == Color.FromArgb(0xdb, 0xde, 0xe2).ToArgb();
            checks["badge_foreground_high_contrast_keeps_system_palette_before_opacity_policy"] = high;
            checks["badge_foreground_null_and_invalid_percent_resolve_to_clamped_profiles"] =
                SamePalette(Palette(BadgeTextColors.Resolve(null, 0, false)), darkSource)
                && SamePalette(Palette(BadgeTextColors.Resolve(VisualTheme.Light, Int32.MinValue, false)),
                    Palette(BadgeTextColors.Resolve(VisualTheme.Light, 0, false)))
                && SamePalette(Palette(BadgeTextColors.Resolve(VisualTheme.Light, Int32.MaxValue, false)),
                    Palette(BadgeTextColors.Resolve(VisualTheme.Light, 100, false)));
            checks["badge_foreground_contrast_formula_has_known_luminance_endpoints"] =
                Math.Abs(BadgeTextColors.Contrast(Color.Black, Color.White) - 21d) < 0.000001
                && Math.Abs(BadgeTextColors.Contrast(Color.FromArgb(119, 119, 119), Color.White) - 4.478089d) < 0.00001
                && BadgeTextColors.Contrast(Color.Black, Color.Black) == 1d;
        }

        private static void CheckEveryGlyphPixel(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            bool formats = true, coverage = true, colors = true, outline = true, bounds = true,
                composited = true, backgrounds = true, backdrops = true, darkInvariant = true;
            int combinations = 0;
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                    foreach (bool date in new[] { false, true })
                        foreach (UsagePresentation presentation in GlyphStates(started, zone))
                        {
                            var layout = new BadgeLayoutSettings();
                            layout.Elements.Find(element => element.Key == "weekly-reset").Visible = date;
                            layout.Normalize();
                            var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                            using (Bitmap referenceGlyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, presentation, layout))
                            using (Bitmap referenceTextBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, layout))
                            {
                                RawBitmap referenceGlyph = ReadPixels(referenceGlyphBitmap), referenceText = ReadPixels(referenceTextBitmap);
                                bool[] allowedOutline = UnderlayBounds(referenceGlyph, size, scale, false);
                                bool[] allowedShadow = UnderlayBounds(referenceGlyph, size, scale, true);
                                foreach (int percent in PercentSteps)
                                {
                                    combinations++;
                                    layout.BackgroundTransparency = percent;
                                    BadgeTextColors profile = BadgeTextColors.Resolve(theme, percent, highContrast);
                                    using (Bitmap glyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, presentation, layout))
                                    using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, layout))
                                    using (Bitmap fullBitmap = BadgeRenderer.CreateBitmap(size, scale, theme, presentation, layout))
                                    {
                                        RawBitmap glyph = ReadPixels(glyphBitmap), text = ReadPixels(textBitmap), full = ReadPixels(fullBitmap);
                                        formats &= glyphBitmap.Size == size && textBitmap.Size == size && fullBitmap.Size == size
                                            && glyphBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                            && textBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                            && fullBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                            && glyph.IsPremultiplied() && text.IsPremultiplied() && full.IsPremultiplied();
                                        Point background = BackgroundPoint(size, scale);
                                        byte[] sample = full.Pixel(background);
                                        int expectedAlpha = BadgeRenderer.BackgroundAlpha(percent, highContrast);
                                        backgrounds &= text.Channel(background, 3) == 0 && Math.Abs(sample[3] - expectedAlpha) <= 1
                                            && BackgroundChannelsMatch(full, background, theme.Surface, expectedAlpha);
                                        int lowCoverage = 0, highCoverage = 0, partialOutline = 0;
                                        int inset = (int)Math.Ceiling(12 * scale);
                                        for (int offset = 0; offset < glyph.Data.Length; offset += 4)
                                        {
                                            int alpha = glyph.Data[offset + 3], textAlpha = text.Data[offset + 3];
                                            coverage &= alpha == referenceGlyph.Data[offset + 3] && textAlpha >= alpha;
                                            if (alpha > 0 && alpha < 128) lowCoverage++;
                                            if (alpha > 128 && alpha < 255) highCoverage++;
                                            if (profile.UseOutline)
                                            {
                                                colors &= OriginalGlyphContributionMatches(glyph.Data, text.Data, offset, profile.OutlineColor);
                                                if (alpha == 0 && textAlpha > 0 && textAlpha < 255) partialOutline++;
                                            }
                                            else
                                                for (int channel = 0; channel < 4; channel++) colors &= glyph.Data[offset + channel] == text.Data[offset + channel];
                                            bool allowed = profile.UseOutline
                                                ? (profile.UseSoftShadow ? allowedShadow[offset / 4] : allowedOutline[offset / 4])
                                                : alpha > 0;
                                            if (!allowed)
                                                for (int channel = 0; channel < 4; channel++) bounds &= text.Data[offset + channel] == 0;
                                            if (textAlpha > 0) composited &= SourceOverMatches(text.Data, full.Data, offset, sample);
                                            else
                                            {
                                                int pixel = offset / 4, x = pixel % size.Width, y = pixel / size.Width;
                                                if (x >= inset && x < size.Width - inset && y >= inset && y < size.Height - inset)
                                                    backgrounds &= Math.Abs(full.Data[offset + 3] - expectedAlpha) <= 1;
                                                if (expectedAlpha == 0)
                                                    for (int channel = 0; channel < 4; channel++) backgrounds &= full.Data[offset + channel] == 0;
                                            }
                                            if (theme.IsDark)
                                                for (int channel = 0; channel < 4; channel++)
                                                    darkInvariant &= glyph.Data[offset + channel] == referenceGlyph.Data[offset + channel]
                                                        && text.Data[offset + channel] == referenceText.Data[offset + channel];
                                        }
                                        coverage &= lowCoverage > 0 && highCoverage > 0;
                                        if (profile.UseOutline) outline &= partialOutline > 0;
                                        colors &= OpaqueCentersMatch(glyphBitmap, glyph, text, scale, presentation, layout, profile);
                                        backdrops &= CompositeGlyphsMatch(fullBitmap, full, glyph);
                                    }
                                }
                            }
                        }
            checks["badge_transparency_all_states_glyph_text_and_full_layers_use_premultiplied_format"] = formats;
            checks["badge_transparency_original_low_and_high_aa_coverage_is_never_deleted"] = coverage;
            checks["badge_transparency_glyph_contribution_and_opaque_centers_match_each_resolved_profile"] = colors;
            checks["badge_transparency_enabled_outline_or_soft_shadow_keeps_partial_alpha"] = outline;
            checks["badge_transparency_no_fixed_row_backing_or_pixels_outside_glyph_and_profile_underlay"] = bounds;
            checks["badge_transparency_full_bitmap_uses_source_over_for_every_glyph_and_outline_pixel"] = composited;
            checks["badge_transparency_all_states_preserve_background_alpha_outside_text_mask"] = backgrounds;
            checks["badge_transparency_all_glyphs_composite_correctly_on_black_white_and_colored_backdrops"] = backdrops;
            checks["badge_transparency_dark_glyph_and_soft_shadow_rgba_are_unchanged_by_opacity"] = darkInvariant;
            checks["badge_transparency_all_eleven_steps_cover_528_state_size_theme_combinations"] = combinations == 528;
        }

        private static void CheckSoftShadow(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            CheckSoftShadow(checks, started, zone, highContrast, VisualTheme.Light);
            CheckSoftShadow(checks, started, zone, highContrast, VisualTheme.Dark);
            checks["badge_light_soft_shadow_offset_is_one_physical_pixel_at_common_dpi_and_scales_above_it"] =
                BadgeRenderer.ShadowOffsetY(1f) == 1 && BadgeRenderer.ShadowOffsetY(1.5f) == 1
                && BadgeRenderer.ShadowOffsetY(2f) == 1 && BadgeRenderer.ShadowOffsetY(3f) == 2
                && BadgeRenderer.ShadowOffsetY(4f) == 2;
        }

        private static void CheckSoftShadow(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast, VisualTheme theme)
        {
            bool mask = true, partial = true, centers = true;
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                if (highContrast) continue;
                var layout = new BadgeLayoutSettings { BackgroundTransparency = 100 };
                layout.Elements.Find(element => element.Key == "weekly-reset").Visible = false;
                layout.Normalize();
                var size = new Size((int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                UsagePresentation presentation = UsagePresentation.CreatePreview(started, zone);
                using (Bitmap glyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, theme, presentation, layout))
                using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, layout))
                {
                    RawBitmap glyph = ReadPixels(glyphBitmap), text = ReadPixels(textBitmap);
                    double radius = Math.Max(1d, scale);
                    int reach = (int)Math.Ceiling(radius + 1d), shift = Math.Max(1, (int)Math.Round(scale / 2d));
                    double totalWeight = 0;
                    for (int dy = -reach; dy <= reach; dy++)
                        for (int dx = -reach; dx <= reach; dx++)
                            totalWeight += Math.Max(0d, Math.Min(1d, radius + 1d - Math.Sqrt(dx * dx + dy * dy)));
                    int partialPixels = 0;
                    // Gather at each destination, independently of the renderer's
                    // source-scatter implementation, including empty mask samples.
                    for (int y = 0; y < size.Height; y++)
                        for (int x = 0; x < size.Width; x++)
                        {
                            double weighted = 0;
                            for (int dy = -reach; dy <= reach; dy++)
                                for (int dx = -reach; dx <= reach; dx++)
                                {
                                    int sourceX = x - dx, sourceY = y - dy - shift;
                                    if (sourceX < 0 || sourceX >= size.Width || sourceY < 0 || sourceY >= size.Height) continue;
                                    double weight = Math.Max(0d, Math.Min(1d, radius + 1d - Math.Sqrt(dx * dx + dy * dy)));
                                    weighted += glyph.Channel(new Point(sourceX, sourceY), 3) * weight;
                                }
                            var point = new Point(x, y);
                            int glyphAlpha = glyph.Channel(point, 3);
                            int shadowAlpha = (int)Math.Round(weighted * 0.85d / totalWeight);
                            int expected = glyphAlpha + (shadowAlpha * (255 - glyphAlpha) + 127) / 255;
                            // Different accumulation order can straddle a half
                            // integer; permit one alpha unit of rounding only.
                            mask &= Math.Abs(text.Channel(point, 3) - expected) <= 1;
                            if (glyphAlpha == 0 && text.Channel(point, 3) > 0)
                            {
                                partialPixels++;
                                partial &= text.Channel(point, 3) <= 217;
                            }
                            if (glyphAlpha == 255)
                                for (int channel = 0; channel < 4; channel++) centers &= glyph.Channel(point, channel) == text.Channel(point, channel);
                        }
                    partial &= partialPixels > 0;
                }
            }
            string prefix = theme.IsDark ? "badge_dark_" : "badge_light_";
            checks[prefix + "soft_shadow_matches_weighted_average_alpha_and_downward_offset"] = mask;
            checks[prefix + "soft_shadow_is_partial_at_empty_glyph_pixels_and_keeps_opaque_centers"] = partial && centers;
        }

        private static bool OpaqueCentersMatch(Bitmap bitmap, RawBitmap glyph, RawBitmap text, float scale,
            UsagePresentation presentation, BadgeLayoutSettings layout, BadgeTextColors profile)
        {
            BadgeRenderer.HeadlinePlacement headline;
            using (Graphics graphics = Graphics.FromImage(bitmap))
                headline = BadgeRenderer.CalculateHeadline(graphics, new Rectangle(Point.Empty, bitmap.Size), scale, presentation, layout);
            foreach (BadgeElement element in layout.Elements)
            {
                if (!element.Visible) continue;
                Color color = element.Key == "remaining" ? (presentation.LowWeeklyRemaining.HasValue ? profile.DangerText : profile.Text)
                    : element.Key == "countdown" ? (presentation.LowWeeklyRemaining.HasValue ? profile.DangerText : profile.Accent)
                    : element.Key == "weekly-reset" ? profile.Muted
                    : presentation.Tone == UsageTone.Low ? profile.Danger
                    : presentation.Tone == UsageTone.Normal ? profile.Success : profile.Attention;
                Rectangle region = Rectangle.Round(new RectangleF(element.X * scale, element.Y * scale,
                    element.Width * scale, BadgeLayoutSettings.ElementHeight(element) * scale));
                if (element.Key == "remaining") region = headline.Main;
                if (element.Key == "countdown") region = headline.Countdown;
                Point center = glyph.FindOpaqueText(region, color);
                if (center.X < 0) return false;
                for (int channel = 0; channel < 4; channel++)
                    if (glyph.Channel(center, channel) != text.Channel(center, channel)) return false;
            }
            return true;
        }

        private static void CheckOptions(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            var failure = new UsageState { status = "stale", reason = "request_timeout",
                observedAt = started.AddMinutes(-4).ToString("o", CultureInfo.InvariantCulture),
                attemptedAt = started.ToString("o", CultureInfo.InvariantCulture) };
            foreach (int duration in new[] { QuotaPeriods.FiveHourMinutes, 60, 180, QuotaPeriods.WeeklyMinutes })
                failure.windows.Add(new LimitWindow { limitId = "codex", window = "primary", windowDurationMins = duration, remainingPercent = 96 });
            UsagePresentation longFailure = UsagePresentation.Create(failure, started, zone);
            bool options = true, clipping = true, glyphs = true;
            foreach (float scale in new[] { 1f, 1.5f, 2f })
                foreach (bool date in new[] { false, true })
                    foreach (bool countdown in new[] { false, true })
                        foreach (int percent in new[] { 40, 50, 100 })
                            foreach (UsagePresentation presentation in new[] { longFailure, GlyphStates(started, zone)[3] })
                            {
                                var layout = new BadgeLayoutSettings { BackgroundTransparency = percent };
                                layout.Elements.Find(element => element.Key == "weekly-reset").Visible = date;
                                layout.Elements.Find(element => element.Key == "countdown").Visible = countdown;
                                layout.Normalize();
                                options &= layout.Height == (date ? 82 : 58)
                                    && layout.Elements.Find(element => element.Key == "remaining").Visible
                                    && layout.Elements.Find(element => element.Key == "updated").Visible;
                                var size = new Size((int)Math.Round(320 * scale), (int)Math.Round(layout.Height * scale));
                                using (Bitmap glyphBitmap = BadgeRenderer.CreateGlyphBitmap(size, scale, VisualTheme.Light, presentation, layout))
                                using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, VisualTheme.Light, presentation, layout))
                                {
                                    RawBitmap glyph = ReadPixels(glyphBitmap), text = ReadPixels(textBitmap);
                                    glyphs &= OpaqueCentersMatch(glyphBitmap, glyph, text, scale, presentation, layout,
                                        BadgeTextColors.Resolve(VisualTheme.Light, percent, highContrast));
                                    using (Graphics graphics = Graphics.FromImage(glyphBitmap))
                                    using (var font = new Font("Segoe UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                                    {
                                        BadgeRenderer.HeadlinePlacement headline = BadgeRenderer.CalculateHeadline(graphics,
                                            new Rectangle(Point.Empty, size), scale, presentation, layout);
                                        if (presentation == longFailure)
                                            clipping &= BadgeRenderer.MeasureTextWidth(graphics, presentation.MainText, font) > headline.Main.Width;
                                        Rectangle ink = glyph.InkBounds(new Rectangle(0, 0, size.Width, (int)Math.Round(31 * scale)));
                                        clipping &= !ink.IsEmpty && ink.Left >= (int)Math.Round(10 * scale)
                                            && ink.Right <= (int)Math.Round(310 * scale);
                                    }
                                }
                            }
            checks["badge_transparency_all_four_date_countdown_options_keep_fixed_or_compact_height"] = options;
            checks["badge_transparency_long_failure_and_unknown_reset_keep_resolved_opaque_centers"] = glyphs;
            checks["badge_transparency_long_failure_uses_ellipsis_allocation_without_header_overflow"] = clipping
                && longFailure.HasQueryFailure && longFailure.ResetDateText.IndexOf("확인 불가", StringComparison.Ordinal) >= 0;
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
                        Point background = BackgroundPoint(size, scale);
                        int expected = BadgeRenderer.BackgroundAlpha(percent, highContrast);
                        backgrounds &= Math.Abs(raw.Channel(background, 3) - expected) <= 1;
                        corners &= raw.Channel(Point.Empty, 3) == 0 && raw.Channel(new Point(size.Width - 1, 0), 3) == 0
                            && raw.Channel(new Point(0, size.Height - 1), 3) == 0
                            && raw.Channel(new Point(size.Width - 1, size.Height - 1), 3) == 0;
                        BadgeTextColors colors = BadgeTextColors.Resolve(theme, percent, highContrast);
                        var heading = new Rectangle((int)Math.Round(10 * scale), (int)Math.Round(6 * scale),
                            (int)Math.Round(300 * scale), (int)Math.Round(25 * scale));
                        Point solidText = raw.FindOpaqueText(heading, colors.Text);
                        text &= solidText.X >= 0;
                        premultiplied &= raw.IsPremultiplied() && BackgroundChannelsMatch(raw, background, theme.Surface, expected);
                        foreach (Color backdrop in Backdrops)
                            composed &= CompositeSamplesMatch(bitmap, raw, background, solidText, backdrop);
                    }
                }
            string prefix = "badge_transparency_" + (theme.IsDark ? "dark_" : "light_") + (int)(scale * 100) + "_";
            checks[prefix + "both_heights_use_32bit_premultiplied_bitmaps"] = format;
            checks[prefix + "background_alpha_tracks_all_eleven_percent_values"] = backgrounds;
            checks[prefix + "rounded_outer_corners_are_fully_transparent"] = corners;
            checks[prefix + "heading_keeps_resolved_opaque_glyph_pixels_even_without_background"] = text;
            checks[prefix + "raw_rgb_is_premultiplied_once_and_zero_alpha_has_zero_rgb"] = premultiplied;
            checks[prefix + "black_white_and_colored_backdrops_composite_without_text_fading"] = composed;
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

        private static Color[] Palette(VisualTheme theme)
        { return new[] { theme.Text, theme.Muted, theme.Accent, theme.Success, theme.Attention, theme.Danger, theme.DangerText }; }
        private static Color[] Palette(BadgeTextColors colors)
        { return new[] { colors.Text, colors.Muted, colors.Accent, colors.Success, colors.Attention, colors.Danger, colors.DangerText }; }
        private static bool SamePalette(Color[] first, Color[] second)
        {
            for (int index = 0; index < first.Length; index++) if (first[index] != second[index]) return false;
            return true;
        }
        private static Point BackgroundPoint(Size size, float scale)
        { return new Point(size.Width / 2, size.Height - (int)Math.Ceiling(4 * scale)); }
        private static bool BackgroundChannelsMatch(RawBitmap raw, Point point, Color surface, int alpha)
        {
            return Math.Abs(raw.Channel(point, 0) - (surface.B * alpha + 127) / 255) <= 1
                && Math.Abs(raw.Channel(point, 1) - (surface.G * alpha + 127) / 255) <= 1
                && Math.Abs(raw.Channel(point, 2) - (surface.R * alpha + 127) / 255) <= 1;
        }
        private static bool OriginalGlyphContributionMatches(byte[] glyph, byte[] text, int offset, Color outline)
        {
            int addedAlpha = text[offset + 3] - glyph[offset + 3];
            return addedAlpha >= 0
                && Math.Abs(text[offset] - glyph[offset] - (outline.B * addedAlpha + 127) / 255) <= 1
                && Math.Abs(text[offset + 1] - glyph[offset + 1] - (outline.G * addedAlpha + 127) / 255) <= 1
                && Math.Abs(text[offset + 2] - glyph[offset + 2] - (outline.R * addedAlpha + 127) / 255) <= 1;
        }
        private static bool SourceOverMatches(byte[] source, byte[] actual, int offset, byte[] background)
        {
            int inverse = 255 - source[offset + 3];
            for (int channel = 0; channel < 4; channel++)
                if (Math.Abs(actual[offset + channel] - source[offset + channel] - (background[channel] * inverse + 127) / 255) > 2) return false;
            return true;
        }
        private static bool[] UnderlayBounds(RawBitmap glyph, Size size, float scale, bool softShadow)
        {
            var allowed = new bool[size.Width * size.Height];
            double reach = Math.Max(1d, scale) + 1d;
            int radius = (int)Math.Ceiling(reach);
            int offsetY = softShadow ? Math.Max(1, (int)Math.Round(scale / 2f)) : 0;
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    if (glyph.Channel(new Point(x, y), 3) == 0) continue;
                    allowed[y * size.Width + x] = true;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                            if (dx * dx + dy * dy <= reach * reach)
                            {
                                int tx = x + dx, ty = y + dy + offsetY;
                                if (tx >= 0 && tx < size.Width && ty >= 0 && ty < size.Height) allowed[ty * size.Width + tx] = true;
                            }
                }
            return allowed;
        }
        private static bool CompositeGlyphsMatch(Bitmap bitmap, RawBitmap raw, RawBitmap glyph)
        {
            foreach (Color backdrop in Backdrops)
                using (Bitmap composite = Composite(bitmap, backdrop))
                {
                    RawBitmap actual = ReadPixels(composite);
                    byte[] background = { backdrop.B, backdrop.G, backdrop.R, 255 };
                    for (int offset = 0; offset < glyph.Data.Length; offset += 4)
                        if (glyph.Data[offset + 3] > 0 && !SourceOverMatches(raw.Data, actual.Data, offset, background)) return false;
                }
            return true;
        }
        private static bool CompositeSamplesMatch(Bitmap bitmap, RawBitmap raw, Point background, Point text, Color backdrop)
        {
            if (text.X < 0) return false;
            using (Bitmap composite = Composite(bitmap, backdrop))
            {
                RawBitmap actual = ReadPixels(composite);
                byte[] color = { backdrop.B, backdrop.G, backdrop.R, 255 };
                return SourceOverMatches(raw.Pixel(background), actual.Pixel(background), 0, color)
                    && SourceOverMatches(raw.Pixel(text), actual.Pixel(text), 0, color);
            }
        }
        private static Bitmap Composite(Bitmap bitmap, Color backdrop)
        {
            var result = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(backdrop);
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.DrawImageUnscaled(bitmap, Point.Empty);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
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
            public byte[] Pixel(Point point)
            { return new[] { (byte)Channel(point, 0), (byte)Channel(point, 1), (byte)Channel(point, 2), (byte)Channel(point, 3) }; }
            public bool IsPremultiplied()
            {
                for (int pixel = 0; pixel < bytes.Length; pixel += 4)
                    if (bytes[pixel] > bytes[pixel + 3] || bytes[pixel + 1] > bytes[pixel + 3] || bytes[pixel + 2] > bytes[pixel + 3]) return false;
                return true;
            }
            public Rectangle InkBounds(Rectangle bounds)
            {
                bounds = Rectangle.Intersect(bounds, new Rectangle(Point.Empty, size));
                int left = size.Width, right = -1, top = size.Height, bottom = -1;
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                    for (int x = bounds.Left; x < bounds.Right; x++)
                        if (Channel(new Point(x, y), 3) > 0)
                        { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
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
