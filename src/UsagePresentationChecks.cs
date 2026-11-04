using System;
using System.Collections.Generic;
using System.Globalization;

namespace PersonalUsageOverlay
{
    internal static class UsagePresentationChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            TimeZoneInfo korea = TimeZoneInfo.CreateCustomTimeZone("fixture-korea", TimeSpan.FromHours(9), "fixture-korea", "fixture-korea");
            TimeZoneInfo west = TimeZoneInfo.CreateCustomTimeZone("fixture-west", TimeSpan.FromHours(-8), "fixture-west", "fixture-west");
            DateTimeOffset now = At("2026-11-04T00:00:00Z");
            DateTimeOffset reset = At("2026-11-10T03:00:00Z");
            UsageState state = State(now, reset.ToUnixTimeSeconds());
            UsagePresentation display = UsagePresentation.Create(state, now, korea);
            checks["presentation_weekly_actual_korean_reset_date_and_calendar_d_day"] = display.ResetText == "주간 초기화 11/10(화) 12:00 · D-6";
            checks["presentation_reset_date_and_countdown_are_independent_elements"] = display.ResetDateText == "주간 초기화 11/10(화) 12:00"
                && display.CountdownText == "D-6" && display.ResetText == display.ResetDateText + " · " + display.CountdownText;
            checks["presentation_update_clock_is_separate_from_reset_date"] = display.DetailText == "갱신 09:00:00 · Codex 계정"
                && display.Details.Contains(display.DetailText) && display.Details.Contains(display.ResetText)
                && display.Details.IndexOf("주간 초기화", StringComparison.Ordinal) == display.Details.LastIndexOf("주간 초기화", StringComparison.Ordinal);

            DateTimeOffset midnightNow = At("2026-11-04T14:59:00Z");
            UsageState midnight = State(midnightNow, At("2026-11-04T15:01:00Z").ToUnixTimeSeconds());
            checks["presentation_crossing_local_midnight_is_one_day_even_after_two_minutes"] = UsagePresentation.Create(midnight, midnightNow, korea).ResetText.EndsWith("D-1", StringComparison.Ordinal);
            checks["presentation_same_instants_use_selected_timezone_calendar"] = UsagePresentation.Create(midnight, midnightNow, TimeZoneInfo.Utc).ResetText.EndsWith("D-DAY", StringComparison.Ordinal)
                && UsagePresentation.Create(midnight, midnightNow, west).ResetText.EndsWith("D-DAY", StringComparison.Ordinal);
            UsageState today = State(now, At("2026-11-04T14:00:00Z").ToUnixTimeSeconds());
            checks["presentation_future_reset_on_same_date_is_d_day"] = UsagePresentation.Create(today, now, korea).ResetText == "주간 초기화 11/04(수) 23:00 · D-DAY";
            checks["presentation_same_day_countdown_is_separate_d_day_element"] = UsagePresentation.Create(today, now, korea).CountdownText == "D-DAY";

            DateTimeOffset leapNow = At("2028-02-28T03:00:00Z");
            UsageState leap = State(leapNow, At("2028-02-29T03:00:00Z").ToUnixTimeSeconds());
            checks["presentation_leap_day_uses_real_calendar_date"] = UsagePresentation.Create(leap, leapNow, korea).ResetText == "주간 초기화 02/29(화) 12:00 · D-1";
            DateTimeOffset yearNow = At("2026-12-31T14:00:00Z");
            UsageState year = State(yearNow, At("2026-12-31T16:00:00Z").ToUnixTimeSeconds());
            checks["presentation_reset_in_another_year_includes_year"] = UsagePresentation.Create(year, yearNow, korea).ResetText == "주간 초기화 2027/01/01(금) 01:00 · D-1";

            state.windows[0].window = "secondary";
            state.windows.Add(Window("primary", QuotaPeriods.FiveHourMinutes, now.AddHours(4).ToUnixTimeSeconds()));
            UsagePresentation secondary = UsagePresentation.Create(state, now, korea);
            checks["presentation_weekly_secondary_is_independent_of_five_hour_reset"] = secondary.ResetText == display.ResetText
                && secondary.Details.Contains("5시간 초기화: 11/04 13:00:00 +09:00");
            state.windows.Add(Window("primary", QuotaPeriods.WeeklyMinutes, reset.ToUnixTimeSeconds()));
            checks["presentation_matching_weekly_duplicate_schedules_are_unambiguous"] = UsagePresentation.Create(state, now, korea).ResetText == display.ResetText;
            state.windows[2].resetsAt = reset.AddSeconds(1).ToUnixTimeSeconds();
            checks["presentation_conflicting_weekly_schedules_are_not_selected_arbitrarily"] = UsagePresentation.Create(state, now, korea).ResetText == "주간 초기화 확인 불가";
            UsagePresentation conflict = UsagePresentation.Create(state, now, korea);
            checks["presentation_unknown_reset_has_no_unqualified_countdown"] = conflict.ResetDateText == "주간 초기화 확인 불가" && conflict.CountdownText == "확인 불가";
            state.windows[2].resetsAt = null;
            checks["presentation_missing_weekly_schedule_cannot_be_replaced_by_other_valid_entry"] = UsagePresentation.Create(state, now, korea).ResetText == "주간 초기화 확인 불가";

