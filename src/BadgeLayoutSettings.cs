using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    public sealed class BadgeElement
    {
        public string Key;
        public int X, Y, Width;
        public float FontSize;
        public bool Bold, Visible;
        internal int FixedHeight;
    }

    public sealed class BadgeLayoutSettings
    {
        internal const int DetailedHeaderHeight = 24;
        internal const int DetailedPadding = 14;
        internal const int DetailedWidth = 320;
        internal const int DetailedHeight = 78;
        internal const int DetailedCodexQuotaY = 44, DetailedCodexMeterY = 72;
        private const int CodexQuotaHeight = 27;
        private const int StackedDetailPadding = 16, StackedDetailHeaderHeight = 28;
        internal const int StackedCodexHeight = 89;
        private const int StackedCodexHeadingY = 25, StackedCodexHeadingHeight = 24;
        private const int StackedCodexQuotaY = 50, StackedCodexMeterY = 78;
        internal const int DetailedClaudeHeight = 120, DetailedClaudePeriodStride = 45;
        internal const int DetailedClaudeHeadingY = 7, DetailedClaudeHeadingHeight = 24;
        internal const int DetailedClaudeQuotaY = 32, DetailedClaudeQuotaHeight = 27;
        internal const int DetailedClaudeMeterY = 60;
        internal const int CompactClaudeBottomPadding = 5;
        private const int CompactWidthWithClaude = 344, CompactWidthWithReset = 292, CompactWidthWithoutReset = 216;
        private const int CompactHeaderHeight = 24, CompactCodexBodyHeight = 38;
        private const int CompactClaudeHeight = 50 + CompactClaudeBottomPadding;
        internal const float ResetFontSize = 12.5f;
        public int Width = DetailedWidth, Height = DetailedHeight;
        public int BackgroundTransparency = 0;
        public bool ShowPlan = true;
        public bool ShowClaude;
        public bool Compact { get; set; }
        public List<BadgeElement> Elements;
        internal bool ShowCodexReset { get { return OptionalVisible("weekly-reset"); } }
        internal bool ShowCountdown { get { return OptionalVisible("countdown"); } }
        internal bool StackedDetail { get { return ShowClaude && !Compact; } }
        internal int ContentPadding { get { return StackedDetail ? StackedDetailPadding : DetailedPadding; } }
        internal int HeaderHeight { get { return Compact ? CompactHeaderHeight : StackedDetail ? StackedDetailHeaderHeight : DetailedHeaderHeight; } }
        internal int CodexMeterY { get { return StackedDetail ? StackedCodexMeterY : DetailedCodexMeterY; } }
        internal int MeterHeight { get { return StackedDetail ? 4 : 3; } }
        internal int CodexHeight { get { return Height - (ShowClaude ? (Compact ? CompactClaudeHeight : DetailedClaudeHeight) : 0); } }

        public BadgeLayoutSettings()
        {
            Elements = FixedElements(true, true);
        }

        public void Normalize()
        {
            // Only these choices migrate from the retired drag editor. Rebuild the
            // geometry without mutating any list/element retained by its caller.
            bool date = ShowCodexReset;
            bool countdown = ShowCountdown;
            Width = Compact ? (ShowClaude ? CompactWidthWithClaude : date ? CompactWidthWithReset : CompactWidthWithoutReset) : DetailedWidth;
            int codexHeight = Compact ? HeaderHeight + CompactCodexBodyHeight : StackedDetail ? StackedCodexHeight : DetailedHeight;
            Height = codexHeight + (ShowClaude ? (Compact ? CompactClaudeHeight : DetailedClaudeHeight) : 0);
            int transparency = Math.Max(0, Math.Min(100, BackgroundTransparency));
            BackgroundTransparency = ((transparency + 5) / 10) * 10;
            // Keep the detailed display choices available when switching styles.
            Elements = FixedElements(date, countdown);
            if (StackedDetail)
            {
                int contentWidth = Width - 2 * ContentPadding;
                Elements.Add(new BadgeElement { Key = "claude-heading", X = ContentPadding, Y = codexHeight + DetailedClaudeHeadingY,
                    Width = contentWidth, FontSize = 12.5f, Bold = true, Visible = true });
                Elements.Add(new BadgeElement { Key = "claude-five-hour", X = ContentPadding, Y = codexHeight + DetailedClaudeQuotaY,
                    Width = contentWidth, FontSize = 12, Visible = true });
                Elements.Add(new BadgeElement { Key = "claude-weekly", X = ContentPadding, Y = codexHeight + DetailedClaudeQuotaY + DetailedClaudePeriodStride,
                    Width = contentWidth, FontSize = 12, Visible = true });
            }
        }

        public BadgeLayoutSettings Copy()
        {
            var copy = new BadgeLayoutSettings { Width = Width, Height = Height, BackgroundTransparency = BackgroundTransparency,
                ShowPlan = ShowPlan, ShowClaude = ShowClaude, Compact = Compact };
            if (Elements == null)
            {
                copy.Elements = null;
                return copy;
            }

            copy.Elements = new List<BadgeElement>();
            foreach (BadgeElement element in Elements)
                copy.Elements.Add(element == null ? null : new BadgeElement
                {
                    Key = element.Key,
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    FontSize = element.FontSize,
                    FixedHeight = element.FixedHeight,
                    Bold = element.Bold,
                    Visible = element.Visible
                });
            return copy;
        }

        public static int ElementHeight(BadgeElement element)
        {
            if (element != null && element.FixedHeight > 0)
                return element.FixedHeight;
            if (element != null && (element.Key == "remaining" || element.Key == "countdown"))
                return 20;
            if (element != null && element.Key == "claude-heading")
                return DetailedClaudeHeadingHeight;
            if (element != null && (element.Key == "claude-five-hour" || element.Key == "claude-weekly"))
                return DetailedClaudeQuotaHeight;
            float size = ValidFontSize(element == null ? 12f : element.FontSize, 12f);
            return (int)Math.Ceiling(size * 1.65d) + 1;
        }

        private static float ValidFontSize(float value, float fallback)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value))
                return fallback;
            return Math.Max(9f, Math.Min(28f, value));
        }

        private bool OptionalVisible(string key)
        {
            if (Elements != null)
                foreach (BadgeElement element in Elements)
                    if (element != null && element.Key == key)
                        return element.Visible;
            return true;
        }

        private List<BadgeElement> FixedElements(bool date, bool countdown)
        {
            int padding = ContentPadding;
            int width = DetailedWidth - 2 * padding;
            int quotaY = StackedDetail ? StackedCodexQuotaY : DetailedCodexQuotaY;
            return new List<BadgeElement>
            {
                new BadgeElement { Key = "remaining", X = padding, Y = quotaY, Width = width,
                    FontSize = 14, FixedHeight = CodexQuotaHeight, Bold = true, Visible = true },
                new BadgeElement { Key = "updated", X = padding, Y = StackedDetail ? StackedCodexHeadingY : DetailedHeaderHeight, Width = width,
                    FontSize = StackedDetail ? 12.5f : 11f, FixedHeight = StackedDetail ? StackedCodexHeadingHeight : 0, Visible = true },
                // Keep these keys so saved visibility choices still apply to the shared quota row.
                new BadgeElement { Key = "weekly-reset", X = padding, Y = quotaY, Width = width,
                    FontSize = StackedDetail ? 13f : ResetFontSize, FixedHeight = CodexQuotaHeight, Visible = date },
                new BadgeElement { Key = "countdown", X = padding, Y = quotaY, Width = width,
                    FontSize = StackedDetail ? 12.5f : 12f, FixedHeight = CodexQuotaHeight, Bold = true, Visible = countdown }
            };
        }
    }
}
