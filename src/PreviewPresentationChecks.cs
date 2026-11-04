using System;
using System.Collections.Generic;
using System.Globalization;

namespace PersonalUsageOverlay
{
    // Explicit timestamps and in-memory time zones only. Never reads an account,
    // starts a worker, or uses the current clock to choose the expected preview.
    internal static class PreviewPresentationChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            TimeZoneInfo korea = TimeZoneInfo.CreateCustomTimeZone("preview-fixture-korea", TimeSpan.FromHours(9), "fixture", "fixture");
            TimeZoneInfo west = TimeZoneInfo.CreateCustomTimeZone("preview-fixture-west", TimeSpan.FromHours(-8), "fixture", "fixture");
            DateTimeOffset started = At("2026-11-04T10:59:37Z");
            UsagePresentation preview = UsagePresentation.CreatePreview(started, korea);
            checks["preview_uses_fixed_example_percent_and_supplied_start_clock"] = preview.MainText == "주간 96% 남음"
                && preview.DetailText == "갱신 19:59:37 · 예시";
            checks["preview_reset_is_seven_days_after_start_with_matching_calendar_countdown"] = preview.ResetDateText == "주간 초기화 11/11(수) 19:59"
                && preview.CountdownText == "D-7" && preview.ResetText == "주간 초기화 11/11(수) 19:59 · D-7";
            checks["preview_is_normal_example_without_failure_or_low_quota_state"] = !preview.HasQueryFailure
                && preview.FailureDetails == String.Empty && preview.Tone == UsageTone.Normal && !preview.LowWeeklyRemaining.HasValue;

            UsagePresentation anotherStart = UsagePresentation.CreatePreview(started.AddMinutes(17).AddSeconds(24), korea);
            checks["preview_another_execution_start_changes_example_update_and_reset_times"] = anotherStart.DetailText == "갱신 20:17:01 · 예시"
                && anotherStart.ResetDateText == "주간 초기화 11/11(수) 20:17" && anotherStart.CountdownText == "D-7";

            UsagePresentation beforeMidnight = UsagePresentation.CreatePreview(At("2026-11-04T14:59:59Z"), korea);
            UsagePresentation afterMidnight = UsagePresentation.CreatePreview(At("2026-11-04T15:00:00Z"), korea);
            UsagePresentation monthEnd = UsagePresentation.CreatePreview(At("2027-01-31T00:00:00Z"), korea);
            checks["preview_midnight_and_month_boundary_follow_actual_dates"] = beforeMidnight.DetailText == "갱신 23:59:59 · 예시"
                && beforeMidnight.ResetDateText == "주간 초기화 11/11(수) 23:59"
                && afterMidnight.DetailText == "갱신 00:00:00 · 예시" && afterMidnight.ResetDateText == "주간 초기화 11/12(목) 00:00"
                && monthEnd.ResetDateText == "주간 초기화 02/07(일) 09:00" && monthEnd.CountdownText == "D-7";

            UsagePresentation nextYear = UsagePresentation.CreatePreview(At("2026-12-28T10:59:37Z"), korea);
            checks["preview_reset_in_next_year_keeps_unambiguous_year"] = nextYear.ResetDateText == "주간 초기화 2027/01/04(월) 19:59"
                && nextYear.CountdownText == "D-7";

            DateTimeOffset yearBoundary = At("2026-12-31T16:30:25Z");
            UsagePresentation eastern = UsagePresentation.CreatePreview(yearBoundary, korea);
            UsagePresentation western = UsagePresentation.CreatePreview(yearBoundary, west);
            UsagePresentation suppliedOffset = UsagePresentation.CreatePreview(At("2026-11-04T19:59:37+05:30"), korea);
            checks["preview_converts_start_instant_and_reset_into_requested_timezone"] = eastern.DetailText == "갱신 01:30:25 · 예시"
                && eastern.ResetDateText == "주간 초기화 01/08(금) 01:30"
                && western.DetailText == "갱신 08:30:25 · 예시" && western.ResetDateText == "주간 초기화 2027/01/07(목) 08:30"
                && suppliedOffset.DetailText == "갱신 23:29:37 · 예시" && suppliedOffset.ResetDateText == "주간 초기화 11/11(수) 23:29";

            TimeZoneInfo.TransitionTime daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
            TimeZoneInfo.TransitionTime daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
            TimeZoneInfo.AdjustmentRule daylightRule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), daylightStart, daylightEnd);
            TimeZoneInfo daylight = TimeZoneInfo.CreateCustomTimeZone("preview-fixture-daylight", TimeSpan.Zero,
                "fixture", "fixture-standard", "fixture-daylight", new[] { daylightRule });
            UsagePresentation calendar = UsagePresentation.CreatePreview(At("2026-03-25T23:30:00Z"), daylight);
            checks["preview_countdown_uses_local_calendar_difference_instead_of_assuming_seven"] = calendar.DetailText == "갱신 23:30:00 · 예시"
                && calendar.ResetDateText == "주간 초기화 04/02(목) 00:30" && calendar.CountdownText == "D-8";

            checks["preview_new_example_does_not_mutate_existing_immutable_presentation"] = !Object.ReferenceEquals(preview, anotherStart)
                && preview.MainText == "주간 96% 남음" && preview.DetailText == "갱신 19:59:37 · 예시"
                && preview.ResetText == "주간 초기화 11/11(수) 19:59 · D-7" && !preview.HasQueryFailure
                && preview.FailureDetails == String.Empty && preview.Tone == UsageTone.Normal && !preview.LowWeeklyRemaining.HasValue;
        }

        private static DateTimeOffset At(string iso)
        {
            return DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);
        }
    }
}
