using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace PersonalUsageOverlay
{
    internal static class ClaudeUsageChecks
    {
        private const string ObservedAt = "\"observedAt\":\"2026-01-01T02:30:00Z\"";
        private const string BothWindows = "{" + ObservedAt + ",\"fiveHour\":{\"usedPercentage\":0,\"resetsAt\":1767240000},"
            + "\"weekly\":{\"usedPercentage\":62.5,\"resetsAt\":1767580200}}";

        public static void Run(Dictionary<string, bool> checks)
        {
            var observed = new DateTimeOffset(2026, 1, 1, 2, 30, 0, TimeSpan.Zero);
            var now = observed.AddMinutes(1);
            ReadFixtures(checks, now);
            CheckPreviewAndLayout(checks, observed);
        }

        private static void ReadFixtures(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CodexUsageOverlay-claude-check-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "claude-usage.json");
            var reader = new ClaudeUsageReader(path);
            try
            {
                checks["claude_missing_directory_waits_for_usage"] = reader.Read(now).HasSameDisplayAs(ClaudeUsageDisplay.Awaiting);
                Directory.CreateDirectory(directory);
                checks["claude_missing_file_waits_for_usage"] = reader.Read(now.AddSeconds(2)).HasSameDisplayAs(ClaudeUsageDisplay.Awaiting);

                File.WriteAllText(path, BothWindows);
                var both = reader.Read(now.AddSeconds(4));
                checks["claude_reads_both_windows_in_local_time"] = both.Status == "마지막 수신 " + LocalTime(new DateTimeOffset(2026, 1, 1, 2, 30, 0, TimeSpan.Zero))
                    && both.FiveHour == "5시간 100% 남음 · 초기화 " + ResetTime(DateTimeOffset.FromUnixTimeSeconds(1767240000))
                    && both.Weekly == "주간 37.5% 남음 · 초기화 " + ResetTime(DateTimeOffset.FromUnixTimeSeconds(1767580200));

                // Expiry must take effect even inside the file polling interval.
                var reset = DateTimeOffset.FromUnixTimeSeconds(1767240000);
                var before = reader.Read(reset.AddSeconds(-1));
                var expired = reader.Read(reset);
                checks["claude_expired_limit_waits_for_update_without_expiring_the_other_window"] = before.HasSameDisplayAs(both)
                    && expired.Status == both.Status && expired.FiveHour == "5시간 · 초기화 후 갱신 대기"
                    && expired.Weekly == both.Weekly;

                File.WriteAllText(path, "{" + ObservedAt + ",\"fiveHour\":null,\"weekly\":{\"usedPercentage\":100,\"resetsAt\":null}}");
                var partial = reader.Read(reset.AddSeconds(1));
                checks["claude_missing_window_preserves_zero_remaining_without_inventing_a_reset"] = partial.Status == both.Status
                    && partial.FiveHour == "5시간 · 확인 불가" && partial.Weekly == "주간 0% 남음";

                File.WriteAllText(path, "{" + ObservedAt + ",\"fiveHour\":{\"usedPercentage\":101},\"weekly\":{\"usedPercentage\":18,\"resetsAt\":253402300800}}");
                var invalidWindow = reader.Read(reset.AddSeconds(3));
                checks["claude_invalid_percent_and_out_of_range_reset_are_handled_independently"] = invalidWindow.Status == both.Status
                    && invalidWindow.FiveHour == "5시간 · 확인 불가" && invalidWindow.Weekly == "주간 82% 남음";

                File.WriteAllText(path, "{\"observedAt\":\"2026-01-02T02:30:00Z\",\"fiveHour\":{\"usedPercentage\":0}}");
                checks["claude_future_receipt_does_not_show_quota"] = reader.Read(reset.AddSeconds(5)).HasSameDisplayAs(ClaudeUsageDisplay.Unreadable);

                File.WriteAllText(path, "{\"observedAt\":\"invalid\",\"fiveHour\":{\"usedPercentage\":0}}");
                checks["claude_invalid_receipt_does_not_show_quota"] = reader.Read(reset.AddSeconds(7)).HasSameDisplayAs(ClaudeUsageDisplay.Unreadable);

                File.WriteAllText(path, "{");
                checks["claude_invalid_json_is_unreadable"] = reader.Read(reset.AddSeconds(9)).HasSameDisplayAs(ClaudeUsageDisplay.Unreadable);

                File.WriteAllText(path, BothWindows + new string(' ', 4096));
                checks["claude_oversized_json_is_unreadable"] = reader.Read(reset.AddSeconds(11)).HasSameDisplayAs(ClaudeUsageDisplay.Unreadable);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    File.Delete(path);
                    Directory.Delete(directory, false);
                }
            }
        }

        private static void CheckPreviewAndLayout(Dictionary<string, bool> checks, DateTimeOffset startedAt)
        {
            var preview = UsagePresentation.CreatePreview(startedAt).Claude;
            checks["claude_preview_uses_launch_time_and_sample_windows"] = preview != null
                && preview.Status == "예시 · " + LocalTime(startedAt)
                && preview.FiveHour == "5시간 76% 남음 · 초기화 " + ResetTime(startedAt.AddHours(3))
                && preview.Weekly == "주간 59% 남음 · 초기화 " + ResetTime(startedAt.AddDays(4))
                && preview.HasSameDisplayAs(UsagePresentation.CreatePreview(startedAt).Claude);

            var json = new JavaScriptSerializer();
            var layout = json.Deserialize<BadgeLayoutSettings>("{}");
            layout.Normalize();
            checks["claude_layout_opt_in_for_old_settings"] = !layout.ShowClaude && layout.Height == 82 && layout.Elements.Count == 4;

            layout.ShowClaude = true;
            var saved = json.Deserialize<BadgeLayoutSettings>(json.Serialize(layout.Copy()));
            saved.Normalize();
            checks["claude_layout_toggle_round_trips_and_keeps_three_rows_in_bounds"] = saved.ShowClaude
                && saved.Height == 146 && saved.Elements.Count == 7
                && ClaudeRowsFit(saved, 82);

            saved.Elements.Find(element => element.Key == "weekly-reset").Visible = false;
            saved.Normalize();
            checks["claude_layout_follows_codex_rows_when_the_reset_date_is_hidden"] = saved.Height == 122 && ClaudeRowsFit(saved, 58);

            saved.ShowClaude = false;
            saved.Normalize();
            checks["claude_layout_toggle_off_restores_codex_only_rows"] = saved.Height == 58 && saved.Elements.Count == 4;
        }

        private static string LocalTime(DateTimeOffset value)
        {
            return value.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture);
        }

        private static string ResetTime(DateTimeOffset value)
        {
            var local = value.ToLocalTime();
            return String.Format(CultureInfo.InvariantCulture, "{0}월 {1}일 {2:HH:mm}", local.Month, local.Day, local);
        }

        private static bool ClaudeRowsFit(BadgeLayoutSettings layout, int codexHeight)
        {
            var heading = layout.Elements.Find(element => element.Key == "claude-heading");
            var fiveHour = layout.Elements.Find(element => element.Key == "claude-five-hour");
            var weekly = layout.Elements.Find(element => element.Key == "claude-weekly");
            return heading != null && fiveHour != null && weekly != null
                && heading.Visible && fiveHour.Visible && weekly.Visible
                && heading.Y >= codexHeight && heading.Y + BadgeLayoutSettings.ElementHeight(heading) <= fiveHour.Y
                && fiveHour.Y + BadgeLayoutSettings.ElementHeight(fiveHour) <= weekly.Y
                && weekly.Y + BadgeLayoutSettings.ElementHeight(weekly) <= layout.Height;
        }
    }
}
