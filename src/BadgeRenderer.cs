using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeRenderer
    {
        private const int QuotaLabelSize = 12, QuotaValueSize = 16, QuotaGap = 6;
        private const int ResetTextSize = 12, MinimumResetTextSize = 11, ResetGap = 4;
        private const string WeeklySession = "주간 세션", CurrentSession = "현재 세션";
        internal sealed class SessionRowPlacement
        {
            internal Rectangle Period, Value, Reset, Countdown;
            internal int ResetFontSize = ResetTextSize;
            internal string PeriodText, ValueText, ResetText, CountdownText;
            internal bool ShowResetIcon;
        }

        internal static SessionRowPlacement CalculateCodexSessionRow(Graphics graphics, Rectangle bounds, float scale,
            UsagePresentation presentation, BadgeLayoutSettings layout)
        {
            double? remaining = presentation == null ? null : presentation.CodexRemaining;
            return CalculateSessionRow(graphics, bounds, scale, remaining.HasValue ? WeeklySession : null,
                remaining.HasValue ? PercentText(remaining.Value) : TextOr(presentation == null ? null : presentation.MainText, "한도 확인 중"),
                layout.ShowCodexReset ? DetailedCodexResetText(presentation) : null,
                layout.ShowCodexReset && presentation != null && presentation.CodexResetTimeText != null,
                layout.ShowCountdown ? DetailedCodexCountdownText(presentation, layout.ShowCodexReset) : null);
        }

        internal static SessionRowPlacement CalculateSessionRow(Graphics graphics, Rectangle bounds, float scale,
            string period, string value, string resetText, bool showResetIcon, string countdown)
        {
            var row = new SessionRowPlacement
            {
                PeriodText = period, ValueText = value, ResetText = resetText, CountdownText = countdown, ShowResetIcon = showResetIcon
            };
            int gap = ScaledGap(QuotaGap, scale);
            int safety = ScaledGap(2, scale);
            int valueLeft = bounds.Left;
            int valueWidth = ScaledGap(48, scale);
            if (!String.IsNullOrEmpty(period))
            {
                using (var labelFont = new Font("Segoe UI", QuotaLabelSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    int labelWidth = Math.Max(MeasureTextWidth(graphics, WeeklySession, labelFont),
                        MeasureTextWidth(graphics, CurrentSession, labelFont)) + safety;
                    row.Period = new Rectangle(bounds.Left, bounds.Top, labelWidth, bounds.Height);
                    valueLeft = row.Period.Right + gap;
                }
                using (var valueFont = new Font("Segoe UI", QuotaValueSize * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    valueWidth = Math.Max(MeasureTextWidth(graphics, "99.9%", valueFont),
                        MeasureTextWidth(graphics, value, valueFont)) + safety;
            }

            row.Value = Rectangle.FromLTRB(valueLeft, bounds.Top, bounds.Right, bounds.Bottom);
            int available = bounds.Right - valueLeft - valueWidth - gap;
            bool hasReset = !String.IsNullOrEmpty(resetText), hasCountdown = !String.IsNullOrEmpty(countdown);
            if (available <= 0 || (!hasReset && !hasCountdown)) return row;

            int textGap = hasReset && hasCountdown ? ScaledGap(ResetGap, scale) : 0;
            int inset = showResetIcon ? ResetIconInset(scale) : 0;
            for (int size = ResetTextSize; size >= MinimumResetTextSize; size--)
            {
                using (var timeFont = new Font("Segoe UI", size * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var timerFont = new Font("Segoe UI", size * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    int timeWidth = hasReset ? inset + MeasureTextWidth(graphics, resetText, timeFont) + safety : 0;
                    int timerWidth = hasCountdown ? MeasureTextWidth(graphics, countdown, timerFont) + safety : 0;
                    bool fits = timeWidth + textGap + timerWidth <= available;
                    if (!fits && size > MinimumResetTextSize) continue;

                    row.ResetFontSize = size;
                    int resetRight = bounds.Right, resetBottom = bounds.Bottom, timerTop = bounds.Top;
                    if (fits && hasReset && hasCountdown)
                    {
                        int referenceWidth = MeasureTextWidth(graphics, "(0h 00m)", timerFont) + safety;
                        int timerColumn = Math.Min(available - timeWidth - textGap, Math.Max(timerWidth, referenceWidth));
                        resetRight -= timerColumn + textGap;
                    }
                    else if (!fits && hasReset && hasCountdown)
                    {
                        // Unusually long dates or countdowns share the right side on two lines.
                        resetBottom = timerTop = bounds.Top + bounds.Height / 2;
                    }
                    int limit = bounds.Right - available;
                    if (hasReset)
                        row.Reset = Rectangle.FromLTRB(Math.Max(limit, resetRight - timeWidth), bounds.Top, resetRight, resetBottom);
                    if (hasCountdown)
                        row.Countdown = Rectangle.FromLTRB(Math.Max(limit, bounds.Right - timerWidth), timerTop, bounds.Right, bounds.Bottom);
                    int firstDetail = Math.Min(hasReset ? row.Reset.Left : bounds.Right, hasCountdown ? row.Countdown.Left : bounds.Right);
                    row.Value.Width = firstDetail - gap - valueLeft;
                    return row;
                }
            }
            return row;
        }

        internal static string DetailedCodexResetText(UsagePresentation presentation)
        {
            return presentation == null ? "초기화 확인 불가" : presentation.CodexResetTimeText ?? presentation.ResetDateText;
        }

        internal static string DetailedCodexCountdownText(UsagePresentation presentation, bool resetVisible)
        {
            if (presentation == null) return resetVisible ? null : "확인 불가";
            if (presentation.CodexResetTimeText != null)
                return CountdownInParentheses(resetVisible ? presentation.CodexResetDay : presentation.CountdownText);
            return resetVisible ? null : presentation.CountdownText;
        }

        internal static int ResetIconInset(float scale, bool compact = false)
        {
            return (int)Math.Round((compact ? 12 : 16) * scale) + ScaledGap(ResetGap, scale);
        }

        private static int ScaledGap(int size, float scale)
        {
            return Math.Max(1, (int)Math.Round(size * scale));
        }

        private static string CountdownInParentheses(string countdown)
        {
            return String.IsNullOrEmpty(countdown) ? null : "(" + countdown + ")";
        }

        public static RectangleF ElementBounds(BadgeElement element)
        {
            if (element == null)
                return RectangleF.Empty;
            return new RectangleF(element.X, element.Y, element.Width, BadgeLayoutSettings.ElementHeight(element));
        }

        internal static byte BackgroundAlpha(int transparency, bool highContrast)
        { return highContrast ? (byte)255 : (byte)Math.Round(255d * (100-Math.Max(0, Math.Min(100,transparency)))/100d); }

        internal static int MeasureTextWidth(Graphics graphics, string text, Font font)
        {
            using (var format = TextFormat())
                return Math.Max(1, (int)Math.Ceiling(graphics.MeasureString(text, font, Int32.MaxValue, format).Width));
        }

        private static StringFormat TextFormat()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.Alignment = StringAlignment.Near; format.LineAlignment = StringAlignment.Center;
            return format;
        }

        internal static Bitmap CreateBitmap(Size size, float scale, VisualTheme theme, UsagePresentation presentation,
            BadgeLayoutSettings layout, ServiceIcons icons = null)
        {
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                { graphics.Clear(Color.Transparent); Draw(graphics, new Rectangle(Point.Empty,size), scale,theme,presentation,layout,icons); }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        internal static Bitmap CreateTextBitmap(Size size, float scale, VisualTheme theme, UsagePresentation presentation,
            BadgeLayoutSettings layout, ServiceIcons icons = null)
        {
            theme = theme ?? VisualTheme.Dark;
            Bitmap bitmap = CreateGlyphBitmap(size, scale, theme, presentation, layout, icons);
            try
            {
                var normalized = layout == null ? new BadgeLayoutSettings() : layout.Copy();
                normalized.Normalize();
                BadgeTextColors colors = BadgeTextColors.Resolve(theme, normalized.BackgroundTransparency, SystemInformation.HighContrast);
                if (colors.UseOutline) AddTextUnderlay(bitmap, scale, colors.OutlineColor, colors.UseSoftShadow);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        // Keep the original antialiased glyph coverage, including thin strokes.
        // It is also used to check the rendered text against the unmodified font.
        internal static Bitmap CreateGlyphBitmap(Size size, float scale, VisualTheme theme, UsagePresentation presentation,
            BadgeLayoutSettings layout, ServiceIcons icons = null)
        {
            theme=theme??VisualTheme.Dark;
            var normalized=layout==null?new BadgeLayoutSettings():layout.Copy(); normalized.Normalize();
            var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
            try
            {
                using(var graphics=Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                    var colors = BadgeTextColors.Resolve(theme, normalized.BackgroundTransparency, SystemInformation.HighContrast);
                    Bitmap codex = icons == null ? null : (theme.IsDark || colors.Bright ? icons.Codex : icons.CodexLight ?? icons.Codex);
                    Bitmap claude = icons == null || !normalized.ShowClaude ? null : icons.Claude;
                    DrawText(graphics, new Rectangle(Point.Empty, size), scale, colors, presentation, normalized, codex, claude);
                }
                return bitmap;
            }
            catch {bitmap.Dispose();throw;}
        }

        internal static int ShadowOffsetY(float scale)
        { return Math.Max(1, (int)Math.Round(scale / 2d)); }

        private static void AddTextUnderlay(Bitmap bitmap, float scale, Color surface, bool softShadow)
        {
            var bounds = new Rectangle(Point.Empty, bitmap.Size);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = checked(bitmap.Width * 4);
                var glyph = new byte[checked(stride * bitmap.Height)];
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), glyph, y * stride, stride);
                var outlined = (byte[])glyph.Clone();
                double radius = Math.Max(1d, scale);
                int reach = (int)Math.Ceiling(radius + 1d);
                var taps = new List<OutlineTap>();
                double totalWeight = 0;
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        double weight = Math.Min(1d, radius + 1d - Math.Sqrt(dx * dx + dy * dy));
                        if (weight > 0)
                        {
                            taps.Add(new OutlineTap { X = dx, Y = dy, Weight = weight });
                            totalWeight += weight;
                        }
                    }
                var outline = new byte[checked(bitmap.Width * bitmap.Height)];
                var shadow = softShadow ? new double[outline.Length] : null;
                int shiftY = softShadow ? ShadowOffsetY(scale) : 0;
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int alpha = glyph[y * stride + x * 4 + 3];
                        if (alpha == 0) continue;
                        foreach (OutlineTap tap in taps)
                        {
                            int tx = x + tap.X, ty = y + tap.Y + shiftY;
                            if (tx < 0 || tx >= bitmap.Width || ty < 0 || ty >= bitmap.Height) continue;
                            int target = ty * bitmap.Width + tx;
                            if (softShadow) shadow[target] += alpha * tap.Weight;
                            else outline[target] = (byte)Math.Max(outline[target], (int)Math.Round(alpha * tap.Weight));
                        }
                    }
                // Both neutral themes use a small, blurred drop shadow for
                // bright text. High contrast retains its system-colored outline.
                // Keep the original font coverage; never threshold its edges.
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int offset = y * stride + x * 4;
                        int alpha = glyph[offset + 3];
                        int pixel = y * bitmap.Width + x;
                        int outlineAlpha = softShadow ? (int)Math.Round(shadow[pixel] * 0.85d / totalWeight) : outline[pixel];
                        int underlayAlpha = (outlineAlpha * (255 - alpha) + 127) / 255;
                        outlined[offset] = (byte)Math.Min(255, glyph[offset] + (surface.B * underlayAlpha + 127) / 255);
                        outlined[offset + 1] = (byte)Math.Min(255, glyph[offset + 1] + (surface.G * underlayAlpha + 127) / 255);
                        outlined[offset + 2] = (byte)Math.Min(255, glyph[offset + 2] + (surface.R * underlayAlpha + 127) / 255);
                        outlined[offset + 3] = (byte)(alpha + underlayAlpha);
                    }
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(outlined, y * stride, IntPtr.Add(data.Scan0, y * data.Stride), stride);
            }
            finally { bitmap.UnlockBits(data); }
        }

        private struct OutlineTap
        {
            internal int X, Y;
            internal double Weight;
        }

        public static void Draw(Graphics g, Rectangle bounds, float scale, VisualTheme theme,
            UsagePresentation presentation, BadgeLayoutSettings layout, ServiceIcons icons = null)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0)
                return;
            if (Single.IsNaN(scale) || Single.IsInfinity(scale) || scale <= 0)
                scale = 1f;
            theme = theme ?? VisualTheme.Dark;
            BadgeLayoutSettings normalized = layout == null ? new BadgeLayoutSettings() : layout.Copy();
            normalized.Normalize();
            double physicalWidth = (double)normalized.Width * scale;
            double physicalHeight = (double)normalized.Height * scale;
            if (physicalWidth > Int32.MaxValue || physicalHeight > Int32.MaxValue
                || physicalWidth < 1d || physicalHeight < 1d)
                return;

            var canvas = new RectangleF(bounds.Left, bounds.Top, (float)physicalWidth, (float)physicalHeight);
            RectangleF clip = RectangleF.Intersect(canvas, bounds);
            if (clip.Width <= 0 || clip.Height <= 0)
                return;
            GraphicsState saved = g.Save();
            try
            {
                g.ResetTransform();
                g.SetClip(clip, CombineMode.Intersect);
                DrawSurface(g, canvas, scale, theme, presentation, normalized);

                using(var text=CreateTextBitmap(new Size((int)Math.Round(physicalWidth),(int)Math.Round(physicalHeight)),scale,theme,presentation,normalized,icons))
                    g.DrawImageUnscaled(text,bounds.Location);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        internal static Bitmap CreateSurfaceBitmap(Size size, float scale, VisualTheme theme, UsagePresentation presentation, BadgeLayoutSettings layout)
        {
            var normalized = layout == null ? new BadgeLayoutSettings() : layout.Copy();
            normalized.Normalize();
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    DrawSurface(graphics, new RectangleF(0, 0, normalized.Width * scale, normalized.Height * scale),
                        scale, theme ?? VisualTheme.Dark, presentation, normalized);
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        private static void DrawSurface(Graphics g, RectangleF canvas, float scale, VisualTheme theme,
            UsagePresentation presentation, BadgeLayoutSettings layout)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool highContrast = SystemInformation.HighContrast;
            bool low = presentation != null && presentation.Tone == UsageTone.Low;
            bool dark = theme.IsDark;
            byte alpha = BackgroundAlpha(layout.BackgroundTransparency, highContrast);
            float inset = 0.7f * scale;
            var surface = RectangleF.Inflate(canvas, -inset, -inset);
            float padding = layout.ContentPadding * scale;
            bool stackedDark = layout.StackedDetail && dark && !highContrast;
            Color surfaceColor = stackedDark ? Color.FromArgb(0x22, 0x28, 0x31) : theme.Surface;
            Color borderColor = stackedDark ? Color.FromArgb(0x3a, 0x44, 0x52) : theme.Border;
            using (GraphicsPath path = Rounded(surface, (layout.StackedDetail ? 13f : 9f) * scale))
            using (var fill = new SolidBrush(Color.FromArgb(alpha, surfaceColor)))
            using (var border = new Pen(Color.FromArgb(alpha, borderColor), Math.Max(1f, scale)))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
                if (layout.ShowClaude)
                {
                    float y = canvas.Top + layout.CodexHeight * scale;
                    g.DrawLine(border, canvas.Left + padding, y, canvas.Right - padding, y);
                }
            }

            if (layout.Compact || presentation == null) return;

            // Each track is an opaque data mark. The badge transparency affects its surfaces only.
            dark = dark || layout.BackgroundTransparency >= 50;
            Color track = highContrast ? theme.Surface : dark ? Color.FromArgb(0x3a, 0x46, 0x54) : Color.FromArgb(0xbf, 0xc7, 0xd1);
            Color codex = highContrast ? theme.Text : low
                ? (dark ? Color.FromArgb(0xff, 0x9c, 0xb2) : Color.FromArgb(0x92, 0x34, 0x49))
                : (dark ? Color.FromArgb(0x9b, 0xc4, 0xed) : Color.FromArgb(0x30, 0x59, 0x78));
            Color claude = highContrast ? theme.Text : dark ? Color.FromArgb(0xc3, 0xab, 0xdd) : Color.FromArgb(0x66, 0x52, 0x7e);
            Color edge = highContrast ? theme.Border : Color.Empty;
            DrawMeter(g, canvas, scale, new RectangleF(layout.ContentPadding, layout.CodexMeterY,
                layout.Width - 2 * layout.ContentPadding, layout.MeterHeight), presentation.CodexBarRemaining, track, codex, edge);
            if (layout.ShowClaude && presentation.Claude != null)
            {
                var trackBounds = new RectangleF(layout.ContentPadding, layout.CodexHeight + BadgeLayoutSettings.DetailedClaudeMeterY,
                    layout.Width - 2 * layout.ContentPadding, layout.MeterHeight);
                DrawMeter(g, canvas, scale, trackBounds, presentation.Claude.FiveHourRemaining, track, claude, edge);
                trackBounds.Y += BadgeLayoutSettings.DetailedClaudePeriodStride;
                DrawMeter(g, canvas, scale, trackBounds, presentation.Claude.WeeklyRemaining, track, claude, edge);
            }
        }

        private static void DrawMeter(Graphics g, RectangleF canvas, float scale, RectangleF bounds,
            double? remaining, Color track, Color fill, Color edge)
        {
            if (!remaining.HasValue) return;
            var physical = new RectangleF(canvas.X + bounds.X * scale, canvas.Y + bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
            float radius = physical.Height / 2;
            using (var path = Rounded(physical, radius))
            using (var background = new SolidBrush(track))
            {
                g.FillPath(background, path);
                if (remaining.Value > 0)
                {
                    var filled = new RectangleF(physical.X, physical.Y, physical.Width * (float)(remaining.Value / 100), physical.Height);
                    using (var fillPath = Rounded(filled, radius))
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, fillPath);
                }
                if (!edge.IsEmpty)
                    using (var pen = new Pen(edge))
                        g.DrawPath(pen, path);
            }
        }

        private static void DrawText(Graphics g, Rectangle bounds, float scale, BadgeTextColors theme,
            UsagePresentation presentation, BadgeLayoutSettings layout, Bitmap codexIcon, Bitmap claudeIcon)
        {
            Rectangle chevron = BadgeFormatButton.CalculateBounds(bounds, layout, scale);
            if (!chevron.IsEmpty)
                BadgeFormatButton.DrawChevron(g, chevron, scale, theme.Muted, layout.Compact);

            if (layout.Compact)
            {
                DrawCompactText(g, bounds, scale, theme, presentation, layout, codexIcon, claudeIcon);
                return;
            }

            UsageTone tone = presentation == null ? UsageTone.Attention : presentation.Tone;
            Color stateColor = tone == UsageTone.Low ? theme.Danger : tone == UsageTone.Normal ? theme.Success : theme.Attention;
            Color quotaColor = tone == UsageTone.Low ? theme.DangerText : theme.Text;
            var claude = (presentation == null ? null : presentation.Claude) ?? ClaudeUsageDisplay.Awaiting;
            foreach (BadgeElement element in layout.Elements)
            {
                if (!element.Visible) continue;
                RectangleF logical = ElementBounds(element);
                var physical = Rectangle.Round(new RectangleF(bounds.Left + logical.Left * scale,
                    bounds.Top + logical.Top * scale, logical.Width * scale, logical.Height * scale));
                switch (element.Key)
                {
                    case "remaining":
                        var codex = CalculateCodexSessionRow(g, physical, scale, presentation, layout);
                        DrawDetailedSessionRow(g, codex, scale, codex.Period.IsEmpty ? element.FontSize : QuotaValueSize, true,
                            quotaColor, tone == UsageTone.Low ? quotaColor : theme.Muted, quotaColor, theme.Muted);
                        break;
                    case "updated":
                        string text = TextOr(presentation == null ? null : presentation.BadgeDetailText(layout.ShowPlan), "연결 대기");
                        if (text.EndsWith(" · Codex 계정", StringComparison.Ordinal))
                            text = text.Substring(0, text.Length - " · Codex 계정".Length);
                        if (presentation != null && presentation.CodexRemaining.HasValue && !String.IsNullOrEmpty(presentation.CodexOtherPeriodsText))
                            text += " · " + presentation.CodexOtherPeriodsText;
                        DrawDetailedServiceHeading(g, physical, codexIcon, scale, "Codex", text, theme.Text, stateColor, layout.StackedDetail);
                        break;
                    case "claude-heading":
                        string claudeDetail = layout.ShowPlan && claude.PlanText != null ? claude.PlanText + " · " + claude.Status : claude.Status;
                        DrawDetailedServiceHeading(g, physical, claudeIcon, scale, "Claude Code", claudeDetail, theme.Text, theme.Muted, true);
                        break;
                    case "claude-five-hour":
                    case "claude-weekly":
                        DrawClaudeSession(g, physical, scale, claude, element.Key == "claude-five-hour", theme);
                        break;
                }
            }
        }

        private static void DrawClaudeSession(Graphics g, Rectangle bounds, float scale, ClaudeUsageDisplay claude,
            bool fiveHour, BadgeTextColors theme)
        {
            string period = fiveHour ? CurrentSession : WeeklySession;
            double? remaining = fiveHour ? claude.FiveHourRemaining : claude.WeeklyRemaining;
            if (!remaining.HasValue)
            {
                string status = fiveHour ? claude.FiveHour : claude.Weekly;
                int separator = status.IndexOf(" · ", StringComparison.Ordinal);
                if (separator >= 0) status = status.Substring(separator + 3);
                var unavailable = CalculateSessionRow(g, bounds, scale, period, status, null, false, null);
                DrawDetailedSessionRow(g, unavailable, scale, QuotaLabelSize, false, theme.Muted, theme.Muted, theme.Text, theme.Muted);
                return;
            }
            string reset = fiveHour ? claude.FiveHourReset : claude.WeeklyReset;
            string countdown = CountdownInParentheses(fiveHour ? claude.FiveHourCountdownText : claude.WeeklyCountdownText);
            bool knownReset = !String.IsNullOrEmpty(reset);
            bool previous = fiveHour ? claude.FiveHourIsPrevious : claude.WeeklyIsPrevious;
            if (!knownReset) reset = previous ? "이전 값 · 초기화 시각 확인 불가" : "초기화 시각 확인 불가";
            var row = CalculateSessionRow(g, bounds, scale, period, PercentText(remaining.Value), reset, knownReset, countdown);
            DrawDetailedSessionRow(g, row, scale, QuotaValueSize, true, theme.Text, theme.Muted, theme.Text, theme.Muted);
        }

        private static void DrawDetailedSessionRow(Graphics g, SessionRowPlacement row, float scale, float valueSize, bool boldValue,
            Color valueColor, Color periodColor, Color timerColor, Color resetColor)
        {
            using (var valueFont = new Font("Segoe UI", valueSize * scale, boldValue ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var periodFont = new Font("Segoe UI", QuotaLabelSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var timeFont = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var timerFont = new Font("Segoe UI", row.ResetFontSize * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                Rectangle label = row.Period, reset = row.Reset, timer = row.Countdown;
                label.Offset(0, BaselineOffset(valueFont, periodFont));
                if (row.ShowResetIcon) reset = DrawResetIcon(g, reset, scale, resetColor);
                if (reset.Height == row.Value.Height) reset.Offset(0, BaselineOffset(valueFont, timeFont));
                if (timer.Height == row.Value.Height) timer.Offset(0, BaselineOffset(valueFont, timerFont));
                DrawTextLine(g, label, row.PeriodText, periodFont, periodColor);
                DrawTextLine(g, row.Value, row.ValueText, valueFont, valueColor);
                DrawTextLine(g, reset, row.ResetText, timeFont, resetColor);
                DrawTextLine(g, timer, row.CountdownText, timerFont, timerColor);
            }
        }

        private static Rectangle DrawResetIcon(Graphics g, Rectangle row, float scale, Color color, bool compact = false)
        {
            int inset = ResetIconInset(scale, compact);
            if (row.Width < inset) return row;
            float diameter = (compact ? 10 : 12) * scale;
            float left = row.X + (compact ? 1 : 2) * scale, top = row.Y + (row.Height - diameter) / 2;
            float arrow = diameter / 3;
            GraphicsState saved = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, 1.1f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, left, top, diameter, diameter, 222, 299);
                    g.DrawLines(pen, new[] { new PointF(left, top), new PointF(left, top + arrow),
                        new PointF(left + arrow, top + arrow) });
                }
            }
            finally { g.Restore(saved); }
            return Rectangle.FromLTRB(row.Left + inset, row.Top, row.Right, row.Bottom);
        }

        private static void DrawCompactText(Graphics g, Rectangle bounds, float scale, BadgeTextColors theme,
            UsagePresentation presentation, BadgeLayoutSettings layout, Bitmap codexIcon, Bitmap claudeIcon)
        {
            int padding = (int)Math.Round(BadgeLayoutSettings.DetailedPadding * scale);
            int left = bounds.Left + padding;
            int right = bounds.Right - padding;
            int labelWidthDip = layout.ShowClaude ? (claudeIcon == null ? 86 : 96) : (codexIcon == null ? 44 : 58);
            int labelWidth = (int)Math.Round(labelWidthDip * scale);
            int middle = left + labelWidth;
            int periodWidth = CompactPeriodWidth(g, scale);
            int codexTop = bounds.Top + (int)Math.Round(layout.HeaderHeight * scale);
            int codexBottom = bounds.Top + (int)Math.Round(layout.CodexHeight * scale);
            UsageTone tone = presentation == null ? UsageTone.Attention : presentation.Tone;
            Color codex = tone == UsageTone.Low ? theme.DangerText : tone == UsageTone.Attention ? theme.Attention : theme.Text;
            string codexPlan = layout.ShowPlan && presentation != null ? presentation.CompactPlanText : null;
            int codexLabelBottom = codexPlan == null ? codexBottom : codexTop + (codexBottom - codexTop) / 2;
            DrawCompactService(g, Rectangle.FromLTRB(left, codexTop, middle, codexLabelBottom), codexBottom,
                codexIcon, "Codex", codexPlan, scale, theme);
            bool hasCodexAmount = presentation != null && !presentation.HasQueryFailure && presentation.CodexRemaining.HasValue;
            string codexReset = layout.ShowCodexReset && hasCodexAmount ? presentation.CompactResetText : null;
            DrawCompactQuota(g, Rectangle.FromLTRB(middle, codexTop, right, codexBottom), hasCodexAmount ? WeeklySession : null,
                periodWidth, CompactCodexAmount(presentation), codexReset, scale, codex, theme.Muted);
            if (!layout.ShowClaude) return;

            var claude = (presentation == null ? null : presentation.Claude) ?? ClaudeUsageDisplay.Awaiting;
            int claudeBottom = bounds.Top + (int)Math.Round((layout.Height - BadgeLayoutSettings.CompactClaudeBottomPadding) * scale);
            int split = codexBottom + (claudeBottom - codexBottom) / 2;
            string claudePlan = layout.ShowPlan ? claude.PlanText : null;
            int claudeLabelBottom = claude.CompactStatus == null || claudePlan != null ? split : claudeBottom;
            DrawCompactService(g, Rectangle.FromLTRB(left, codexBottom, middle, claudeLabelBottom), claudeBottom,
                claudeIcon, "Claude Code", claudePlan, scale, theme);
            if (claude.CompactStatus != null)
            {
                DrawTextLine(g, Rectangle.FromLTRB(middle, codexBottom, right, claudeBottom), claude.CompactStatus, 12 * scale, true, theme.Attention);
                return;
            }

            DrawCompactQuota(g, Rectangle.FromLTRB(middle, codexBottom, right, split), CurrentSession, periodWidth, claude.FiveHourCompactAmount,
                claude.FiveHourCompactReset, scale, claude.FiveHourRemaining.HasValue ? theme.Text : theme.Attention, theme.Muted);
            DrawCompactQuota(g, Rectangle.FromLTRB(middle, split, right, claudeBottom), WeeklySession, periodWidth, claude.WeeklyCompactAmount,
                claude.WeeklyCompactReset, scale, claude.WeeklyRemaining.HasValue ? theme.Text : theme.Attention, theme.Muted);
        }

        private static void DrawCompactService(Graphics g, Rectangle label, int bottom, Bitmap icon,
            string name, string plan, float scale, BadgeTextColors theme)
        {
            label = DrawServiceIcon(g, label, icon, scale);
            DrawTextLine(g, label, name, 12 * scale, true, theme.Muted);
            if (plan != null)
                DrawTextLine(g, Rectangle.FromLTRB(label.Left, label.Bottom, label.Right, bottom), plan, 11 * scale, false, theme.Muted);
        }

        internal static int CompactPeriodWidth(Graphics g, float scale)
        {
            using (var font = new Font("Segoe UI", 12 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                return Math.Max(MeasureTextWidth(g, CurrentSession, font), MeasureTextWidth(g, WeeklySession, font)) + ScaledGap(5, scale);
        }

        private static void DrawCompactQuota(Graphics g, Rectangle row, string period, int periodWidth, string amount, string reset,
            float scale, Color color, Color muted)
        {
            if (period != null)
            {
                DrawTextLine(g, Rectangle.FromLTRB(row.Left, row.Top, Math.Min(row.Right, row.Left + periodWidth), row.Bottom),
                    period, 12 * scale, true, color);
                row = Rectangle.FromLTRB(row.Left + periodWidth, row.Top, row.Right, row.Bottom);
            }
            DrawCompactValue(g, row, amount, reset, 12 * scale, scale, color, muted);
        }

        private static void DrawDetailedServiceHeading(Graphics g, Rectangle row, Bitmap icon, float scale,
            string name, string detail, Color nameColor, Color detailColor, bool large)
        {
            row = DrawServiceIcon(g, row, icon, scale, large ? 18 : 12, large ? 7 : 6);
            if (row.Width <= 0) return;
            using (var font = new Font("Segoe UI", (large ? 16 : 12) * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                int width = Math.Min(row.Width, MeasureTextWidth(g, name, font));
                DrawTextLine(g, new Rectangle(row.X, row.Y, width, row.Height), name, font, nameColor);
                int detailLeft = row.X + width + (int)Math.Round((large ? 7 : 5) * scale);
                var detailBounds = Rectangle.FromLTRB(detailLeft, row.Top, row.Right, row.Bottom);
                string caption = "· " + detail;
                if (large)
                {
                    using (var detailFont = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        if (MeasureTextWidth(g, caption, detailFont) <= detailBounds.Width)
                        {
                            DrawTextLine(g, detailBounds, caption, detailFont, detailColor);
                            return;
                        }
                    }
                }
                DrawTextLine(g, detailBounds, caption, 11 * scale, false, detailColor);
            }
        }

        private static Rectangle DrawServiceIcon(Graphics g, Rectangle label, Bitmap icon, float scale, int iconSize = 12, int iconGap = 6)
        {
            if (icon == null) return label;
            int size = Math.Max(1, (int)Math.Round(iconSize * scale));
            int gap = Math.Max(1, (int)Math.Round(iconGap * scale));
            if (label.Width <= size + gap || label.Height < size) return label;

            int top = label.Top + (label.Height - size) / 2;
            var destination = new Rectangle(label.Left, top, size, size);
            GraphicsState state = g.Save();
            try
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(icon, destination, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            finally { g.Restore(state); }
            return Rectangle.FromLTRB(destination.Right + gap, label.Top, label.Right, label.Bottom);
        }

        private static void DrawCompactValue(Graphics g, Rectangle row, string text, string reset, float size, float scale, Color color, Color muted)
        {
            if (reset != null)
            {
                float resetSize = BadgeLayoutSettings.ResetFontSize * scale;
                using (var resetFont = new Font("Segoe UI", BadgeLayoutSettings.ResetFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var valueFont = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    int inset = ResetIconInset(scale, compact: true);
                    int gap = ScaledGap(6, scale);
                    int right = row.Right - (int)Math.Ceiling(scale);
                    int resetLeft = Math.Max(row.Left, right - inset - MeasureTextWidth(g, reset, resetFont));
                    int valueWidth = resetLeft - gap - row.Left;
                    var resetBounds = Rectangle.FromLTRB(resetLeft, row.Top, row.Right, row.Bottom);
                    if (MeasureTextWidth(g, text, valueFont) > valueWidth)
                    {
                        using (var smaller = new Font("Segoe UI", 11 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            if (MeasureTextWidth(g, text, smaller) <= valueWidth)
                                size = smaller.Size;
                            else if (row.Height >= 36 * scale)
                            {
                                // The taller Codex row can keep both strings at full size on two lines.
                                resetBounds.Y += row.Height / 2;
                                resetBounds.Height -= row.Height / 2;
                                valueWidth = row.Width;
                                row.Height /= 2;
                            }
                            else
                            {
                                using (var smallReset = new Font("Segoe UI", smaller.Size, FontStyle.Regular, GraphicsUnit.Pixel))
                                {
                                    resetLeft = Math.Max(row.Left, right - inset - MeasureTextWidth(g, reset, smallReset));
                                    resetBounds = Rectangle.FromLTRB(resetLeft, row.Top, row.Right, row.Bottom);
                                    valueWidth = resetLeft - gap - row.Left;
                                    size = resetSize = smaller.Size;
                                }
                            }
                        }
                    }
                    DrawTextLine(g, DrawResetIcon(g, resetBounds, scale, muted, compact: true), reset, resetSize, false, muted);
                    row.Width = valueWidth;
                }
            }
            DrawTextLine(g, row, text, size, true, color);
        }

        internal static string CompactCodexAmount(UsagePresentation presentation)
        {
            if (presentation == null) return "한도 확인 중";
            if (presentation.HasQueryFailure) return "조회 실패";
            if (!presentation.CodexRemaining.HasValue) return presentation.MainText;

            string remaining = PercentText(presentation.CodexRemaining.Value);
            if (!presentation.CodexBarRemaining.HasValue) return remaining + " · 이전";
            if (presentation.Tone == UsageTone.Low)
                return remaining + (presentation.CodexRemaining.Value == 0 ? " · 소진" : " · 부족");
            return remaining;
        }

        private static string PercentText(double remaining)
        {
            return remaining.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private static int BaselineOffset(Font value, Font label)
        {
            return (int)Math.Round(BaselineFromCenter(value) - BaselineFromCenter(label));
        }

        private static float BaselineFromCenter(Font font)
        {
            FontFamily family = font.FontFamily;
            return font.Size * (family.GetCellAscent(font.Style) - family.GetLineSpacing(font.Style) / 2f) / family.GetEmHeight(font.Style);
        }

        private static void DrawTextLine(Graphics g, Rectangle bounds, string text, float size, bool bold, Color color)
        {
            if (String.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                DrawTextLine(g, bounds, text, font, color);
        }

        private static void DrawTextLine(Graphics g, Rectangle bounds, string text, Font font, Color color)
        {
            if (String.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var brush = new SolidBrush(color))
            using (var format = TextFormat())
            {
                GraphicsState saved = g.Save();
                try
                {
                    g.SetClip(bounds, CombineMode.Intersect);
                    g.DrawString(text, font, brush, bounds, format);
                }
                finally { g.Restore(saved); }
            }
        }

        private static string TextOr(string text, string fallback)
        {
            return String.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
