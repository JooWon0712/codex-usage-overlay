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
            bool formats = true, coverage = true, colors = true, outline = true, bounds = true,
                invariant = true, composited = true, backgrounds = true;
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
                                bool[] allowed = OutlineBounds(glyph, size, scale);
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
                                        outline &= OriginalGlyphContributionMatches(glyph.Data, baseline.Data, offset, theme.Surface);
                                    }
                                    if (!allowed[offset / 4])
                                        for (int channel = 0; channel < 4; channel++) bounds &= baseline.Data[offset + channel] == 0;
                                    if (outlinedAlpha == 0)
                                        bounds &= baseline.Data[offset] == 0 && baseline.Data[offset + 1] == 0 && baseline.Data[offset + 2] == 0;
                                }
                                coverage &= lowCoverage > 0 && highCoverage > 0;
                                outline &= partialOutline > 0;
                                var heading = new Rectangle((int)Math.Round(10 * scale), (int)Math.Round(6 * scale),
                                    (int)Math.Round(214 * scale), (int)Math.Round(25 * scale));
                                colors &= opaqueCenters > 0 && glyph.FindOpaqueText(heading, mainColor).X >= 0
                                    && baseline.FindOpaqueText(heading, mainColor).X >= 0;

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
                                        }
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
