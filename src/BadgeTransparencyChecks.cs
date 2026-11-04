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