            UsageState onlyFiveHour = State(now, reset.ToUnixTimeSeconds());
            onlyFiveHour.windows[0].windowDurationMins = QuotaPeriods.FiveHourMinutes;
            UsageState unknownPeriod = State(now, reset.ToUnixTimeSeconds());
            unknownPeriod.windows[0].windowDurationMins = null;
            UsageState otherBucket = State(now, reset.ToUnixTimeSeconds());
            otherBucket.windows[0].limitId = "other";
            checks["presentation_weekly_schedule_is_never_inferred_from_five_hour_unknown_or_other_bucket"] = UsagePresentation.Create(onlyFiveHour, now, korea).ResetText == "주간 초기화 확인 불가"
                && UsagePresentation.Create(unknownPeriod, now, korea).ResetText == "주간 초기화 확인 불가"
                && UsagePresentation.Create(otherBucket, now, korea).ResetText == "주간 초기화 확인 불가";

            bool invalidSafe = true;
            foreach (double? value in new double?[] { null, Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, Double.MaxValue, -62135596801d, 253402300800d })
            {
                UsageState invalid = State(now, value);
                invalid.windows.Add(Window("secondary", QuotaPeriods.FiveHourMinutes, value));
                UsagePresentation unavailable = UsagePresentation.Create(invalid, now, korea);
                invalidSafe &= unavailable.ResetText == "주간 초기화 확인 불가" && !unavailable.Details.Contains("5시간 초기화:");
            }
            checks["presentation_nonfinite_missing_and_out_of_range_timestamps_are_safe"] = invalidSafe;
            checks["presentation_local_conversion_boundaries_do_not_clamp_or_throw"] = UsagePresentation.Create(State(now, 253402300799d), now, korea).ResetText == "주간 초기화 확인 불가"
                && UsagePresentation.Create(State(now, -62135596800d), now, west).ResetText == "주간 초기화 확인 불가"
                && UsagePresentation.Create(State(now, -62135596800d), now, TimeZoneInfo.Utc).ResetText == "주간 초기화 시각 지남 · 갱신 필요";
            checks["presentation_past_and_exact_reset_instants_request_refresh"] = UsagePresentation.Create(State(now, now.AddSeconds(-1).ToUnixTimeSeconds()), now, korea).ResetText == "주간 초기화 시각 지남 · 갱신 필요"
                && UsagePresentation.Create(State(now, now.ToUnixTimeSeconds()), now, korea).ResetText == "주간 초기화 시각 지남 · 갱신 필요";
            UsagePresentation passed = UsagePresentation.Create(State(now, now.AddSeconds(-1).ToUnixTimeSeconds()), now, korea);
            checks["presentation_expired_countdown_requests_refresh_independently"] = passed.ResetDateText == "주간 초기화 시각 지남" && passed.CountdownText == "갱신 필요";

            bool historical = true;
            foreach (string status in new[] { "loading", "stale", "unavailable", "error" })
            {
                UsageState old = State(now, reset.ToUnixTimeSeconds());
                old.status = status;
                old.reason = "request_timeout";
                UsagePresentation previous = UsagePresentation.Create(old, now, korea);
                historical &= previous.ResetText.StartsWith("이전 주간 초기화 ", StringComparison.Ordinal)
                    && previous.ResetDateText == "이전 주간 초기화 11/10(화) 12:00" && previous.CountdownText == "이전 D-6"
                    && previous.DetailText.Contains("09:00:00") && old.status == status;
            }
            checks["presentation_loading_stale_and_failure_schedules_are_marked_previous"] = historical;
            UsageState staleUnknown = State(now, null);
            staleUnknown.status = "stale";
            checks["presentation_stale_unknown_countdown_explicitly_means_previous_schedule"] = UsagePresentation.Create(staleUnknown, now, korea).CountdownText == "이전 일정";
            bool noHistory = true;
            foreach (string status in new[] { "loading", "stale", "unavailable" })
            {
                UsagePresentation initial = UsagePresentation.Create(new UsageState { status = status }, now, korea);
                noHistory &= initial.ResetText == "주간 초기화 확인 불가"
                    && initial.ResetDateText == "주간 초기화 확인 불가" && initial.CountdownText == "확인 불가";
            }
            checks["presentation_empty_initial_or_failed_state_does_not_invent_previous_schedule"] = noHistory;
            UsageState aged = State(now, reset.ToUnixTimeSeconds());
            aged.observedAt = now.AddMinutes(-3).ToString("o", CultureInfo.InvariantCulture);
            UsageState futureObservation = State(now.AddMinutes(1), reset.ToUnixTimeSeconds());
            checks["presentation_aged_or_future_observations_do_not_claim_fresh_schedule"] = UsagePresentation.Create(aged, now, korea).ResetText.StartsWith("이전 주간 초기화 ", StringComparison.Ordinal)
                && UsagePresentation.Create(futureObservation, now, korea).ResetText.StartsWith("이전 주간 초기화 ", StringComparison.Ordinal);
            UsageState failed = new UsageState { status = "unavailable", attemptedAt = now.ToString("o", CultureInfo.InvariantCulture) };
            checks["presentation_failure_attempt_clock_is_retained_without_success_data"] = UsagePresentation.Create(failed, now, korea).DetailText == "조회 실패 · 시도 09:00:00";

