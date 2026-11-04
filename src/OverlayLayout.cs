using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class OverlayLayout
    {
        public const int BadgeWidth = 224;
        public const int BadgeHeight = 46;
        public static Rectangle CalculateBounds(Rectangle client, float scale, Preferences preferences)
        {
            int width = Math.Min(client.Width, (int)Math.Round(BadgeWidth * scale));
            int height = Math.Min(client.Height, (int)Math.Round(BadgeHeight * scale));
            bool sidebar = preferences.Position != "title";
            int left = client.Left + (int)Math.Round((sidebar ? preferences.SidebarX : preferences.TitleX) * scale);
            int top = sidebar ? client.Bottom - (int)Math.Round(preferences.SidebarY * scale) : client.Top + (int)Math.Round(preferences.TitleY * scale);
            left = Math.Max(client.Left, Math.Min(left, client.Right - width));
            top = Math.Max(client.Top, Math.Min(top, client.Bottom - height));
            return new Rectangle(left, top, width, height);
        }
    }
}
