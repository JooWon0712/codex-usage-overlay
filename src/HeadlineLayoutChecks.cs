using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class HeadlineLayoutChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            var now = DateTimeOffset.Parse("2026-11-04T19:59:37+09:00", CultureInfo.InvariantCulture);
            var longState = new UsageState { status = "stale", observedAt = now.AddMinutes(-4).ToString("o") };
            for (int index = 0; index < 5; index++) longState.windows.Add(new LimitWindow { limitId="codex",window="secondary",windowDurationMins=300,remainingPercent=99 });
            var longText = UsagePresentation.Create(longState, now, TimeZoneInfo.Utc);
            var samples = new[] { SettingsPreviewHost.CreateSample(95,"ok"), SettingsPreviewHost.CreateSample(14,"stale"), SettingsPreviewHost.CreateSample(0,"loading"), SettingsPreviewHost.CreateSample(95,"ok",false), longText };
            bool inside = true, nextToTitle = true, noOverlap = true, compact = true, clipped = true, hidden = true, shortTitle = true;
            using (var bitmap = new Bitmap(800,400))
            using (var graphics = Graphics.FromImage(bitmap))
                foreach (float scale in new[] {1f,1.5f,2f})
                    for (int mask=0;mask<4;mask++)
                        foreach (var sample in samples)
                        {
                            var layout = new BadgeLayoutSettings();
                            layout.Elements.Find(item => item.Key == "weekly-reset").Visible = (mask & 1) != 0;
                            layout.Elements.Find(item => item.Key == "countdown").Visible = (mask & 2) != 0;
                            layout.Normalize();
                            var bounds = new Rectangle(17,23,(int)Math.Round(layout.Width*scale),(int)Math.Round(layout.Height*scale));
                            var placement = BadgeRenderer.CalculateHeadline(graphics,bounds,scale,sample,layout);
                            inside &= bounds.Contains(placement.Main) && placement.Main.Width > 0 && placement.Main.Height > 0;
                            compact &= layout.Height == ((mask & 1) != 0 ? 82 : 58);
                            inside &= layout.Elements.Count == 4 && !layout.Elements.Exists(item => item.Key == "indicator")
                                && placement.Main.Left == bounds.Left + (int)Math.Round(10 * scale);
                            if ((mask & 2) == 0) { hidden &= placement.Countdown.IsEmpty; continue; }
                            inside &= bounds.Contains(placement.Countdown) && placement.Countdown.Width > 0;
                            noOverlap &= !placement.Main.IntersectsWith(placement.Countdown) && placement.Main.Top == placement.Countdown.Top && placement.Main.Height == placement.Countdown.Height;
                            nextToTitle &= placement.Countdown.Left - placement.Main.Right == (int)Math.Round(8*scale);
                            var main = layout.Elements.Find(item => item.Key == "remaining");
                            using (var font = new Font("Segoe UI",main.FontSize*scale,FontStyle.Bold,GraphicsUnit.Pixel))
                            {
                                int measured = TextRenderer.MeasureText(graphics,sample.MainText,font,new Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width;
                                clipped &= placement.Main.Width <= measured;
                                if (ReferenceEquals(sample,samples[0])) shortTitle &= placement.Main.Width == measured && placement.Countdown.Left < bounds.Left+(int)Math.Round(212*scale);
                                if (ReferenceEquals(sample,longText)) clipped &= placement.Main.Width < measured;
                            }
                        }
            checks["headline_countdown_is_eight_dips_after_visible_title_extent"] = nextToTitle;
            checks["headline_short_title_has_no_outer_edge_alignment_gap"] = shortTitle;
            checks["headline_title_and_countdown_fit_without_overlap_at_all_dpis"] = inside && noOverlap;
            checks["headline_long_or_previous_values_reserve_countdown_space"] = clipped;
            checks["headline_countdown_toggle_keeps_independent_date_height"] = compact && hidden;
        }
    }
}