            UsageState low = State(now, reset.ToUnixTimeSeconds());
            low.windows[0].remainingPercent = 14;
            low.windows[0].resetsAtKorea = "untrusted preformatted date";
            UsagePresentation warning = UsagePresentation.Create(low, now, korea);
            checks["presentation_existing_weekly_warning_and_observation_clock_are_preserved"] = warning.LowWeeklyRemaining == 14 && warning.Tone == UsageTone.Low
                && warning.DetailText == "주간 한도 부족 · 갱신 09:00:00" && warning.ResetText == display.ResetText;
            UsageState nullEntry = State(now, reset.ToUnixTimeSeconds());
            nullEntry.windows.Add(null);
            checks["presentation_null_state_and_window_entries_are_safe"] = UsagePresentation.Create(null, now, korea).ResetText.Contains("확인 불가")
                && UsagePresentation.Create(nullEntry, now, korea).ResetText == display.ResetText;
            CheckQueryFailures(checks, now, reset, korea, west);
        }

        private static void CheckQueryFailures(Dictionary<string, bool> checks, DateTimeOffset now,
            DateTimeOffset reset, TimeZoneInfo korea, TimeZoneInfo west)
        {
            UsageState retry = State(now, reset.ToUnixTimeSeconds());
            retry.status = "loading";
            retry.reason = "request_timeout";
            UsagePresentation loading = UsagePresentation.Create(retry, now, korea);
            UsagePresentation firstLoad = UsagePresentation.Create(new UsageState { status = "loading" }, now, korea);
            checks["presentation_query_initial_loading_and_retry_do_not_expose_previous_failure"] = !firstLoad.HasQueryFailure
                && firstLoad.FailureDetails == String.Empty && firstLoad.MainText == "한도 확인 중"
                && !loading.HasQueryFailure && loading.FailureDetails == String.Empty
                && loading.DetailText == "갱신 중 · 마지막 09:00:00";

            UsageState aged = State(now.AddMinutes(-3), reset.ToUnixTimeSeconds());
            UsageState expired = State(now.AddMinutes(-3), reset.ToUnixTimeSeconds());
            expired.status = "stale";
            expired.reason = "last_update_expired";
            UsageState simpleStale = State(now.AddMinutes(-3), reset.ToUnixTimeSeconds());
            simpleStale.status = "stale";
            UsageState changed = new UsageState { status = "unavailable", reason = "account_changed" };
            bool notFailure = true;
            foreach (UsageState observation in new[] { aged, expired, simpleStale, changed })
            {
                UsagePresentation result = UsagePresentation.Create(observation, now, korea);
                notFailure &= !result.HasQueryFailure && result.FailureDetails == String.Empty;
            }
            checks["presentation_query_expiration_and_account_recheck_are_not_failures"] = notFailure;

            bool knownWithSuccess = true, knownWithoutSuccess = true;
            foreach (string reason in new[] { "process_start_failed", "process_exited", "process_not_running", "worker_process_exited",
                "process_write_failed", "process_read_failed", "request_timeout", "usage_unavailable",
                "query_failed", "invalid_arguments", "rpc_error_-32603" })
            {
                UsageState previous = State(now.AddMinutes(-1), reset.ToUnixTimeSeconds());
                previous.status = "stale";
                previous.reason = reason;
                previous.attemptedAt = now.ToString("o", CultureInfo.InvariantCulture);
                UsagePresentation failed = UsagePresentation.Create(previous, now, korea);
                knownWithSuccess &= failed.HasQueryFailure && failed.FailureDetails.Contains("마지막 성공: 2026/11/04 08:59:00 +09:00")
                    && failed.FailureDetails.Contains("마지막 시도: 2026/11/04 09:00:00 +09:00")
                    && failed.FailureDetails.Contains("지금 갱신") && failed.FailureDetails.Contains("로그인 상태")
                    && !failed.FailureDetails.Contains("주간 초기화") && !failed.FailureDetails.Contains("5시간 초기화")
                    && !failed.FailureDetails.Contains("rpc_error_") && previous.status == "stale" && previous.reason == reason;
                UsagePresentation noSuccess = UsagePresentation.Create(new UsageState { status = "unavailable", reason = reason }, now, korea);
                knownWithoutSuccess &= noSuccess.HasQueryFailure && noSuccess.FailureDetails.Contains("마지막 성공: 없음")
                    && !noSuccess.FailureDetails.Contains("마지막 시도:");
            }
            checks["presentation_query_known_failures_retain_previous_success_and_attempt_times"] = knownWithSuccess;
            checks["presentation_query_failure_without_success_explicitly_says_none"] = knownWithoutSuccess;

            UsageState timed = State(now.AddMinutes(-1), reset.ToUnixTimeSeconds());
            timed.status = "stale";
            timed.reason = "process_read_failed";
            timed.attemptedAt = now.ToString("o", CultureInfo.InvariantCulture);
            UsagePresentation western = UsagePresentation.Create(timed, now, west);
            checks["presentation_query_failure_details_use_selected_timezone_and_date"] = western.FailureDetails.Contains("마지막 성공: 2026/11/03 15:59:00 -08:00")
                && western.FailureDetails.Contains("마지막 시도: 2026/11/03 16:00:00 -08:00");
            timed.status = "ok";
            timed.observedAt = now.ToString("o", CultureInfo.InvariantCulture);
            UsagePresentation recovered = UsagePresentation.Create(timed, now, korea);
            checks["presentation_query_success_clears_failure_even_if_old_reason_remains"] = !recovered.HasQueryFailure
                && recovered.FailureDetails == String.Empty && recovered.DetailText == "갱신 09:00:00 · Codex 계정"
                && recovered.CountdownText == "D-6" && recovered.MainText == "주간 80% 남음";

            string sensitive = "rpc_error_401 token=private-secret C:\\private\\auth.json exception-body";
            UsagePresentation unknown = UsagePresentation.Create(new UsageState { status = "unknown-private-status", reason = sensitive }, now, korea);
            checks["presentation_query_null_and_unknown_states_fail_safely_without_raw_text"] = UsagePresentation.Create(null, now, korea).HasQueryFailure
                && UsagePresentation.Create(new UsageState(), now, korea).HasQueryFailure && unknown.HasQueryFailure
                && !unknown.FailureDetails.Contains("private") && !unknown.FailureDetails.Contains("rpc_error")
                && !unknown.FailureDetails.Contains("401") && !unknown.FailureDetails.Contains("exception-body")
                && !unknown.Details.Contains(sensitive);
            UsagePresentation missingReason = UsagePresentation.Create(new UsageState { status = "unavailable" }, now, korea);
            checks["presentation_query_unavailable_without_reason_is_generic_failure"] = missingReason.HasQueryFailure
                && missingReason.FailureDetails.StartsWith("문제: 공식 한도 조회를 완료하지 못했습니다", StringComparison.Ordinal);
            UsagePresentation brokenTimes = UsagePresentation.Create(new UsageState
            {
                status = "unavailable", reason = "usage_unavailable", observedAt = "invalid-success-time",
                attemptedAt = "invalid-attempt-time"
            }, now, korea);
            checks["presentation_query_invalid_time_fields_are_not_echoed_or_fabricated"] = brokenTimes.HasQueryFailure
                && brokenTimes.FailureDetails.Contains("마지막 성공: 확인 불가") && brokenTimes.FailureDetails.Contains("마지막 시도: 확인 불가")
                && !brokenTimes.FailureDetails.Contains("invalid-");
        }

        private static DateTimeOffset At(string iso)
        {
            return DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);
        }

        private static UsageState State(DateTimeOffset observed, double? reset)
        {
            return new UsageState
            {
                status = "ok",
                observedAt = observed.ToString("o", CultureInfo.InvariantCulture),
                windows = new List<LimitWindow> { Window("primary", QuotaPeriods.WeeklyMinutes, reset) }
            };
        }

        private static LimitWindow Window(string role, double? minutes, double? reset)
        {
            return new LimitWindow
            {
                limitId = "codex",
                window = role,
                windowDurationMins = minutes,
                resetsAt = reset,
                remainingPercent = 80
            };
        }
    }
}
