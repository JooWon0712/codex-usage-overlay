using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class OverlayLayout
    {
        private const int TitleLeftGutter = 12;
        // Codex's desktop title menu has 7 DIP padding and about 1.5 DIP before the folder stroke.
        private const double TitleMenuFolderInset = 8.5;

        public static Rectangle CalculateBounds(Rectangle client, float scale, Preferences preferences, TitleAnchorObservation anchor = null)
        {
            var layout = preferences.BadgeLayout == null ? new BadgeLayoutSettings() : preferences.BadgeLayout.Copy();
            layout.Normalize();
            int width = (int)Math.Round(layout.Width * scale), height = (int)Math.Round(layout.Height * scale);
            if (client.Width < width || client.Height < height) return Rectangle.Empty;
            if (preferences.Position != "sidebar")
            {
                if (anchor == null || anchor.Status != "ok") return Rectangle.Empty;
                var header = Rectangle.Intersect(client, anchor.HeaderBounds);
                if (header.IsEmpty || anchor.TitleBounds.Width <= 0 || anchor.TitleBounds.Height <= 0 || !header.Contains(anchor.TitleBounds)) return Rectangle.Empty;
                int top = Math.Max(anchor.TitleBounds.Bottom, header.Bottom) + (int)Math.Round(Math.Max(0, preferences.TitleGap) * scale);
                int leftGutter = (int)Math.Round(TitleLeftGutter * scale);
                if (header.Width - leftGutter < width || client.Bottom - top < height) return Rectangle.Empty;
                bool hasTitleMenu = header.Contains(anchor.TitleMenuBounds) && anchor.TitleMenuBounds.Contains(anchor.TitleBounds);
                int baseLeft = hasTitleMenu ? anchor.TitleMenuBounds.Left : anchor.TitleBounds.Left;
                double inset = hasTitleMenu ? TitleMenuFolderInset : -Preferences.TitleLeadingInset;
                double desiredLeft = baseLeft + Math.Round((preferences.TitleOffsetX + inset) * scale);
                int left = (int)Math.Max(header.Left + leftGutter, Math.Min(desiredLeft, header.Right - width));
                return new Rectangle(left, top, width, height);
            }
            int bottomGap = preferences.EffectiveSidebarBottomGap;
            int sideLeft = client.Left + (int)Math.Round(preferences.SidebarX * scale);
            int sideTop = client.Bottom - (int)Math.Round(bottomGap * scale) - height;
            sideLeft = Math.Max(client.Left, Math.Min(sideLeft, client.Right - width));
            sideTop = Math.Max(client.Top, Math.Min(sideTop, client.Bottom - height));
            return new Rectangle(sideLeft, sideTop, width, height);
        }
    }
}
