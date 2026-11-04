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
        }

        private static void CheckEveryGlyphPixel(Dictionary<string, bool> checks, DateTimeOffset started,
            TimeZoneInfo zone, bool highContrast)
        {
            bool formats = true, glyphs = true, opaque = true, masks = true, colors = true,
                composited = true, matte = true, backgrounds = true;
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
                            Color stateColor = presentation.Tone == UsageTone.Low ? theme.Danger
                                : presentation.Tone == UsageTone.Normal ? theme.Success : theme.Attention;
                            Color[] palette = { mainColor, stateColor, theme.Muted,
                                presentation.LowWeeklyRemaining.HasValue ? theme.DangerText : theme.Accent };
                            using (Bitmap baselineBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, baselineLayout))
                            {
                                formats &= baselineBitmap.Size == size && baselineBitmap.PixelFormat == PixelFormat.Format32bppPArgb;
                                RawBitmap baseline = ReadPixels(baselineBitmap);
                                int baselineCount = 0, antialiased = 0;
                                for (int offset = 0; offset < baseline.Data.Length; offset += 4)
                                {
                                    if (baseline.Data[offset + 3] == 0) continue;
                                    baselineCount++;
                                    bool exactForeground = false;
                                    foreach (Color foreground in palette)
                                        exactForeground |= MatchesRgb(baseline.Data, offset, foreground);
                                    if (!exactForeground && !MatchesRgb(baseline.Data, offset, theme.Surface)) antialiased++;
                                    matte &= !MatchesRgb(baseline.Data, offset, theme.Surface)
                                        && MatchesMatte(baseline.Data, offset, theme.Surface, palette);
                                }
                                var heading = new Rectangle((int)Math.Round(10 * scale), (int)Math.Round(6 * scale),
                                    (int)Math.Round(214 * scale), (int)Math.Round(25 * scale));
                                glyphs &= baselineCount > 0 && baselineCount < size.Width * size.Height
                                    && baseline.FindOpaqueText(heading, mainColor).X >= 0;
                                matte &= antialiased > 0;

                                foreach (int percent in new[] { 0, 25, 50, 100 })
                                {
                                    BadgeLayoutSettings layout = baselineLayout.Copy();
                                    layout.BackgroundTransparency = percent;
                                    using (Bitmap textBitmap = BadgeRenderer.CreateTextBitmap(size, scale, theme, presentation, layout))
                                    using (Bitmap fullBitmap = BadgeRenderer.CreateBitmap(size, scale, theme, presentation, layout))
                                    {
                                        formats &= textBitmap.Size == size && fullBitmap.Size == size
                                            && textBitmap.PixelFormat == PixelFormat.Format32bppPArgb
                                            && fullBitmap.PixelFormat == PixelFormat.Format32bppPArgb;
                                        RawBitmap text = ReadPixels(textBitmap), full = ReadPixels(fullBitmap);
                                        if (text.Data.Length != baseline.Data.Length || full.Data.Length != baseline.Data.Length)
                                        {
                                            masks = colors = composited = backgrounds = false;
                                            continue;
                                        }
                                        int count = 0;
                                        int expectedAlpha = BadgeRenderer.BackgroundAlpha(percent, highContrast);
                                        int inset = (int)Math.Ceiling(12 * scale);
                                        for (int offset = 0; offset < text.Data.Length; offset += 4)
                                        {
                                            bool baselineGlyph = baseline.Data[offset + 3] > 0;
                                            bool currentGlyph = text.Data[offset + 3] > 0;
                                            masks &= baselineGlyph == currentGlyph;
                                            if (baselineGlyph || currentGlyph)
                                                for (int channel = 0; channel < 4; channel++)
                                                    colors &= text.Data[offset + channel] == baseline.Data[offset + channel];
                                            if (currentGlyph)
                                            {
                                                count++;
                                                opaque &= text.Data[offset + 3] == 255;
                                                for (int channel = 0; channel < 4; channel++)
                                                {
                                                    composited &= full.Data[offset + channel] == text.Data[offset + channel];
                                                }
                                            }
                                            else
                                            {
                                                // The text-only layer must not retain its opaque matte.
                                                opaque &= text.Data[offset] == 0 && text.Data[offset + 1] == 0 && text.Data[offset + 2] == 0;
                                                int pixel = offset / 4, x = pixel % size.Width, y = pixel / size.Width;
                                                if (x >= inset && x < size.Width - inset && y >= inset && y < size.Height - inset)
                                                    backgrounds &= Math.Abs(full.Data[offset + 3] - expectedAlpha) <= 1;
                                                if (expectedAlpha == 0)
                                                    backgrounds &= full.Data[offset] == 0 && full.Data[offset + 1] == 0
                                                        && full.Data[offset + 2] == 0 && full.Data[offset + 3] == 0;
                                            }
                                        }
                                        masks &= count == baselineCount;
                                    }
                                }
                            }
                        }
            checks["badge_transparency_all_states_text_layers_have_expected_size_and_pargb_format"] = formats;
            checks["badge_transparency_all_states_and_sizes_keep_visible_main_glyphs_and_state_colors"] = glyphs;
            checks["badge_transparency_every_glyph_pixel_is_opaque_and_text_layer_has_no_matte_background"] = opaque;
            checks["badge_transparency_every_glyph_mask_and_pixel_count_is_invariant_at_all_percent_values"] = masks;
            checks["badge_transparency_every_glyph_rgba_including_aa_is_invariant_at_all_percent_values"] = colors;
            checks["badge_transparency_full_bitmap_matches_every_text_layer_glyph_pixel_exactly"] = composited;
            checks["badge_transparency_aa_pixels_use_opaque_surface_matte_and_are_not_discarded"] = matte;
            checks["badge_transparency_all_states_preserve_background_alpha_outside_text_mask"] = backgrounds;
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

        private static bool MatchesRgb(byte[] bytes, int offset, Color color)
        {
            return bytes[offset] == color.B && bytes[offset + 1] == color.G && bytes[offset + 2] == color.R;
        }

        private static bool MatchesMatte(byte[] bytes, int offset, Color surface, Color[] palette)
        {
            int[] actual = { bytes[offset], bytes[offset + 1], bytes[offset + 2] };
            int[] background = { surface.B, surface.G, surface.R };
            foreach (Color color in palette)
            {
                int[] foreground = { color.B, color.G, color.R };
                int channel = 0;
                for (int i = 1; i < 3; i++)
                    if (Math.Abs(foreground[i] - background[i]) > Math.Abs(foreground[channel] - background[channel])) channel = i;
                int difference = foreground[channel] - background[channel];
                if (difference == 0) continue;
                double coverage = (actual[channel] - background[channel]) / (double)difference;
                if (coverage < -0.02 || coverage > 1.02) continue;
                bool matches = true;
                for (int i = 0; i < 3; i++)
                    matches &= Math.Abs(actual[i] - background[i] - coverage * (foreground[i] - background[i])) <= 2;
                if (matches) return true;
            }
            return false;
        }

        private static void CheckBitmaps(Dictionary<string, bool> checks, VisualTheme theme, float scale,
            UsagePresentation presentation, bool highContrast)
        {
            bool format = true, backgrounds = true, corners = true, text = true, premultiplied = true, composed = true;
            foreach (bool date in new[] { true, false })
                foreach (int percent in new[] { 0, 25, 50, 100 })
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
            checks[prefix + "background_alpha_tracks_all_four_percent_values"] = backgrounds;
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
