using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class OverlayLayout
    {
        private const int TitleLeftGutter = 12;
        private const float MinimumStackedScale = 0.8f;
        // Codex's desktop title menu has 7 DIP padding and about 1.5 DIP before the folder stroke.
        private const double TitleMenuFolderInset = 8.5;

        public static Rectangle CalculateBounds(Rectangle client, float scale, Preferences preferences, TitleAnchorObservation anchor = null)
        {
            float badgeScale;
            return CalculateBounds(client, scale, preferences, anchor, out badgeScale);
        }

        public static Rectangle CalculateBounds(Rectangle client, float scale, Preferences preferences, TitleAnchorObservation anchor, out float badgeScale)
        {
            var layout = preferences.BadgeLayout == null ? new BadgeLayoutSettings() : preferences.BadgeLayout.Copy();
            layout.Normalize();
            badgeScale = scale;
            if (preferences.Position != "sidebar")
            {
                if (anchor == null || anchor.Status != "ok") return Rectangle.Empty;
                var header = Rectangle.Intersect(client, anchor.HeaderBounds);
                if (header.IsEmpty || anchor.TitleBounds.Width <= 0 || anchor.TitleBounds.Height <= 0 || !header.Contains(anchor.TitleBounds)) return Rectangle.Empty;
                int top = Math.Max(anchor.TitleBounds.Bottom, header.Bottom) + (int)Math.Round(Math.Max(0, preferences.TitleGap) * scale);
                int leftGutter = (int)Math.Round(TitleLeftGutter * scale);
                Size size = FitBadge(layout, scale, new Size(header.Width - leftGutter, client.Bottom - top), out badgeScale);
                if (size.IsEmpty) return Rectangle.Empty;
                bool hasTitleMenu = header.Contains(anchor.TitleMenuBounds) && anchor.TitleMenuBounds.Contains(anchor.TitleBounds);
                int baseLeft = hasTitleMenu ? anchor.TitleMenuBounds.Left : anchor.TitleBounds.Left;
                double inset = hasTitleMenu ? TitleMenuFolderInset : -Preferences.TitleLeadingInset;
                double desiredLeft = baseLeft + Math.Round((preferences.TitleOffsetX + inset) * scale);
                int left = (int)Math.Max(header.Left + leftGutter, Math.Min(desiredLeft, header.Right - size.Width));
                return new Rectangle(new Point(left, top), size);
            }
            Size sidebarSize = FitBadge(layout, scale, client.Size, out badgeScale);
            if (sidebarSize.IsEmpty) return Rectangle.Empty;
            int bottomGap = preferences.EffectiveSidebarBottomGap;
            int sideLeft = client.Left + (int)Math.Round(preferences.SidebarX * scale);
            int sideTop = client.Bottom - (int)Math.Round(bottomGap * scale) - sidebarSize.Height;
            sideLeft = Math.Max(client.Left, Math.Min(sideLeft, client.Right - sidebarSize.Width));
            sideTop = Math.Max(client.Top, Math.Min(sideTop, client.Bottom - sidebarSize.Height));
            return new Rectangle(new Point(sideLeft, sideTop), sidebarSize);
        }

        private static Size FitBadge(BadgeLayoutSettings layout, float dpiScale, Size space, out float badgeScale)
        {
            badgeScale = dpiScale;
            if (layout.StackedDetail)
            {
                // Scale both periods together while keeping the text readable.
                float minimumScale = dpiScale * MinimumStackedScale;
                float fitted = Math.Min((float)space.Width / layout.Width, (float)space.Height / layout.Height);
                badgeScale = Math.Min(dpiScale, Math.Max(minimumScale, fitted));
            }

            var size = new Size((int)Math.Round(layout.Width * badgeScale), (int)Math.Round(layout.Height * badgeScale));
            return size.Width <= space.Width && size.Height <= space.Height ? size : Size.Empty;
        }
    }
}
