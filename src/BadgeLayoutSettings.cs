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
    }

    public sealed class BadgeLayoutSettings
    {
        public int Width = 320, Height = 82;
        public List<BadgeElement> Elements;

        public BadgeLayoutSettings()
        {
            Elements = Defaults();
        }

        public void Normalize()
        {
            Width = Clamp(Width, 180, 600);
            Height = Clamp(Height, 60, 300);
            List<BadgeElement> defaults = Defaults();
            var known = new Dictionary<string, BadgeElement>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BadgeElement element in defaults)
                known.Add(element.Key, element);

            var normalized = new List<BadgeElement>();
            if (Elements != null)
                foreach (BadgeElement element in Elements)
                    if (element != null && element.Key != null && known.ContainsKey(element.Key)
                        && seen.Add(element.Key))
                        normalized.Add(element);

            foreach (BadgeElement fallback in defaults)
                if (seen.Add(fallback.Key))
                    normalized.Add(fallback);

            foreach (BadgeElement element in normalized)
            {
                BadgeElement fallback = known[element.Key];
                element.FontSize = ValidFontSize(element.FontSize, fallback.FontSize);
                element.Width = element.Key == "indicator" ? 18 : Clamp(element.Width, 18, Width);
                element.X = Clamp(element.X, 0, Width - element.Width);
                element.Y = Clamp(element.Y, 0, Height - ElementHeight(element));
                if (element.Key == "remaining" || element.Key == "updated")
                    element.Visible = true;
            }

            Elements = normalized;
        }

        public BadgeLayoutSettings Copy()
        {
            var copy = new BadgeLayoutSettings { Width = Width, Height = Height };
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
                    Bold = element.Bold,
                    Visible = element.Visible
                });
            return copy;
        }

        public static int ElementHeight(BadgeElement element)
        {
            if (element != null && element.Key == "indicator")
                return 18;
            float size = ValidFontSize(element == null ? 12f : element.FontSize, 12f);
            return (int)Math.Ceiling(size * 1.65d) + 2;
        }

        private static float ValidFontSize(float value, float fallback)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value))
                return fallback;
            return Math.Max(9f, Math.Min(28f, value));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static List<BadgeElement> Defaults()
        {
            return new List<BadgeElement>
            {
                new BadgeElement { Key = "indicator", X = 10, Y = 8, Width = 18, FontSize = 12, Visible = true },
                new BadgeElement { Key = "remaining", X = 32, Y = 6, Width = 278, FontSize = 14, Bold = true, Visible = true },
                new BadgeElement { Key = "updated", X = 10, Y = 31, Width = 300, FontSize = 11, Visible = true },
                new BadgeElement { Key = "weekly-reset", X = 10, Y = 53, Width = 212, FontSize = 11, Visible = true },
                new BadgeElement { Key = "countdown", X = 232, Y = 53, Width = 78, FontSize = 12, Bold = true, Visible = true }
            };
        }
    }
}
