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
            Elements = FixedElements(true, true, true);
        }

        public void Normalize()
        {
            // Only these choices migrate from the retired drag editor. Rebuild the
            // geometry without mutating any list/element retained by its caller.
            bool indicator = OptionalVisible("indicator");
            bool date = OptionalVisible("weekly-reset");
            bool countdown = OptionalVisible("countdown");
            Width = 320;
            Height = date || countdown ? 82 : 58;
            Elements = FixedElements(indicator, date, countdown);
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
            // At the fixed 14px heading size, 25 DIPs end exactly where row two starts.
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

        private static List<BadgeElement> FixedElements(bool indicator, bool date, bool countdown)
        {
            return new List<BadgeElement>
            {
                new BadgeElement { Key = "indicator", X = 10, Y = 8, Width = 18, FontSize = 12, Visible = indicator },
                new BadgeElement { Key = "remaining", X = indicator ? 32 : 10, Y = 6, Width = indicator ? 278 : 300, FontSize = 14, Bold = true, Visible = true },
                new BadgeElement { Key = "updated", X = 10, Y = 31, Width = 300, FontSize = 11, Visible = true },
                new BadgeElement { Key = "weekly-reset", X = 10, Y = 53, Width = countdown ? 212 : 300, FontSize = 11, Visible = date },
                new BadgeElement { Key = "countdown", X = date ? 232 : 10, Y = 53, Width = date ? 78 : 300, FontSize = 12, Bold = true, Visible = countdown }
            };
        }
    }
}
