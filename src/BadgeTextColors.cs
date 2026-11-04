using System;
using System.Drawing;

namespace PersonalUsageOverlay
{
    // Badge foreground only. Settings and the badge body retain the chosen theme.
    internal sealed class BadgeTextColors
    {
        internal Color Text, Muted, Accent, Success, Attention, Danger, DangerText, OutlineColor;
        internal bool Bright, UseOutline, UseSoftShadow;

        internal static BadgeTextColors Resolve(VisualTheme theme, int transparency, bool highContrast)
        {
            theme = theme ?? VisualTheme.Dark;
            int percent = Math.Max(0, Math.Min(100, transparency));
            bool bright = !highContrast && (theme.IsDark || percent >= 50);
            VisualTheme foreground = bright ? VisualTheme.Dark : theme;
            var result = new BadgeTextColors {
                Text=foreground.Text, Muted=foreground.Muted, Accent=foreground.Accent,
                Success=foreground.Success, Attention=foreground.Attention,
                Danger=foreground.Danger, DangerText=foreground.DangerText,
                Bright=bright, UseOutline=highContrast || bright,
                UseSoftShadow=bright && !theme.IsDark,
                OutlineColor=bright ? VisualTheme.Dark.Surface : theme.Surface
            };
            if (bright && !theme.IsDark)
            {
                // A translucent light body can be much brighter than a dark
                // badge. Lift small labels as well as the main heading.
                result.Text=Color.FromArgb(0xf8,0xfa,0xfc);
                result.Muted=Color.FromArgb(0xe2,0xe8,0xf0);
                result.Attention=Color.FromArgb(0xff,0xe0,0xa3);
                result.Danger=Color.FromArgb(0xff,0xac,0xb8);
                result.DangerText=Color.FromArgb(0xff,0xc2,0xcd);
            }
            if (!highContrast && !theme.IsDark)
            {
                // Neutral ink uses one secondary tone for ordinary information
                // in both opacity profiles. Warning/failure colors keep their meaning.
                result.Success=result.Muted;
                result.Accent=result.Muted;
            }
            if (!highContrast && !bright)
            {
                // A black backdrop gives the darkest possible light badge body.
                // Do not sample or retain desktop pixels to choose foregrounds.
                Color floor = MinimumBody(theme.Surface, percent);
                result.Text=DarkenForContrast(result.Text,floor);
                result.Muted=DarkenForContrast(result.Muted,floor);
                result.Accent=DarkenForContrast(result.Accent,floor);
                result.Success=DarkenForContrast(result.Success,floor);
                result.Attention=DarkenForContrast(result.Attention,floor);
                result.Danger=DarkenForContrast(result.Danger,floor);
                result.DangerText=DarkenForContrast(result.DangerText,floor);
            }
            return result;
        }

        internal static Color MinimumBody(Color surface, int transparency)
        {
            int alpha=BadgeRenderer.BackgroundAlpha(transparency,false);
            return Color.FromArgb((surface.R*alpha+127)/255,(surface.G*alpha+127)/255,(surface.B*alpha+127)/255);
        }

        private static Color DarkenForContrast(Color color, Color background)
        {
            for (int strength=100;strength>=0;strength--)
            {
                Color candidate=Color.FromArgb(color.R*strength/100,color.G*strength/100,color.B*strength/100);
                if (Contrast(candidate,background)>=4.5) return candidate;
            }
            return Color.Black;
        }

        internal static double Contrast(Color first, Color second)
        {
            double a=Luminance(first), b=Luminance(second);
            return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
        }

        private static double Luminance(Color color)
        { return 0.2126*Linear(color.R)+0.7152*Linear(color.G)+0.0722*Linear(color.B); }

        private static double Linear(byte value)
        {
            double channel=value/255d;
            return channel<=0.04045 ? channel/12.92 : Math.Pow((channel+0.055)/1.055,2.4);
        }
    }
}
