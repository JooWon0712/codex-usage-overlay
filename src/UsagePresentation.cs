using System;
using System.Collections.Generic;
using System.Globalization;

namespace PersonalUsageOverlay
{
    internal enum UsageTone { Normal, Attention, Low }

    // Formats an observation without reading an account or touching any window.
    internal sealed class UsagePresentation
    {
        public readonly string MainText, DetailText, Details, ResetText, ResetDateText, CountdownText, FailureDetails;
        public readonly bool HasQueryFailure;
        public readonly string PlanText;
        public readonly double? LowWeeklyRemaining;
        public readonly UsageTone Tone;
        private UsagePresentation(string mainText, string detailText, ResetPresentation reset, string details,
            double? lowWeeklyRemaining, UsageTone tone, bool hasQueryFailure, string failureDetails, string planText)
        {
            MainText = mainText;
            DetailText = detailText;
            ResetText = reset.Combined;
            ResetDateText = reset.Date;
            CountdownText = reset.Countdown;
            Details = details;
            LowWeeklyRemaining = lowWeeklyRemaining;
            Tone = tone;
            HasQueryFailure = hasQueryFailure;
            FailureDetails = failureDetails;
            PlanText = planText;
        }

        public string BadgeDetailText(bool showPlan)
        {
            if (!showPlan) return DetailText;
            const string accountSuffix = " · Codex 계정";
            string text = DetailText.EndsWith(accountSuffix, StringComparison.Ordinal)
                ? DetailText.Substring(0, DetailText.Length - accountSuffix.Length) : DetailText;
            return text + " · " + PlanText;
        }

        internal bool HasSameDisplayAs(UsagePresentation prior)
        {
            return prior != null && prior.MainText == MainText && prior.DetailText == DetailText && prior.PlanText == PlanText
                && prior.ResetDateText == ResetDateText && prior.CountdownText == CountdownText && prior.Details == Details
                && prior.LowWeeklyRemaining == LowWeeklyRemaining && prior.Tone == Tone
                && prior.HasQueryFailure == HasQueryFailure && prior.FailureDetails == FailureDetails;
        }

        public static UsagePresentation Create(UsageState state, DateTimeOffset now)
        {
            return Create(state, now, TimeZoneInfo.Local);
        }

        // Settings preview only: a stable example tied to this process's launch.
        // It never reads or replaces a live account observation and never expires.
        public static UsagePresentation CreatePreview(DateTimeOffset startedAt)
        {
            return CreatePreview(startedAt, TimeZoneInfo.Local);
        }

        public static UsagePresentation CreatePreview(DateTimeOffset startedAt, TimeZoneInfo zone)
        {
            zone = zone ?? TimeZoneInfo.Local;
            var windows = new List<LimitWindow>
            {
                new LimitWindow
                {
                    limitId = "codex", window = "primary", windowDurationMins = QuotaPeriods.WeeklyMinutes,
                    remainingPercent = 96, resetsAt = startedAt.AddDays(7).ToUnixTimeSeconds()
                }
            };
            var reset = WeeklyResetText(windows, startedAt, zone, true);
            return new UsagePresentation("주간 96% 남음", "갱신 " + TimeText(startedAt.ToString("o", CultureInfo.InvariantCulture), zone) + " · 예시",
                reset, "설정 미리보기용 예시 데이터", null, UsageTone.Normal, false, String.Empty, "Plus");
        }

