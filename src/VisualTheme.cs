using System.Drawing;

namespace PersonalUsageOverlay
{
    internal sealed class VisualTheme
    {
        public static readonly VisualTheme Dark = new VisualTheme(
            true,
            Color.FromArgb(0x17, 0x1b, 0x22),
            Color.FromArgb(0x24, 0x2a, 0x34),
            Color.FromArgb(0x41, 0x4b, 0x59),
            Color.FromArgb(0xe7, 0xeb, 0xf0),
            Color.FromArgb(0xb5, 0xbe, 0xc9),
            Color.FromArgb(0x2e, 0x37, 0x44),
            Color.FromArgb(0x35, 0x41, 0x50),
            Color.FromArgb(0xa8, 0xb7, 0xcc),
            Color.FromArgb(0xb5, 0xbe, 0xc9),
            Color.FromArgb(0xff, 0xe0, 0xa3),
            Color.FromArgb(0xff, 0xac, 0xb8),
            Color.FromArgb(0xff, 0xc2, 0xcd));

        public static readonly VisualTheme Light = new VisualTheme(
            false,
            Color.FromArgb(0xcf, 0xd3, 0xd8),
            Color.FromArgb(0xdb, 0xde, 0xe2),
            Color.FromArgb(0xa7, 0xaf, 0xb9),
            Color.FromArgb(0x20, 0x26, 0x2e),
            Color.FromArgb(0x44, 0x4f, 0x5d),
            Color.FromArgb(0xc4, 0xca, 0xd2),
            Color.FromArgb(0xba, 0xc2, 0xcd),
            Color.FromArgb(0x3f, 0x4c, 0x5c),
            Color.FromArgb(0x44, 0x4f, 0x5d),
            Color.FromArgb(0x79, 0x51, 0x0c),
            Color.FromArgb(0xa6, 0x2b, 0x41),
            Color.FromArgb(0x92, 0x26, 0x3a));

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
