using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    public class Preferences
    {
        internal const string BelowTitle = "title-below";
        internal const int DefaultTitleOffsetX = 0, DefaultTitleGap = 6;
        public string Position = BelowTitle;
        public int SidebarX = 64, SidebarY = 82;
        public int TitleOffsetX = DefaultTitleOffsetX, TitleGap = DefaultTitleGap;
        public string ThemeMode = "codex";

        internal void NormalizePosition()
        {
            if (Position != "sidebar" && Position != BelowTitle)
                Position = BelowTitle;
            TitleOffsetX = Math.Max(-4000, Math.Min(4000, TitleOffsetX));
            TitleGap = Math.Max(0, Math.Min(3000, TitleGap));
            if (ThemeMode != "light" && ThemeMode != "dark" && ThemeMode != "system")
                ThemeMode = "codex";
        }

        internal void ResetCurrentPosition()
        {
            if (Position == "sidebar")
            {
                SidebarX = 64;
                SidebarY = 82;
            }
            else
            {
                TitleOffsetX = DefaultTitleOffsetX;
                TitleGap = DefaultTitleGap;
            }
        }
    }

    public class RuntimePaths
    {
        public string Node;
        public string Codex;
    }

    public class LimitWindow
    {
        public string limitId, window, resetsAtKorea;
        public double usedPercent, remainingPercent;
        public double? windowDurationMins, resetsAt;
    }

    public class UsageState
    {
        public string type, status, observedAt, attemptedAt, reason;
        public List<LimitWindow> windows = new List<LimitWindow>();
    }

    internal static class QuotaPeriods
    {
        public const int WeeklyMinutes = 10080;
        public const int FiveHourMinutes = 300;
    }

    internal static class WeeklyQuotaWarning
    {
        // Matches the warning colour in the inspected desktop version, not its icon visibility.
        public const double ThresholdPercent = 15;
        public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(3);
        public static double? Remaining(UsageState state, DateTimeOffset now)
        {
            DateTimeOffset observed;
            if (state == null || state.status != "ok" || !DateTimeOffset.TryParse(state.observedAt, out observed) || observed > now || now - observed >= FreshFor)
                return null;
            double? lowest = null;
            foreach (var w in state.windows ?? new List<LimitWindow>())
            {
                if (w == null || w.limitId != "codex" || w.windowDurationMins != QuotaPeriods.WeeklyMinutes || double.IsNaN(w.remainingPercent) || double.IsInfinity(w.remainingPercent) || w.remainingPercent < 0 || w.remainingPercent >= ThresholdPercent)
                    continue;
                if (!lowest.HasValue || w.remainingPercent < lowest.Value)
                    lowest = w.remainingPercent;
            }

            return lowest;
        }
    }
}
