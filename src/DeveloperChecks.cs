using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class DeveloperChecks
    {
        public static void SelfTest(string output)
        {
            var checks = new Dictionary<string, bool>();
            var p = new Preferences();
            var normal = OverlayLayout.CalculateBounds(new Rectangle(100, 200, 1200, 800), 1, p);
            checks["sidebar_relative_to_client"] = normal == new Rectangle(164, 918, 224, 46);
            var negative = OverlayLayout.CalculateBounds(new Rectangle(-2560, -200, 2000, 1200), 1.5f, p);
            checks["mixed_dpi_negative_monitor"] = negative == new Rectangle(-2464, 877, 336, 69);
            p.Position = "title";
            p.TitleX = 3900;
            p.TitleY = 2900;
            var small = OverlayLayout.CalculateBounds(new Rectangle(-900, 0, 600, 400), 2, p);
            checks["title_clamped_inside_window"] = small == new Rectangle(-748, 308, 448, 92);
            var tiny = OverlayLayout.CalculateBounds(new Rectangle(0, 0, 100, 30), 1, p);
            checks["tiny_window_stays_inside"] = tiny == new Rectangle(0, 0, 100, 30);
            RuntimeResolverChecks.Run(checks);
            var now = DateTimeOffset.Parse("2020-01-01T00:03:00Z", CultureInfo.InvariantCulture);
            var quota = new UsageState
            {
                status = "ok",
                observedAt = now.ToString("o")
            };
            var weekly = new LimitWindow
            {
                limitId = "codex",
                window = "primary",
                windowDurationMins = 10080,
                remainingPercent = 14
            };
            quota.windows.Add(weekly);
            checks["weekly_fourteen_percent_warns"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.remainingPercent = 15;
            checks["exactly_fifteen_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.remainingPercent = 15.001;
            checks["above_fifteen_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.remainingPercent = 14.999;
            checks["unrounded_value_controls_warning"] = WeeklyQuotaWarning.Remaining(quota, now) == 14.999 && weekly.remainingPercent.ToString("0.#", CultureInfo.InvariantCulture) == "15";
            weekly.remainingPercent = 0;
            checks["zero_is_exhausted_not_unavailable"] = WeeklyQuotaWarning.Remaining(quota, now) == 0;
            weekly.remainingPercent = 14;
            weekly.window = "secondary";
            checks["weekly_secondary_is_supported"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.windowDurationMins = 300;
            checks["five_hour_is_not_weekly_warning"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.windowDurationMins = null;
            checks["missing_period_is_not_guessed"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.windowDurationMins = 10080;
            weekly.limitId = "other";
            checks["other_account_bucket_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.limitId = "codex";
            foreach (var status in new[] { "loading", "stale", "unavailable" })
            {
                quota.status = status;
                checks["no_live_warning_when_" + status] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            }

            quota.status = "ok";
            quota.observedAt = now.AddMinutes(-3).ToString("o");
            checks["three_minute_boundary_is_stale"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.AddMinutes(-3).AddMilliseconds(1).ToString("o");
            checks["fresh_last_millisecond_can_warn"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            quota.observedAt = null;
            checks["missing_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = "invalid";
            checks["invalid_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.AddMinutes(1).ToString("o");
            checks["future_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.ToString("o");
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 101 })
            {
                weekly.remainingPercent = value;
                checks["invalid_remaining_" + value.ToString(CultureInfo.InvariantCulture)] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            }

            weekly.remainingPercent = 14;
            checks["successful_recovery_restores_warning"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.remainingPercent = 80;
            checks["quota_recovery_clears_warning"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            bool passed = true;
            foreach (bool result in checks.Values)
                passed &= result;
            var text = new JavaScriptSerializer().Serialize(new { passed = passed, checks = checks });
            if (output != null)
                File.WriteAllText(output, text, new UTF8Encoding(false));
            Environment.ExitCode = passed ? 0 : 1;
        }

        public static void RenderSamples(string directory)
        {
            // Developer-only visual verification: no tray, worker, account read or settings writes.
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var samples = new[]
            {
                new
                {
                    Name = "normal",
                    Main = "주간 75% 남음",
                    Detail = "갱신 12:34:56 · Codex 계정",
                    Low = (double?)null,
                    Color = Color.FromArgb(127, 199, 174)
                },
                new
                {
                    Name = "weekly-low",
                    Main = "주간 14% 남음",
                    Detail = "주간 한도 부족 · 갱신 12:34:56",
                    Low = (double?)14,
                    Color = Color.FromArgb(242, 100, 112)
                },
                new
                {
                    Name = "weekly-exhausted",
                    Main = "주간 0% 남음",
                    Detail = "주간 한도 소진 · 갱신 12:34:56",
                    Low = (double?)0,
                    Color = Color.FromArgb(242, 100, 112)
                },
                new
                {
                    Name = "stale",
                    Main = "이전 값 · 주간 14% 남음",
                    Detail = "갱신 실패 · 마지막 12:34:56",
                    Low = (double?)null,
                    Color = Color.FromArgb(238, 183, 98)
                }
            };
            foreach (var sample in samples)
                foreach (var scale in new[] { 1f, 1.5f, 2f })
                    using (var badge = new Badge
                    {
                        ClientSize = new Size((int)(OverlayLayout.BadgeWidth * scale), (int)(OverlayLayout.BadgeHeight * scale)),
                        ScaleFactor = scale,
                        MainText = sample.Main,
                        DetailText = sample.Detail,
                        LowWeeklyRemaining = sample.Low,
                        StateColor = sample.Color
                    })
                    using (var bitmap = new Bitmap(badge.Width, badge.Height))
                    {
                        badge.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(directory, sample.Name + "-" + (int)(scale * 100) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
        }
    }
}
