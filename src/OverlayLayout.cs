using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class OverlayLayout
    {
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
                if (header.Width < width || client.Bottom - top < height) return Rectangle.Empty;
                double desiredLeft = anchor.TitleBounds.Left
                    + Math.Round((preferences.TitleOffsetX - (double)Preferences.TitleLeadingInset) * scale);
                int left = (int)Math.Max(header.Left, Math.Min(desiredLeft, header.Right - width));
                return new Rectangle(left, top, width, height);
            }
            int bottomGap = preferences.SidebarBottomGap ?? (int)Math.Max(0L, Math.Min(3000L, (long)preferences.SidebarY - 46));
            int sideLeft = client.Left + (int)Math.Round(preferences.SidebarX * scale);
            int sideTop = client.Bottom - (int)Math.Round(bottomGap * scale) - height;
            sideLeft = Math.Max(client.Left, Math.Min(sideLeft, client.Right - width));
            sideTop = Math.Max(client.Top, Math.Min(sideTop, client.Bottom - height));
            return new Rectangle(sideLeft, sideTop, width, height);
        }
    }
}
