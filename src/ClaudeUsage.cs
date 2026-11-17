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
    }

    internal sealed class ClaudeUsageSnapshot
    {
        public string observedAt;
        public ClaudeUsageWindow fiveHour, weekly;
    }

    internal sealed class ClaudeUsageDisplay
    {
        private const string NoFiveHour = "5시간 · 확인 불가", NoWeekly = "주간 · 확인 불가";
        internal static readonly ClaudeUsageDisplay Awaiting = new ClaudeUsageDisplay("한도 수신 대기", NoFiveHour, NoWeekly);
        internal static readonly ClaudeUsageDisplay Unreadable = new ClaudeUsageDisplay("데이터 확인 불가", NoFiveHour, NoWeekly);
        public readonly string Status, FiveHour, Weekly;

        private ClaudeUsageDisplay(string status, string fiveHour, string weekly)
        {
            Status = status;
            FiveHour = fiveHour;
            Weekly = weekly;
        }

        internal static ClaudeUsageDisplay FromSnapshot(ClaudeUsageSnapshot snapshot, DateTimeOffset now, TimeZoneInfo zone)
        {
            DateTimeOffset observed;
            if (snapshot == null || !DateTimeOffset.TryParse(snapshot.observedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out observed) || observed > now)
                return Unreadable;

            return new ClaudeUsageDisplay("마지막 수신 " + LocalTime(observed, zone),
                WindowText("5시간", snapshot.fiveHour, now, zone),
                WindowText("주간", snapshot.weekly, now, zone));
        }

        internal static ClaudeUsageDisplay Example(DateTimeOffset startedAt)
        {
            var snapshot = new ClaudeUsageSnapshot
            {
                observedAt = startedAt.ToString("o", CultureInfo.InvariantCulture),
                fiveHour = new ClaudeUsageWindow { usedPercentage = 24, resetsAt = startedAt.AddHours(3).ToUnixTimeSeconds() },
                weekly = new ClaudeUsageWindow { usedPercentage = 41, resetsAt = startedAt.AddDays(4).ToUnixTimeSeconds() }
            };
            var example = FromSnapshot(snapshot, startedAt, TimeZoneInfo.Local);
            return new ClaudeUsageDisplay("예시 · " + LocalTime(startedAt, TimeZoneInfo.Local), example.FiveHour, example.Weekly);
        }

        internal bool HasSameDisplayAs(ClaudeUsageDisplay prior)
        {
            return prior != null && Status == prior.Status && FiveHour == prior.FiveHour && Weekly == prior.Weekly;
        }

        private static string WindowText(string label, ClaudeUsageWindow window, DateTimeOffset now, TimeZoneInfo zone)
        {
            double used = window == null ? Double.NaN : window.usedPercentage ?? Double.NaN;
            if (Double.IsNaN(used) || used < 0 || used > 100) return label + " · 확인 불가";
            DateTimeOffset? reset = null;
            if (window.resetsAt.HasValue && window.resetsAt.Value >= 0 && window.resetsAt.Value <= 253402300799L)
                reset = DateTimeOffset.FromUnixTimeSeconds(window.resetsAt.Value);
            if (reset.HasValue && reset.Value <= now) return label + " · 초기화 후 갱신 대기";

            return label + " " + (100 - used).ToString("0.#", CultureInfo.InvariantCulture) + "% 남음"
                + (reset.HasValue ? " · 초기화 " + LocalTime(reset.Value, zone, "M'월' d'일' HH:mm") : "");
        }

        private static string LocalTime(DateTimeOffset value, TimeZoneInfo zone, string format = "M/d HH:mm")
        {
            return TimeZoneInfo.ConvertTime(value, zone).ToString(format, CultureInfo.InvariantCulture);
        }
    }

    internal sealed class ClaudeUsageReader
    {
        private readonly string path;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 12 };
        private DateTimeOffset nextRead;
        private ClaudeUsageSnapshot snapshot;
        private ClaudeUsageDisplay fallback = ClaudeUsageDisplay.Awaiting;

        internal ClaudeUsageReader(string path) { this.path = path; }

        internal ClaudeUsageDisplay Read(DateTimeOffset now)
        {
            if (now >= nextRead || nextRead - now > TimeSpan.FromSeconds(2))
            {
                nextRead = now.AddSeconds(2);
                snapshot = null;
                fallback = ClaudeUsageDisplay.Unreadable;
                try
                {
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var contents = new StreamReader(file))
                        if (file.Length <= json.MaxJsonLength)
                            snapshot = json.Deserialize<ClaudeUsageSnapshot>(contents.ReadToEnd());
                }
                catch (FileNotFoundException) { fallback = ClaudeUsageDisplay.Awaiting; }
                catch (DirectoryNotFoundException) { fallback = ClaudeUsageDisplay.Awaiting; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return snapshot == null ? fallback : ClaudeUsageDisplay.FromSnapshot(snapshot, now, TimeZoneInfo.Local);
        }
    }
}
