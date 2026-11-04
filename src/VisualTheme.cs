using System.Drawing;

namespace PersonalUsageOverlay
{
    internal sealed class VisualTheme
    {
        public static readonly VisualTheme Dark = new VisualTheme(
            true,
            Color.FromArgb(0x10, 0x13, 0x18),
            Color.FromArgb(0x19, 0x1d, 0x24),
            Color.FromArgb(0x30, 0x35, 0x3e),
            Color.FromArgb(0xe6, 0xe8, 0xeb),
            Color.FromArgb(0x9d, 0xa4, 0xae),
            Color.FromArgb(0x25, 0x2b, 0x34),
            Color.FromArgb(0x30, 0x38, 0x43),
            Color.FromArgb(0xb4, 0xbd, 0xca),
            Color.FromArgb(0x91, 0xc9, 0xb6),
            Color.FromArgb(0xe8, 0xb9, 0x79),
            Color.FromArgb(0xed, 0x74, 0x82),
            Color.FromArgb(0xff, 0xb3, 0xbf));

        public static readonly VisualTheme Light = new VisualTheme(
            false,
            Color.FromArgb(0xf7, 0xf7, 0xf8),
            Color.FromArgb(0xff, 0xff, 0xff),
            Color.FromArgb(0xd6, 0xd8, 0xdd),
            Color.FromArgb(0x20, 0x23, 0x29),
            Color.FromArgb(0x61, 0x68, 0x73),
            Color.FromArgb(0xec, 0xee, 0xf1),
            Color.FromArgb(0xe0, 0xe3, 0xe8),
            Color.FromArgb(0x47, 0x54, 0x66),
            Color.FromArgb(0x24, 0x6c, 0x57),
            Color.FromArgb(0x81, 0x58, 0x0c),
            Color.FromArgb(0xb5, 0x2a, 0x42),
            Color.FromArgb(0x9e, 0x20, 0x38));

        public readonly bool IsDark;
        public readonly Color Background;
        public readonly Color Surface;
        public readonly Color Border;
        public readonly Color Text;
        public readonly Color Muted;
        public readonly Color Hover;
        public readonly Color Pressed;
        public readonly Color Accent;
        public readonly Color Success;
        public readonly Color Attention;
        public readonly Color Danger;
        public readonly Color DangerText;

        private VisualTheme(bool isDark, Color background, Color surface, Color border,
            Color text, Color muted, Color hover, Color pressed, Color accent, Color success,
            Color attention, Color danger, Color dangerText)
        {
            IsDark = isDark;
            Background = background;
            Surface = surface;
            Border = border;
            Text = text;
            Muted = muted;
            Hover = hover;
            Pressed = pressed;
            Accent = accent;
            Success = success;
            Attention = attention;
            Danger = danger;
            DangerText = dangerText;
        }

        public static VisualTheme For(bool dark, bool highContrast)
        {
            if (!highContrast)
            {
                return dark ? Dark : Light;
            }

            // Keep one system foreground/background pair for surfaces and states.
            // In high contrast, do not introduce muted text or tinted state fills.
            Color background = SystemColors.Window;
            Color foreground = SystemColors.WindowText;
            return new VisualTheme(background.GetBrightness() < 0.5f,
                background, background, foreground, foreground, foreground,
                background, background, SystemColors.Highlight, foreground,
                foreground, foreground, foreground);
        }
    }
}
