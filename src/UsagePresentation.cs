using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace PersonalUsageOverlay
{
    // Formats an observation without reading an account or touching any window.
    internal sealed class UsagePresentation
    {
        public readonly string MainText, DetailText, Details;
        public readonly double? LowWeeklyRemaining;
        public readonly Color StateColor;
        private UsagePresentation(string mainText, string detailText, string details, double? lowWeeklyRemaining, Color stateColor)
        {
            MainText = mainText;
            DetailText = detailText;
            Details = details;
            LowWeeklyRemaining = lowWeeklyRemaining;
            StateColor = stateColor;
        }

        public static UsagePresentation Create(UsageState state, DateTimeOffset now)
        {
            var windows = (state.windows ?? new List<LimitWindow>()).FindAll(w => w.limitId == "codex");
            DateTimeOffset observed;
            bool aged = DateTimeOffset.TryParse(state.observedAt, out observed) && now - observed >= WeeklyQuotaWarning.FreshFor;
            string status = state.status ?? "unavailable";
            if (status == "ok" && aged)
                status = "stale";
            double? lowWeekly = WeeklyQuotaWarning.Remaining(state, now);
            var parts = new List<string>();
            var resets = new List<string>();
            foreach (var window in windows)
            {
                string label = WindowLabel(window);
                parts.Add(label + " " + window.remainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%");
                if (window.resetsAt.HasValue && window.resetsAt.Value >= -62135596800 && window.resetsAt.Value <= 253402300799)
                {
                    var reset = DateTimeOffset.FromUnixTimeSeconds((long)window.resetsAt.Value).ToLocalTime();
                    resets.Add(label + " 초기화: " + reset.ToString("MM/dd HH:mm:ss zzz"));
                }
            }

            string mainText = parts.Count > 0 ? (status == "stale" ? "이전 값 · " : "") + string.Join(" · ", parts.ToArray()) + " 남음" : status == "loading" ? "한도 확인 중" : "한도 확인 불가";
            string detailText = StatusText(state, status, parts.Count > 0);
            if (lowWeekly.HasValue)
                detailText = (lowWeekly.Value == 0 ? "주간 한도 소진" : "주간 한도 부족") + " · 갱신 " + TimeText(state.observedAt);
            string details = "연결된 Codex 계정의 한도\n" + detailText + "\n" + string.Join("\n", resets.ToArray());
            if (state.reason != null)
                details += "\n상태: " + FriendlyReason(state.reason);
            var color = lowWeekly.HasValue ? Color.FromArgb(242, 100, 112) : status == "ok" && parts.Count > 0 ? Color.FromArgb(127, 199, 174) : Color.FromArgb(238, 183, 98);
            return new UsagePresentation(mainText, detailText, details, lowWeekly, color);
        }

        private static string WindowLabel(LimitWindow window)
        {
            if (window.windowDurationMins == QuotaPeriods.WeeklyMinutes)
                return "주간";
            if (window.windowDurationMins == QuotaPeriods.FiveHourMinutes)
                return "5시간";
            return window.windowDurationMins.HasValue ? window.windowDurationMins.Value.ToString("0", CultureInfo.InvariantCulture) + "분" : window.window;
        }

        private static string StatusText(UsageState state, string status, bool hasValues)
        {
            if (status == "loading")
                return state.observedAt == null ? "공식 조회 · 연결 중" : "갱신 중 · 마지막 " + TimeText(state.observedAt);
            if (status == "ok" && hasValues)
                return "갱신 " + TimeText(state.observedAt) + " · Codex 계정";
            if (hasValues)
                return (state.reason != null && state.reason != "last_update_expired" ? "갱신 실패" : "오래된 값") + " · 마지막 " + TimeText(state.observedAt);
            return "조회 실패 · 설정 창에서 다시 갱신";
        }

        private static string TimeText(string iso)
        {
            DateTimeOffset value;
            return DateTimeOffset.TryParse(iso, out value) ? value.ToLocalTime().ToString("HH:mm:ss") : "—";
        }

        private static string FriendlyReason(string reason)
        {
            if (reason == "account_changed")
                return "로그인 상태가 바뀌어 다시 확인합니다";
            if (reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "조회 시간 초과";
            if (reason.IndexOf("aged", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0)
                return "마지막 확인 후 시간이 지났습니다";
            if (reason.IndexOf("process", StringComparison.OrdinalIgnoreCase) >= 0)
                return "조회 연결을 사용할 수 없습니다";
            if (reason.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
                return "한도 데이터 확인 불가";
            return "공식 조회 실패 (" + reason + ")";
        }
    }
}
