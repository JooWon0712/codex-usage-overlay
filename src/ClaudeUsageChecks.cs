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
            CheckCompactStatus(checks, now);
            CheckSubscriptionPlan(checks, now);
            CheckCountdowns(checks);
            CheckMissingReset(checks, now);
            CheckPreviewAndLayout(checks, observed);
        }

        private static void CheckCompactStatus(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                status = "missing-cli",
                attemptedAt = now.ToString("o"),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, observedAt = now.AddMinutes(-3).ToString("o"), resetsAt = now.AddHours(3).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, observedAt = now.ToString("o"), resetsAt = now.AddDays(4).ToUnixTimeSeconds() }
            };
            var missing = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_compact_missing_cli_explains_unavailable_quotas"] = missing.CompactStatus == "CLI 확인 불가"
                && !missing.FiveHourRemaining.HasValue && !missing.WeeklyRemaining.HasValue
                && missing.FiveHourCompactReset == null && missing.WeeklyCompactReset == null;
            snapshot.status = "auth-required";
            var signedOut = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_compact_requires_login_without_retaining_signed_out_values"] = signedOut.CompactStatus == "로그인 필요"
                && !signedOut.FiveHourRemaining.HasValue && !signedOut.WeeklyRemaining.HasValue
                && signedOut.FiveHourCompactReset == null && signedOut.WeeklyCompactReset == null;
            snapshot.status = "partial";
            var partial = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_compact_marks_only_the_retained_quota_as_previous"] = partial.CompactStatus == null
                && partial.FiveHourRemaining == 76 && partial.WeeklyRemaining == 59
                && partial.FiveHourCompactAmount == "76% · 이전" && partial.WeeklyCompactAmount == "59%";
            checks["claude_compact_uses_the_matching_reset_for_each_period"] =
                partial.FiveHourCompactReset == "05:31" && partial.WeeklyCompactReset == "1월 5일 02:31";
            checks["claude_countdown_marks_only_the_retained_window_as_previous"] =
                partial.FiveHourCountdownText == "이전 3h 00m" && partial.WeeklyCountdownText == "D-4";
            snapshot.weekly.observedAt = snapshot.fiveHour.observedAt;
            checks["claude_countdown_marks_retained_weekly_window_as_previous"] =
                ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc).WeeklyCountdownText == "이전 D-4";
            snapshot.weekly.observedAt = snapshot.attemptedAt;
            snapshot.fiveHour.resetsAt = now.ToUnixTimeSeconds();
            var elapsed = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_compact_omits_only_the_elapsed_reset_when_the_other_is_current"] =
                elapsed.FiveHourCompactReset == null && elapsed.WeeklyCompactReset == partial.WeeklyCompactReset
                && !elapsed.FiveHourRemaining.HasValue && elapsed.WeeklyRemaining == partial.WeeklyRemaining;
        }

        private static void CheckSubscriptionPlan(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            string timestamp = now.ToString("o", CultureInfo.InvariantCulture);
            var snapshot = new ClaudeUsageSnapshot
            {
                status = "ok",
                observedAt = timestamp,
                attemptedAt = timestamp,
                subscriptionType = "pro",
                fiveHour = new ClaudeUsageWindow { usedPercentage = 23, observedAt = timestamp },
                weekly = new ClaudeUsageWindow { usedPercentage = 32, observedAt = timestamp }
            };
            var pro = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            snapshot.subscriptionType = "max";
            var max = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_automatic_subscription_uses_verified_display_names_and_changes_the_badge"] =
                pro.PlanText == "Pro" && max.PlanText == "Max" && !max.HasSameDisplayAs(pro);

            snapshot.status = "partial";
            snapshot.fiveHour = null;
            checks["claude_partial_refresh_can_show_confirmed_plan_with_the_available_window"] =
                ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc).PlanText == "Max";

            snapshot.status = "unavailable";
            snapshot.weekly = null;
            var unavailable = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_usage_failure_can_show_a_plan_confirmed_by_the_current_authentication"] =
                unavailable.PlanText == "Max" && unavailable.CompactStatus == "갱신 실패"
                && !unavailable.FiveHourRemaining.HasValue && !unavailable.WeeklyRemaining.HasValue;

            bool cleared = true;
            foreach (string status in new[] { "auth-required", "missing-cli" })
            {
                snapshot.status = status;
                cleared &= ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc).PlanText == null;
            }
            checks["claude_unverified_authentication_and_bridge_never_show_a_subscription_plan"] = cleared
                && ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc).PlanText == null;

            snapshot.status = "partial";
            snapshot.weekly = new ClaudeUsageWindow { usedPercentage = 32, observedAt = timestamp };
            snapshot.subscriptionType = "unrecognized-plan";
            var unknown = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            snapshot.subscriptionType = null;
            var absent = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_absent_or_unknown_subscription_is_ignored_without_hiding_usage"] =
                unknown.PlanText == null && absent.PlanText == null && absent.HasSameDisplayAs(unknown)
                && unknown.WeeklyCompactAmount == "68%";
        }

        private static void CheckCountdowns(Dictionary<string, bool> checks)
        {
            var now = new DateTimeOffset(2026, 1, 1, 14, 40, 0, TimeSpan.Zero);
            var fiveHourReset = now.AddMinutes(20);
            var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+09:00", TimeSpan.FromHours(9), "UTC+09:00", "UTC+09:00");
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = now.ToString("o", CultureInfo.InvariantCulture),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, resetsAt = fiveHourReset.ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, resetsAt = fiveHourReset.AddDays(3).ToUnixTimeSeconds() }
            };
            var initial = ClaudeUsageDisplay.FromSnapshot(snapshot, now, zone);
            var nextMinute = ClaudeUsageDisplay.FromSnapshot(snapshot, now.AddMinutes(1), zone);
            checks["claude_detailed_five_hour_reset_uses_date_only_when_the_local_day_changes"] =
                initial.FiveHourReset == "1월 2일 00:00"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc).FiveHourReset == "15:00";
            checks["claude_detailed_weekly_reset_keeps_the_local_date_even_on_reset_day"] =
                initial.WeeklyReset == "1월 5일 00:00"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, fiveHourReset.AddDays(3).AddMinutes(-1), TimeZoneInfo.Utc).WeeklyReset == "1월 4일 15:00";
            checks["claude_countdown_shows_zero_hours_and_refreshes_at_a_minute_change"] =
                initial.FiveHourCountdownText == "0h 20m" && nextMinute.FiveHourCountdownText == "0h 19m"
                && !nextMinute.HasSameDisplayAs(initial);
            checks["claude_countdown_rounds_up_until_five_hour_expiry"] =
                ClaudeUsageDisplay.FromSnapshot(snapshot, fiveHourReset.AddSeconds(-61), zone).FiveHourCountdownText == "0h 02m"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, fiveHourReset.AddSeconds(-60), zone).FiveHourCountdownText == "0h 01m"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, fiveHourReset.AddTicks(-1), zone).FiveHourCountdownText == "0h 01m"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, fiveHourReset, zone).FiveHourCountdownText == null;
            checks["claude_weekly_countdown_follows_the_supplied_local_calendar"] = initial.WeeklyCountdownText == "D-4"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Utc).WeeklyCountdownText == "D-3";
            var weeklyReset = fiveHourReset.AddDays(3);
            var beforeMidnight = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddMinutes(-1), zone);
            // The reset is at midnight in UTC+09:00, so it is still tomorrow right before expiry.
            checks["claude_weekly_countdown_ends_on_the_local_reset_day"] = beforeMidnight.WeeklyCountdownText == "D-1"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddMinutes(-1), TimeZoneInfo.Utc).WeeklyCountdownText == "D-DAY · 0h 01m"
                && ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset, zone).WeeklyCountdownText == null;

            var fourHoursLeft = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddHours(-4), TimeZoneInfo.Utc);
            var tenMinutesLeft = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddMinutes(-10), TimeZoneInfo.Utc);
            var sameMinute = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddMinutes(-10).AddSeconds(1), TimeZoneInfo.Utc);
            var nextWeeklyMinute = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddMinutes(-9), TimeZoneInfo.Utc);
            var justBeforeReset = ClaudeUsageDisplay.FromSnapshot(snapshot, weeklyReset.AddTicks(-1), TimeZoneInfo.Utc);
            checks["claude_weekly_same_day_countdown_updates_at_displayed_minute_boundaries"] =
                fourHoursLeft.WeeklyCountdownText == "D-DAY · 4h 00m" && tenMinutesLeft.WeeklyCountdownText == "D-DAY · 0h 10m"
                && sameMinute.HasSameDisplayAs(tenMinutesLeft) && nextWeeklyMinute.WeeklyCountdownText == "D-DAY · 0h 09m"
                && !nextWeeklyMinute.HasSameDisplayAs(tenMinutesLeft) && justBeforeReset.WeeklyCountdownText == "D-DAY · 0h 01m"
                && fourHoursLeft.WeeklyCompactReset == "15:00" && nextWeeklyMinute.WeeklyCompactReset == "15:00";

            snapshot.status = "partial";
            snapshot.attemptedAt = weeklyReset.AddMinutes(-10).ToString("o", CultureInfo.InvariantCulture);
            snapshot.weekly.observedAt = weeklyReset.AddMinutes(-11).ToString("o", CultureInfo.InvariantCulture);
            var previous = ClaudeUsageDisplay.FromAutomatic(snapshot, weeklyReset.AddMinutes(-10), TimeZoneInfo.Utc);
            checks["claude_weekly_same_day_countdown_keeps_its_previous_marker"] =
                previous.WeeklyIsPrevious && previous.WeeklyCountdownText == "이전 D-DAY · 0h 10m"
                && previous.WeeklyCompactReset == "15:00";

            var evening = new DateTimeOffset(2026, 11, 18, 11, 32, 0, TimeSpan.Zero);
            snapshot.observedAt = evening.ToString("o", CultureInfo.InvariantCulture);
            snapshot.fiveHour.resetsAt = evening.AddHours(5).ToUnixTimeSeconds();
            var overnight = ClaudeUsageDisplay.FromSnapshot(snapshot, evening, zone);
            checks["claude_five_hour_countdown_preserves_date_and_hours_when_the_reset_crosses_midnight"] =
                overnight.FiveHourReset == "11월 19일 01:32" && overnight.FiveHourCountdownText == "5h 00m"
                && overnight.FiveHourCompactReset == "11월 19일 01:32";
        }

        private static void CheckMissingReset(Dictionary<string, bool> checks, DateTimeOffset now)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                status = "ok",
                attemptedAt = now.ToString("o", CultureInfo.InvariantCulture),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 0, observedAt = now.ToString("o", CultureInfo.InvariantCulture), resetsAt = null },
                weekly = new ClaudeUsageWindow { usedPercentage = 9, observedAt = now.ToString("o", CultureInfo.InvariantCulture), resetsAt = now.AddDays(4).ToUnixTimeSeconds() }
            };
            var display = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
            checks["claude_auto_valid_full_five_hour_balance_does_not_require_a_reset_time"] =
                display.FiveHourRemaining == 100 && display.FiveHourReset == null && display.FiveHourCountdownText == null
                && !display.FiveHourIsPrevious && display.FiveHourCompactAmount == "100%" && display.FiveHourCompactReset == null
                && display.WeeklyRemaining == 91 && display.WeeklyReset == "1월 5일 02:31" && display.WeeklyCountdownText == "D-4";
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
                checks["claude_reads_both_windows_in_local_time"] = both.Status == "마지막 수신 " + ResetTime(new DateTimeOffset(2026, 1, 1, 2, 30, 0, TimeSpan.Zero))
                    && both.FiveHour == "5시간 100% 남음 · 초기화 " + ResetTime(DateTimeOffset.FromUnixTimeSeconds(1767240000))
                    && both.Weekly == "주간 37.5% 남음 · 초기화 " + ResetTime(DateTimeOffset.FromUnixTimeSeconds(1767580200));

                // Expiry must take effect even inside the file polling interval.
                var reset = DateTimeOffset.FromUnixTimeSeconds(1767240000);
                var before = reader.Read(reset.AddSeconds(-1));
                var expired = reader.Read(reset);
                DateTimeOffset localReset = TimeZoneInfo.ConvertTime(reset, TimeZoneInfo.Local);
                DateTimeOffset localBefore = TimeZoneInfo.ConvertTime(reset.AddSeconds(-1), TimeZoneInfo.Local);
                string compactTimeFormat = localBefore.Date == localReset.Date ? "HH:mm" : "M'월' d'일' HH:mm";
                checks["claude_expired_limit_waits_for_update_without_expiring_the_other_window"] = before.Status == both.Status
                    && before.FiveHour == both.FiveHour && before.Weekly == both.Weekly
                    && before.FiveHourCompactReset == localReset.ToString(compactTimeFormat, CultureInfo.InvariantCulture)
                    && expired.Status == both.Status && expired.FiveHour == "5시간 · 초기화 후 갱신 대기"
                    && expired.FiveHourCompactReset == null && expired.Weekly == both.Weekly;

                File.WriteAllText(path, "{" + ObservedAt + ",\"fiveHour\":null,\"weekly\":{\"usedPercentage\":100,\"resetsAt\":null}}");
                var partial = reader.Read(reset.AddSeconds(1));
                checks["claude_missing_window_preserves_zero_remaining_without_inventing_a_reset"] = partial.Status == both.Status
                    && partial.FiveHour == "5시간 · 확인 불가" && partial.Weekly == "주간 0% 남음"
                    && partial.FiveHourCountdownText == null && partial.WeeklyCountdownText == null;

                File.WriteAllText(path, "{" + ObservedAt + ",\"fiveHour\":{\"usedPercentage\":101},\"weekly\":{\"usedPercentage\":18,\"resetsAt\":253402300800}}");
                var invalidWindow = reader.Read(reset.AddSeconds(3));
                checks["claude_invalid_percent_and_out_of_range_reset_are_handled_independently"] = invalidWindow.Status == both.Status
                    && invalidWindow.FiveHour == "5시간 · 확인 불가" && invalidWindow.Weekly == "주간 82% 남음"
                    && invalidWindow.FiveHourReset == null && invalidWindow.WeeklyReset == null
                    && invalidWindow.FiveHourCountdownText == null && invalidWindow.WeeklyCountdownText == null;

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
                && preview.Status == "예시 · " + ResetTime(startedAt)
                && preview.PlanText == "Pro"
                && preview.FiveHour == "5시간 76% 남음 · 초기화 " + ResetTime(startedAt.AddHours(3))
                && preview.Weekly == "주간 59% 남음 · 초기화 " + ResetTime(startedAt.AddDays(4))
                && preview.FiveHourCountdownText == "3h 00m" && preview.WeeklyCountdownText != null
                && preview.HasSameDisplayAs(UsagePresentation.CreatePreview(startedAt).Claude);

            var json = new JavaScriptSerializer();
            var layout = json.Deserialize<BadgeLayoutSettings>("{}");
            layout.Normalize();
            checks["claude_layout_opt_in_for_old_settings"] = !layout.ShowClaude && layout.Height == 78 && layout.Elements.Count == 4;

            layout.ShowClaude = true;
            layout.Width = 480;
            layout.Height = 241;
            var saved = json.Deserialize<BadgeLayoutSettings>(json.Serialize(layout.Copy()));
            saved.Normalize();
            checks["claude_layout_toggle_round_trips_and_replaces_old_width_with_stacked_rows"] = saved.ShowClaude
                && saved.Width == 320 && saved.Height == 209 && saved.Elements.Count == 7
                && ClaudeRowsFit(saved);

            saved.Elements.Find(element => element.Key == "weekly-reset").Visible = false;
            saved.Normalize();
            checks["claude_layout_keeps_its_rows_in_place_when_codex_reset_is_hidden"] = !saved.ShowCodexReset
                && saved.Height == 209 && ClaudeRowsFit(saved);

            saved.ShowClaude = false;
            saved.Normalize();
            checks["claude_layout_toggle_off_restores_codex_only_rows"] = saved.Height == 78 && saved.Elements.Count == 4;
        }

        private static string ResetTime(DateTimeOffset value)
        {
            var local = value.ToLocalTime();
            return String.Format(CultureInfo.InvariantCulture, "{0}월 {1}일 {2:HH:mm}", local.Month, local.Day, local);
        }

        private static bool ClaudeRowsFit(BadgeLayoutSettings layout)
        {
            var heading = layout.Elements.Find(element => element.Key == "claude-heading");
            var fiveHour = layout.Elements.Find(element => element.Key == "claude-five-hour");
            var weekly = layout.Elements.Find(element => element.Key == "claude-weekly");
            int codexHeight = layout.CodexHeight;
            return heading != null && heading.Visible && heading.X == 16 && heading.Width == 288
                && heading.Y == codexHeight + 7 && heading.Y + BadgeLayoutSettings.ElementHeight(heading) <= codexHeight + 32
                && ClaudePeriodFits(layout, fiveHour, codexHeight)
                && ClaudePeriodFits(layout, weekly, codexHeight + 45)
                && codexHeight + 60 + layout.MeterHeight < weekly.Y;
        }

        private static bool ClaudePeriodFits(BadgeLayoutSettings layout, BadgeElement usage, int periodTop)
        {
            return usage != null && usage.Visible && usage.X == 16 && usage.Width == 288
                && usage.Y == periodTop + 32
                && usage.Y + BadgeLayoutSettings.ElementHeight(usage) < periodTop + 60
                && periodTop + 60 + layout.MeterHeight < layout.Height;
        }
    }
}
