using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class OverlayLayout
    {
        public const int BadgeWidth = 224;
        public const int BadgeHeight = 46;
        public static Rectangle CalculateBounds(Rectangle client, float scale, Preferences preferences, TitleAnchorObservation anchor = null)
        {
            int width = Math.Min(client.Width, (int)Math.Round(BadgeWidth * scale));
            int height = Math.Min(client.Height, (int)Math.Round(BadgeHeight * scale));
            bool sidebar = preferences.Position == "sidebar";
            if (!sidebar)
            {
                if (anchor == null || anchor.Status != "ok")
                    return Rectangle.Empty;
                var header = Rectangle.Intersect(client, anchor.HeaderBounds);
                var title = anchor.TitleBounds;
                if (header.IsEmpty || title.Width <= 0 || title.Height <= 0 || !header.Contains(title))
                    return Rectangle.Empty;
                int topBelow = Math.Max(title.Bottom, header.Bottom) + (int)Math.Round(Math.Max(0, preferences.TitleGap) * scale);
                width = Math.Min(width, header.Width);
                height = Math.Min(height, client.Bottom - topBelow);
                if (width < 80 || height < 25)
                    return Rectangle.Empty;
                int titleLeft = title.Left + (int)Math.Round(preferences.TitleOffsetX * scale);
                titleLeft = Math.Max(header.Left, Math.Min(titleLeft, header.Right - width));
                return new Rectangle(titleLeft, topBelow, width, height);
            }
            int left = client.Left + (int)Math.Round(preferences.SidebarX * scale);
            int top = client.Bottom - (int)Math.Round(preferences.SidebarY * scale);
            left = Math.Max(client.Left, Math.Min(left, client.Right - width));
            top = Math.Max(client.Top, Math.Min(top, client.Bottom - height));
            return new Rectangle(left, top, width, height);
        }
    }
}
