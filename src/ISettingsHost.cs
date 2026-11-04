namespace PersonalUsageOverlay
{
    // Keeps the settings view independent of account queries and process ownership.
    internal interface ISettingsHost
    {
        Preferences Prefs { get; }
        string MainText { get; }
        string Details { get; }
        string PositionStatus { get; }
        string ThemeStatus { get; }
        VisualTheme Theme { get; }
        UsagePresentation Presentation { get; }
        bool IsSample { get; }
        void SetPosition(string position);
        void SetCoordinates(int left, int vertical);
        void ResetPosition();
        void SetTheme(string mode);
        void SetBadgeLayout(BadgeLayoutSettings layout);
        void ManualRefresh();
        void ActivateTarget();
        void Quit();
    }
}
