namespace PersonalUsageOverlay
{
    // Developer rendering only: no application context, worker, account reads or disk writes.
    internal sealed class SettingsPreviewHost : ISettingsHost
    {
        private readonly Preferences prefs = new Preferences();
        private readonly VisualTheme theme;
        public SettingsPreviewHost(VisualTheme theme) { this.theme = theme; }
        public Preferences Prefs { get { return prefs; } }
        public string MainText { get { return "주간 75% · 5시간 98% 남음"; } }
        public string Details { get { return "연결된 Codex 계정의 한도\n갱신 12:34:56 · Codex 계정\n주간 초기화: 11/10 12:00:00 +09:00\n5시간 초기화: 11/04 17:00:00 +09:00"; } }
        public string PositionStatus { get { return "본문 제목 아래 · 자동 위치 확인됨"; } }
        public string ThemeStatus { get { return theme.IsDark ? "Codex · 다크 (미리보기)" : "Codex · 라이트 (미리보기)"; } }
        public VisualTheme Theme { get { return theme; } }
        public void SetPosition(string value) { }
        public void SetCoordinates(int left, int vertical) { }
        public void ResetPosition() { }
        public void SetTheme(string value) { }
        public void ManualRefresh() { }
        public void ActivateTarget() { }
        public void Quit() { }
    }
}
