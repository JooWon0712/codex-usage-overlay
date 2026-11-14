using System;
using System.Collections.Generic;
using System.Globalization;

namespace PersonalUsageOverlay
{
    public sealed class UsageLookupResult
    {
        public string mode, attemptedAt, finishedAt, status, reason;
        public List<LimitWindow> windows;
    }

    internal sealed class UsageLookupHistory
    {
        private const int Capacity = 30;
        private readonly List<string> entries = new List<string>();

        public string Text
        {
            get { return entries.Count == 0 ? "아직 조회 기록이 없습니다." : string.Join(Environment.NewLine, entries.ToArray()); }
        }

        public void Add(UsageLookupResult result)
        {
            DateTimeOffset attempted, finished;
            if (result == null || (result.mode != "automatic" && result.mode != "manual")
                || (result.status != "ok" && result.status != "unavailable")
                || !DateTimeOffset.TryParse(result.attemptedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out attempted)
                || !DateTimeOffset.TryParse(result.finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out finished)
                || finished < attempted)
                return;

            var remaining = new List<string>();
            if (result.status == "ok" && result.windows != null)
                foreach (var window in result.windows)
                    if (IsCodexWindow(window))
                        remaining.Add(UsagePresentation.WindowLabel(window) + " "
                            + window.remainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%");

            string outcome = remaining.Count > 0
                ? "성공 · " + string.Join(", ", remaining.ToArray()) + " 남음"
                : "실패 · " + UsagePresentation.FriendlyReason(result.status == "ok" ? "usage_unavailable" : result.reason);
            entries.Insert(0, finished.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
                + (result.mode == "manual" ? " · 수동 · " : " · 자동 · ") + outcome);
            if (entries.Count > Capacity)
                entries.RemoveAt(entries.Count - 1);
        }

        public static bool IsCodexWindow(LimitWindow window)
        {
            return window != null && window.limitId == "codex"
                && window.remainingPercent >= 0 && window.remainingPercent <= 100;
        }
    }
}
