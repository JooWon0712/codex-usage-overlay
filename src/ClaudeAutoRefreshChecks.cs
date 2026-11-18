using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    internal static class ClaudeAutoRefreshChecks
    {
        internal static void Run(Dictionary<string, bool> checks)
        {
            var first = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
            var schedule = new ClaudeRefreshSchedule();
            bool startup = schedule.TryBegin(first, DateTime.MinValue);
            schedule.Finish(first, true);
            checks["claude_auto_queries_at_start_and_every_fifteen_minutes"] = startup
                && !schedule.TryBegin(first.AddMinutes(15).AddSeconds(-1), DateTime.MinValue)
                && schedule.TryBegin(first.AddMinutes(15), DateTime.MinValue);

            schedule = new ClaudeRefreshSchedule();
            schedule.TryBegin(first, DateTime.MinValue);
            schedule.Finish(first, true);
            var afterRequest = first.AddMinutes(2);
            var secondRequest = afterRequest.AddSeconds(2);
            bool immediate = schedule.TryBegin(afterRequest, afterRequest);
            bool queued = !schedule.TryBegin(secondRequest, secondRequest);
            schedule.Finish(secondRequest, true);
            queued &= schedule.TryBegin(secondRequest.AddSeconds(1), secondRequest);
            schedule.Finish(secondRequest.AddSeconds(2), true);
            checks["claude_auto_completion_signal_bypasses_idle_delay_without_parallel_query"] = immediate && queued
                && !schedule.TryBegin(afterRequest.AddMinutes(15), secondRequest)
                && schedule.TryBegin(secondRequest.AddMinutes(15).AddSeconds(2), secondRequest);

            schedule = new ClaudeRefreshSchedule();
            schedule.TryBegin(first, DateTime.MinValue);
            schedule.Finish(first, false);
            bool backoff = schedule.TryBegin(first.AddMinutes(15), DateTime.MinValue);
            schedule.Finish(first.AddMinutes(15), false);
            checks["claude_auto_idle_retries_slow_down_after_repeated_failures"] = backoff
                && !schedule.TryBegin(first.AddMinutes(30), DateTime.MinValue)
                && schedule.TryBegin(first.AddMinutes(45), DateTime.MinValue);

            var now = new DateTimeOffset(first);
            var report = new ClaudeUsageSnapshot
            {
                attemptedAt = now.ToString("o"),
                status = "partial",
                fiveHour = new ClaudeUsageWindow { observedAt = now.ToString("o"), usedPercentage = 0, resetsAt = now.AddHours(2).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { observedAt = now.AddMinutes(-15).ToString("o"), usedPercentage = 24, resetsAt = now.AddDays(5).ToUnixTimeSeconds() }
            };
            var partial = ClaudeUsageDisplay.FromAutomatic(report, now, TimeZoneInfo.Utc);
            checks["claude_auto_partial_result_marks_previous_period_without_inventing_zero"] = partial.Status.StartsWith("일부 갱신", StringComparison.Ordinal)
                && partial.FiveHourRemaining == 100 && partial.WeeklyRemaining == 76
                && !partial.FiveHourIsPrevious && partial.WeeklyIsPrevious
                && partial.FiveHourReset == "10:00" && partial.WeeklyReset == "11월 23일 08:00"
                && partial.FiveHourCountdownText == "2h 00m" && partial.WeeklyCountdownText == "이전 D-5";
            var expired = ClaudeUsageDisplay.FromAutomatic(report, now.AddHours(3), TimeZoneInfo.Utc);
            checks["claude_auto_expired_period_waits_while_other_window_remains_available"] = !expired.FiveHourRemaining.HasValue
                && expired.FiveHour.Contains("갱신 대기") && expired.WeeklyRemaining == 76;
            report.status = "auth-required";
            var loggedOut = ClaudeUsageDisplay.FromAutomatic(report, now, TimeZoneInfo.Utc);
            checks["claude_auto_signed_out_displays_no_prior_account_usage"] = loggedOut.Status == "Claude 로그인 필요"
                && !loggedOut.FiveHourRemaining.HasValue && !loggedOut.WeeklyRemaining.HasValue;
        }
    }
}
