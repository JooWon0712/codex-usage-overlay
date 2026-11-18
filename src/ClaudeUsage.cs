using System;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace PersonalUsageOverlay
{
    internal sealed class ClaudeUsageWindow
    {
        public double? usedPercentage;
        public long? resetsAt;
        public string observedAt;
    }

    internal sealed class ClaudeUsageSnapshot
    {
        public string observedAt, attemptedAt, status, subscriptionType;
        public ClaudeUsageWindow fiveHour, weekly;
    }

    internal sealed class ClaudeUsageDisplay
    {
        private static readonly WindowDisplay NoFiveHour = new WindowDisplay("5시간 · 확인 불가");
        private static readonly WindowDisplay NoWeekly = new WindowDisplay("주간 · 확인 불가");
        internal static readonly ClaudeUsageDisplay Awaiting = new ClaudeUsageDisplay("한도 수신 대기", NoFiveHour, NoWeekly);
        internal static readonly ClaudeUsageDisplay Unreadable = new ClaudeUsageDisplay("데이터 확인 불가", NoFiveHour, NoWeekly);
        public readonly string Status, FiveHour, Weekly, FiveHourReset, WeeklyReset;
        public readonly string CompactStatus, FiveHourCompactAmount, WeeklyCompactAmount, FiveHourCompactReset, WeeklyCompactReset;
        public readonly string PlanText;
        public readonly string FiveHourCountdownText, WeeklyCountdownText;
        public readonly string SetupHint;
        public readonly double? FiveHourRemaining, WeeklyRemaining;
        public readonly bool FiveHourIsPrevious, WeeklyIsPrevious;

        private ClaudeUsageDisplay(string status, WindowDisplay fiveHour, WindowDisplay weekly, string compactStatus = null,
            string setupHint = null, string planText = null)
        {
            Status = status;
            FiveHour = fiveHour.Text;
            Weekly = weekly.Text;
            FiveHourRemaining = fiveHour.Remaining;
            WeeklyRemaining = weekly.Remaining;
            FiveHourIsPrevious = fiveHour.IsPrevious;
            WeeklyIsPrevious = weekly.IsPrevious;
            FiveHourReset = fiveHour.Reset;
            WeeklyReset = weekly.Reset;
            FiveHourCompactAmount = fiveHour.CompactValue;
            WeeklyCompactAmount = weekly.CompactValue;
            FiveHourCompactReset = fiveHour.CompactReset;
            WeeklyCompactReset = weekly.CompactReset;
            FiveHourCountdownText = fiveHour.Countdown;
            WeeklyCountdownText = weekly.Countdown;
            SetupHint = setupHint;
            PlanText = planText;
            if (!FiveHourRemaining.HasValue && !WeeklyRemaining.HasValue)
                CompactStatus = fiveHour.Waiting || weekly.Waiting ? "한도 갱신 대기" : compactStatus ?? status;
        }

        internal static ClaudeUsageDisplay FromSnapshot(ClaudeUsageSnapshot snapshot, DateTimeOffset now, TimeZoneInfo zone)
        {
            DateTimeOffset observed;
            if (snapshot == null || !DateTimeOffset.TryParse(snapshot.observedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out observed) || observed > now)
                return Unreadable;

            return Create("마지막 수신 " + LocalTime(observed, zone), snapshot, now, zone);
        }

        internal static ClaudeUsageDisplay FromAutomatic(ClaudeUsageSnapshot snapshot, DateTimeOffset now, TimeZoneInfo zone)
        {
            DateTimeOffset attempted;
            if (snapshot == null || !ReadTime(snapshot.attemptedAt, out attempted) || attempted > now) return Unreadable;
            if (snapshot.status == "auth-required")
                return new ClaudeUsageDisplay("Claude 로그인 필요", NoFiveHour, NoWeekly, "로그인 필요",
                    "Claude Code CLI에서 Claude 구독 계정으로 로그인하세요.");
            if (snapshot.status == "missing-cli")
                return new ClaudeUsageDisplay("Claude CLI 확인 불가", NoFiveHour, NoWeekly, "CLI 확인 불가",
                    "Claude Code CLI 설치 여부와 실행 경로를 확인하세요.");

            string status;
            switch (snapshot.status)
            {
                case "ok": status = "자동 갱신"; break;
                case "partial": status = "일부 갱신"; break;
                case "unavailable": status = "갱신 실패"; break;
                default: return Unreadable;
            }

            return new ClaudeUsageDisplay(status + " " + LocalTime(attempted, zone),
                ReadAutomaticWindow("5시간", snapshot.fiveHour, attempted, now, zone),
                ReadAutomaticWindow("주간", snapshot.weekly, attempted, now, zone, weekly: true),
                snapshot.status == "unavailable" ? status : "한도 확인 불가",
                planText: DisplayPlan(snapshot.subscriptionType));
        }

        private static string DisplayPlan(string subscriptionType)
        {
            switch (subscriptionType)
            {
                case "free": return "Free";
                case "pro": return "Pro";
                case "max": return "Max";
                case "ultra": return "Ultra";
                case "team": return "Team";
                case "enterprise": return "Enterprise";
                default: return null;
            }
        }

        private static WindowDisplay ReadAutomaticWindow(string label, ClaudeUsageWindow window,
            DateTimeOffset attempted, DateTimeOffset now, TimeZoneInfo zone, bool weekly = false)
        {
            if (window == null) return new WindowDisplay(label + " · 갱신 대기");
            DateTimeOffset observed;
            if (!ReadTime(window.observedAt, out observed) || observed > now)
                return new WindowDisplay(label + " · 확인 불가");
            var display = ReadWindow(label, window, now, zone, weekly);
            if (!display.Remaining.HasValue || observed >= attempted) return display;

            return new WindowDisplay(display.Text, display.Remaining, display.Reset, display.CompactReset, display.Countdown, previous: true);
        }

        private static bool ReadTime(string value, out DateTimeOffset timestamp)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
        }

        internal static ClaudeUsageDisplay Example(DateTimeOffset startedAt, TimeZoneInfo zone)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = startedAt.ToString("o", CultureInfo.InvariantCulture),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, resetsAt = startedAt.AddHours(3).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, resetsAt = startedAt.AddDays(4).ToUnixTimeSeconds() }
            };
            return Create("예시 · " + LocalTime(startedAt, zone), snapshot, startedAt, zone, planText: "Pro");
        }

        internal bool HasSameDisplayAs(ClaudeUsageDisplay prior)
        {
            return prior != null && Status == prior.Status && FiveHour == prior.FiveHour && Weekly == prior.Weekly
                && FiveHourRemaining == prior.FiveHourRemaining && WeeklyRemaining == prior.WeeklyRemaining
                && FiveHourIsPrevious == prior.FiveHourIsPrevious && WeeklyIsPrevious == prior.WeeklyIsPrevious
                && FiveHourReset == prior.FiveHourReset && WeeklyReset == prior.WeeklyReset && CompactStatus == prior.CompactStatus
                && FiveHourCompactAmount == prior.FiveHourCompactAmount && WeeklyCompactAmount == prior.WeeklyCompactAmount
                && FiveHourCompactReset == prior.FiveHourCompactReset && WeeklyCompactReset == prior.WeeklyCompactReset
                && FiveHourCountdownText == prior.FiveHourCountdownText && WeeklyCountdownText == prior.WeeklyCountdownText
                && SetupHint == prior.SetupHint && PlanText == prior.PlanText;
        }

        private static ClaudeUsageDisplay Create(string status, ClaudeUsageSnapshot snapshot, DateTimeOffset now, TimeZoneInfo zone,
            string planText = null)
        {
            return new ClaudeUsageDisplay(status,
                ReadWindow("5시간", snapshot.fiveHour, now, zone),
                ReadWindow("주간", snapshot.weekly, now, zone, weekly: true), "한도 확인 불가", planText: planText);
        }

        private static WindowDisplay ReadWindow(string label, ClaudeUsageWindow window, DateTimeOffset now, TimeZoneInfo zone,
            bool weekly = false)
        {
            double used = window == null ? Double.NaN : window.usedPercentage ?? Double.NaN;
            if (!(used >= 0 && used <= 100))
                return new WindowDisplay(label + " · 확인 불가");
            DateTimeOffset? reset = null;
            if (window.resetsAt.HasValue && window.resetsAt.Value >= 0 && window.resetsAt.Value <= 253402300799L)
                reset = DateTimeOffset.FromUnixTimeSeconds(window.resetsAt.Value);
            if (reset.HasValue && reset.Value <= now)
                return new WindowDisplay(label + " · 초기화 후 갱신 대기", waiting: true);

            double remaining = 100 - used;
            string text = label + " " + remaining.ToString("0.#", CultureInfo.InvariantCulture) + "% 남음";
            if (reset.HasValue)
            {
                DateTimeOffset localReset = TimeZoneInfo.ConvertTime(reset.Value, zone);
                DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, zone);
                string fullReset = UsagePresentation.FormatDetailedReset(localReset);
                string compactReset = UsagePresentation.FormatCompactReset(localReset, localNow);
                string badgeReset = !weekly && localReset.Date == localNow.Date ? compactReset : fullReset;
                string countdown = weekly
                    ? UsagePresentation.FormatWeeklyCountdown((localReset.Date - localNow.Date).Days, reset.Value - now)
                    : UsagePresentation.FormatRemainingTime(reset.Value - now);
                return new WindowDisplay(text + " · 초기화 " + fullReset, remaining, badgeReset, compactReset, countdown);
            }
            return new WindowDisplay(text, remaining);
        }

        private sealed class WindowDisplay
        {
            public readonly string Text, Reset, CompactValue, CompactReset, Countdown;
            public readonly double? Remaining;
            public readonly bool IsPrevious, Waiting;

            public WindowDisplay(string text, double? remaining = null, string reset = null, string compactReset = null,
                string countdown = null, bool previous = false, bool waiting = false)
            {
                Text = text;
                Remaining = remaining;
                Reset = reset;
                CompactReset = compactReset;
                Countdown = previous && countdown != null ? "이전 " + countdown : countdown;
                IsPrevious = previous;
                Waiting = waiting;
                CompactValue = remaining.HasValue
                    ? remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" + (previous ? " · 이전" : "")
                    : waiting ? "갱신 대기" : "확인 불가";
            }
        }

        private static string LocalTime(DateTimeOffset value, TimeZoneInfo zone)
        {
            return UsagePresentation.FormatDetailedReset(TimeZoneInfo.ConvertTime(value, zone));
        }
    }

    internal sealed class ClaudeUsageReader
    {
        private readonly string path, automaticPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 12 };
        private DateTimeOffset nextRead;
        private ClaudeUsageSnapshot snapshot, automatic;
        private ClaudeUsageDisplay fallback = ClaudeUsageDisplay.Awaiting;

        internal ClaudeUsageReader(string path, string automaticPath = null)
        {
            this.path = path;
            this.automaticPath = automaticPath;
        }

        internal ClaudeUsageDisplay Read(DateTimeOffset now)
        {
            if (now >= nextRead || nextRead - now > TimeSpan.FromSeconds(2))
            {
                nextRead = now.AddSeconds(2);
                snapshot = ReadFile(path, out fallback);
                if (automaticPath != null)
                {
                    ClaudeUsageDisplay ignored;
                    automatic = ReadFile(automaticPath, out ignored);
                }
            }
            if (automatic != null && (automatic.status != "missing-cli" || snapshot == null))
                return ClaudeUsageDisplay.FromAutomatic(automatic, now, TimeZoneInfo.Local);
            return snapshot == null ? fallback : ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Local);
        }

        private ClaudeUsageSnapshot ReadFile(string source, out ClaudeUsageDisplay onFailure)
        {
            onFailure = ClaudeUsageDisplay.Unreadable;
            try
            {
                using (var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var contents = new StreamReader(file))
                    return file.Length <= json.MaxJsonLength ? json.Deserialize<ClaudeUsageSnapshot>(contents.ReadToEnd()) : null;
            }
            catch (FileNotFoundException) { onFailure = ClaudeUsageDisplay.Awaiting; }
            catch (DirectoryNotFoundException) { onFailure = ClaudeUsageDisplay.Awaiting; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return null;
        }
    }
}