        public static UsagePresentation Create(UsageState state, DateTimeOffset now, TimeZoneInfo zone)
        {
            state = state ?? new UsageState { status = "unavailable" };
            zone = zone ?? TimeZoneInfo.Local;
            var windows = (state.windows ?? new List<LimitWindow>()).FindAll(w => w != null && w.limitId == "codex");
            DateTimeOffset observed;
            bool fresh = TryTime(state.observedAt, out observed) && observed <= now && now - observed < WeeklyQuotaWarning.FreshFor;
            string status = state.status ?? "unavailable";
            if (status == "ok" && !fresh)
                status = "stale";
            fresh = status == "ok" && fresh;
            double? lowWeekly = WeeklyQuotaWarning.Remaining(state, now);
            var parts = new List<string>();
            var resets = new List<string>();
            foreach (var window in windows)
            {
                string label = WindowLabel(window);
                parts.Add(label + " " + window.remainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%");
                DateTimeOffset reset;
                // The weekly schedule is shown once, with its freshness and D-day.
                if (window.windowDurationMins != QuotaPeriods.WeeklyMinutes && TryReset(window.resetsAt, zone, out reset))
                {
                    resets.Add((fresh ? "" : "이전 ") + label + " 초기화: " + reset.ToString("MM/dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
                }
            }

            string mainText = parts.Count > 0 ? (fresh ? "" : "이전 값 · ") + string.Join(" · ", parts.ToArray()) + " 남음" : status == "loading" ? "한도 확인 중" : "한도 확인 불가";
            string detailText = StatusText(state, status, parts.Count > 0, zone);
            if (lowWeekly.HasValue)
                detailText = (lowWeekly.Value == 0 ? "주간 한도 소진" : "주간 한도 부족") + " · 갱신 " + TimeText(state.observedAt, zone);
            ResetPresentation resetText = WeeklyResetText(windows, now, zone, fresh);
            string details = "연결된 Codex 계정의 한도\n" + detailText + "\n" + resetText.Combined;
            if (resets.Count > 0)
                details += "\n" + string.Join("\n", resets.ToArray());
            if (state.reason != null)
                details += "\n상태: " + FriendlyReason(state.reason);
            var tone = lowWeekly.HasValue ? UsageTone.Low : status == "ok" && parts.Count > 0 ? UsageTone.Normal : UsageTone.Attention;
            bool hasQueryFailure = IsQueryFailure(state);
            string planName = PlanName(state.planType);
            bool hasPlanObservation = windows.Count > 0 && TryTime(state.observedAt, out observed)
                && observed <= now && state.reason != "account_changed";
            string planText = hasPlanObservation && planName != null ? (fresh ? "" : "이전 ") + planName
                : status == "loading" && state.observedAt == null ? "요금제 확인 중" : "요금제 확인 불가";
            return new UsagePresentation(mainText, detailText, resetText, details, lowWeekly, tone,
                hasQueryFailure, hasQueryFailure ? QueryFailureDetails(state, zone) : String.Empty, planText);
        }

        private static string PlanName(string value)
        {
            switch (value)
            {
                case "free": return "Free";
                case "go": return "Go";
                case "plus": return "Plus";
                case "prolite": return "Pro 100";
                case "pro": return "Pro 200";
                case "promax": return "Pro 500";
                case "team": return "Team";
                case "business": return "Business";
                case "enterprise": return "Enterprise";
                case "edu": return "Edu";
                // Preserve the verified protocol identifier for specialized plans;
                // do not invent a retail name, price or paid entitlement from it.
                case "self_serve_business_prolite":
                case "self_serve_business_usage_based": case "ent26":
                case "enterprise_cbp_automation": case "enterprise_cbp_usage_based":
                case "edu_plus": case "edu_pro": case "sci": case "sci_plus":
                    return "요금제: " + value;
                default: return null;
            }
        }

        private static bool IsQueryFailure(UsageState state)
        {
            // usage-state.mjs begin/success clear an earlier failure. In-memory
            // retry/recovery snapshots must also stay safe if an old reason remains.
            if (state.status == "loading" || state.status == "ok")
                return false;
            if (state.reason == "account_changed" || state.reason == "last_update_expired")
                return false;
            // Expiration can also be computed locally without a worker reason.
            if (state.status == "stale" && String.IsNullOrEmpty(state.reason))
                return false;
            // Worker failures use stale/unavailable. Unknown status is invalid
            // query state, not a successful observation or an inferred live quota.
            return true;
        }

        private static string QueryFailureDetails(UsageState state, TimeZoneInfo zone)
        {
            string details = "문제: " + FriendlyReason(state.reason)
                + "\n마지막 성공: " + FailureTime(state.observedAt, zone, "없음");
            if (!String.IsNullOrEmpty(state.attemptedAt))
                details += "\n마지막 시도: " + FailureTime(state.attemptedAt, zone, "확인 불가");
            return details + "\n지금 갱신을 눌러 다시 시도하세요. 계속 실패하면 Codex 로그인 상태와 연결 상태를 확인하세요.";
        }

        private static string FailureTime(string iso, TimeZoneInfo zone, string absent)
        {
            if (String.IsNullOrEmpty(iso))
                return absent;
            DateTimeOffset value, local;
            return TryTime(iso, out value) && TryLocalTime(value, zone, out local)
                ? local.ToString("yyyy/MM/dd HH:mm:ss zzz", CultureInfo.InvariantCulture) : "확인 불가";
        }

        private sealed class ResetPresentation
        {
            public readonly string Combined, Date, Countdown;
            public ResetPresentation(string date, string countdown, string combinedCountdown)
            {
                Date = date;
                Countdown = countdown;
                Combined = date + (combinedCountdown == null ? "" : " · " + combinedCountdown);
            }
        }

        private static ResetPresentation UnknownReset(string prefix, bool previous)
        {
            return new ResetPresentation(prefix + " 확인 불가", previous ? "이전 일정" : "확인 불가", null);
        }

        private static ResetPresentation WeeklyResetText(List<LimitWindow> windows, DateTimeOffset now, TimeZoneInfo zone, bool fresh)
        {
            bool previous = !fresh && windows.Count > 0;
            string prefix = previous ? "이전 주간 초기화" : "주간 초기화";
            double? seconds = null;
            DateTimeOffset reset = default(DateTimeOffset);
            foreach (LimitWindow window in windows)
            {
                if (window.windowDurationMins != QuotaPeriods.WeeklyMinutes)
                    continue;
                DateTimeOffset candidate;
                // Missing/invalid entries cannot be reconciled with another weekly entry.
                if (!TryReset(window.resetsAt, zone, out candidate)
                    || (seconds.HasValue && seconds.Value != window.resetsAt.Value))
                    return UnknownReset(prefix, previous);
                seconds = window.resetsAt;
                reset = candidate;
            }
            DateTimeOffset localNow;
            if (!seconds.HasValue || !TryLocalTime(now, zone, out localNow))
                return UnknownReset(prefix, previous);
            if (reset <= now)
                return new ResetPresentation(prefix + " 시각 지남", "갱신 필요", "갱신 필요");
            int days = (reset.Date - localNow.Date).Days;
            if (days < 0)
                return UnknownReset(prefix, previous);
            string date = reset.ToString(reset.Year == localNow.Year ? "MM/dd" : "yyyy/MM/dd", CultureInfo.InvariantCulture);
            string weekday = new[] { "일", "월", "화", "수", "목", "금", "토" }[(int)reset.DayOfWeek];
            string countdown = days == 0 ? "D-DAY" : "D-" + days.ToString(CultureInfo.InvariantCulture);
            return new ResetPresentation(prefix + " " + date + "(" + weekday + ") " + reset.ToString("HH:mm", CultureInfo.InvariantCulture),
                previous ? "이전 " + countdown : countdown, countdown);
        }

        private static bool TryReset(double? seconds, TimeZoneInfo zone, out DateTimeOffset result)
        {
            result = default(DateTimeOffset);
            if (!seconds.HasValue || Double.IsNaN(seconds.Value) || Double.IsInfinity(seconds.Value)
                || seconds.Value < -62135596800d || seconds.Value > 253402300799d)
                return false;
            try
            {
                double whole = Math.Floor(seconds.Value);
                DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds((long)whole)
                    .AddTicks((long)Math.Round((seconds.Value - whole) * TimeSpan.TicksPerSecond));
                return TryLocalTime(utc, zone, out result);
            }
            catch (Exception) { return false; }
        }

        private static bool TryLocalTime(DateTimeOffset value, TimeZoneInfo zone, out DateTimeOffset result)
        {
            result = default(DateTimeOffset);
            try
            {
                // ConvertTime may clamp an out-of-range local date. Reject that case
                // rather than displaying a date different from the actual instant.
                long ticks = value.UtcDateTime.Ticks + zone.GetUtcOffset(value).Ticks;
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                    return false;
                result = TimeZoneInfo.ConvertTime(value, zone);
                return result.UtcDateTime.Ticks == value.UtcDateTime.Ticks;
            }
            catch (Exception) { return false; }
        }

        private static string WindowLabel(LimitWindow window)
        {
            if (window.windowDurationMins == QuotaPeriods.WeeklyMinutes)
                return "주간";
            if (window.windowDurationMins == QuotaPeriods.FiveHourMinutes)
                return "5시간";
            return window.windowDurationMins.HasValue ? window.windowDurationMins.Value.ToString("0", CultureInfo.InvariantCulture) + "분" : window.window;
        }

        private static string StatusText(UsageState state, string status, bool hasValues, TimeZoneInfo zone)
        {
            if (status == "loading")
                return state.observedAt == null ? "공식 조회 · 연결 중" : "갱신 중 · 마지막 " + TimeText(state.observedAt, zone);
            if (status == "ok" && hasValues)
                return "갱신 " + TimeText(state.observedAt, zone) + " · Codex 계정";
            if (hasValues)
                return (state.reason != null && state.reason != "last_update_expired" ? "갱신 실패" : "오래된 값") + " · 마지막 " + TimeText(state.observedAt, zone);
            return state.attemptedAt == null ? "조회 실패 · 설정 창에서 다시 갱신" : "조회 실패 · 시도 " + TimeText(state.attemptedAt, zone);
        }

        private static string TimeText(string iso, TimeZoneInfo zone)
        {
            DateTimeOffset value, local;
            return TryTime(iso, out value) && TryLocalTime(value, zone, out local)
                ? local.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "—";
        }

        private static bool TryTime(string iso, out DateTimeOffset value)
        {
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        private static string FriendlyReason(string reason)
        {
            if (reason == "account_changed")
                return "로그인 상태가 바뀌어 다시 확인합니다";
            if (reason == "request_timeout")
                return "조회 응답을 기다리는 시간이 초과되었습니다";
            if (reason == "last_update_expired")
                return "마지막 확인 후 시간이 지났습니다";
            if (reason == "process_start_failed")
                return "조회 연결을 시작하지 못했습니다";
            if (reason == "process_exited" || reason == "process_not_running" || reason == "worker_process_exited")
                return "조회 연결이 종료되어 한도를 확인하지 못했습니다";
            if (reason == "process_write_failed")
                return "조회 요청을 전달하지 못했습니다";
            if (reason == "process_read_failed")
                return "조회 응답을 읽지 못했습니다";
            if (reason == "usage_unavailable")
                return "한도 데이터가 없거나 형식이 올바르지 않습니다";
            if (reason == "invalid_arguments")
                return "조회 연결 설정을 확인하지 못했습니다";
            // Includes query_failed, RPC errors, and unknown future reasons. Never
            // echo identifiers, RPC codes, paths, or an exception's original text.
            return "공식 한도 조회를 완료하지 못했습니다";
        }
    }
}
