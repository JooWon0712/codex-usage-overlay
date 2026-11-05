using System;
using System.Globalization;

namespace PersonalUsageOverlay
{
    // Developer rendering only. All mutations stay in this in-memory fixture.
    internal sealed class SettingsPreviewHost : ISettingsHost
    {
        private readonly Preferences prefs = new Preferences();
        private readonly VisualTheme theme;
        private UsagePresentation presentation;
        internal int ReleasePageRequests;
        public SettingsPreviewHost(VisualTheme theme, string position = Preferences.BelowTitle, UsagePresentation value = null)
        {
            this.theme = theme; prefs.Position = position; prefs.NormalizePosition();
            presentation = value ?? CreateSample(96, "ok");
        }
        internal static UsagePresentation CreateSample(double remaining, string status, bool includeReset = true)
        {
            var now = DateTimeOffset.Parse("2026-11-04T19:59:37+09:00", CultureInfo.InvariantCulture);
            var zone = TimeZoneInfo.CreateCustomTimeZone("FixtureKST", TimeSpan.FromHours(9), "FixtureKST", "FixtureKST");
            var state = new UsageState
            {
                status = status,
                planType = status == "loading" ? null : "plus",
                observedAt = status == "loading" ? null : (status == "stale" ? now.AddMinutes(-4) : now).ToString("o"),
                attemptedAt = status == "stale" ? now.ToString("o") : null,
                reason = status == "stale" ? "request_timeout" : null
            };
            if (status != "loading") state.windows.Add(new LimitWindow { limitId = "codex", window = "primary", windowDurationMins = 10080, remainingPercent = remaining, resetsAt = includeReset ? (double?)DateTimeOffset.Parse("2026-11-10T12:00:00+09:00", CultureInfo.InvariantCulture).ToUnixTimeSeconds() : null });
            return UsagePresentation.Create(state, now, zone);
        }
        public Preferences Prefs { get { return prefs; } }
        public UsagePresentation Presentation { get { return presentation; } }
        internal void SetPresentation(UsagePresentation value) { presentation = value; }
        public DateTimeOffset StartedAt { get { return DateTimeOffset.Parse("2026-11-04T19:59:37+09:00", CultureInfo.InvariantCulture); } }
        public string PositionStatus { get { return prefs.Position == "sidebar" ? "사이드바 · 창 기준 위치" : "본문 제목 아래 · 자동 위치 확인됨"; } }
        public string ThemeStatus { get { return theme.IsDark ? "Codex · 다크 (미리보기)" : "Codex · 라이트 (미리보기)"; } }
        public VisualTheme Theme { get { return theme; } }
        public void SetPosition(string value) { prefs.Position = value; }
        public void SetCoordinates(int left, int vertical) { }
        public void ResetPosition() { prefs.ResetCurrentPosition(); }
        public void SetTheme(string value) { prefs.ThemeMode = value; }
        public void SetBadgeLayout(BadgeLayoutSettings value) { prefs.BadgeLayout = value.Copy(); }
        public void ManualRefresh() { }
        public void OpenReleases() { ReleasePageRequests++; }
        public void ActivateTarget() { }
        public void Quit() { }
    }
}
