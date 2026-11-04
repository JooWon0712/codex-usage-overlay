using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class SettingsFrameChecks
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Native.RECT bounds);
        internal static object Observation;

        internal static void Run(Dictionary<string, bool> checks)
        {
            object iconObservation = CheckIcon(checks);
            bool composition;
            bool composed = Native.DwmIsCompositionEnabled(out composition) == 0 && composition;
            var observations = new List<object>();
            IntPtr foreground = Native.GetForegroundWindow();
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                string suffix = "_" + (int)(scale * 100);
                using (var window = new SettingsWindow(new SettingsPreviewHost(VisualTheme.Dark), scale))
                {
                    window.ShowInTaskbar = false;
                    window.Opacity = 0;
                    window.StartPosition = FormStartPosition.Manual;
                    window.Location = new Point(-12000, -12000);
                    int closing = 0;
                    bool cancelled = false;
                    window.FormClosing += delegate(object sender, FormClosingEventArgs e) { closing++; cancelled |= e.Cancel; };
                    window.Show();
                    int paintEvents = 0;
                    window.Paint += delegate { paintEvents++; };
                    window.Invalidate();
                    window.Update();
                    checks["settings_frame_dwm_does_not_swallow_winforms_paint" + suffix] = paintEvents > 0;
                    IntPtr handle = window.Handle;
                    Native.RECT outer = new Native.RECT(), client = new Native.RECT();
                    bool read = GetWindowRect(handle, out outer) && Native.GetClientRect(handle, out client);
                    var origin = new Native.POINT();
                    read &= Native.ClientToScreen(handle, ref origin);
                    Rectangle buttons = window.NativeCaptionButtons;
                    Rectangle icon = window.CaptionIconBounds;
                    var textPoint = new Point(icon.Right + (int)Math.Round(24 * scale), window.CaptionHeight / 2);
                    int textHit = Hit(handle, window.PointToScreen(textPoint));
                    int iconHit = Hit(handle, window.PointToScreen(new Point(icon.Left + icon.Width / 2, icon.Top + icon.Height / 2)));
                    int minimizeHit = Hit(handle, window.PointToScreen(new Point(buttons.Left + buttons.Width / 6, buttons.Top + buttons.Height / 2)));
                    int closeHit = Hit(handle, window.PointToScreen(new Point(buttons.Right - Math.Max(1, buttons.Width / 6), buttons.Top + buttons.Height / 2)));
                    bool custom = window.HasCustomCaption;
                    int captionHeight = window.CaptionHeight;
                    bool expectedCustom = composed && !SystemInformation.HighContrast;
                    checks["settings_frame_uses_available_dwm_caption" + suffix] = custom == expectedCustom;
                    checks["settings_frame_client_covers_window_without_old_caption_gap" + suffix] = !custom || (read
                        && origin.X == outer.Left && origin.Y == outer.Top
                        && client.Right - client.Left == outer.Right - outer.Left
                        && client.Bottom - client.Top == outer.Bottom - outer.Top);
                    Control content = window.Controls.Count == 1 ? window.Controls[0] : null;
                    checks["settings_frame_caption_has_scaled_room_and_separate_content" + suffix] = !custom || (content != null
                        && window.CaptionHeight == (int)Math.Round(44 * scale)
                        && content.Top == window.CaptionHeight && content.Bottom == window.ClientSize.Height
                        && icon.Top >= (int)Math.Round(10 * scale) && icon.Bottom <= window.CaptionHeight - (int)Math.Round(10 * scale));
                    checks["settings_frame_text_drags_and_icon_opens_native_system_menu" + suffix] = !custom || (textHit == 2 && iconHit == 3);
                    checks["settings_frame_native_minimize_and_close_hit_targets" + suffix] = !custom || (minimizeHit == 8 && closeHit == 20);
                    SendMessage(handle, 0x112, new IntPtr(0xf020), IntPtr.Zero); // SC_MINIMIZE, our invisible fixture only.
                    bool minimized = window.WindowState == FormWindowState.Minimized && Native.IsIconic(handle);
                    checks["settings_frame_native_minimize_preserves_existing_window" + suffix] = minimized && !window.IsDisposed;
                    Native.ShowWindow(handle, 4); // SW_SHOWNOACTIVATE restores without taking the user's focus.
                    bool restored = window.WindowState == FormWindowState.Normal && !Native.IsIconic(handle);
                    checks["settings_frame_restore_keeps_same_window" + suffix] = restored && window.Handle == handle;
                    SendMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE follows the same close path as caption X.
                    bool disposed = window.IsDisposed && !Native.IsWindow(handle);
                    checks["settings_frame_native_close_is_not_cancelled_or_hidden" + suffix] = disposed && closing == 1 && !cancelled;
                    observations.Add(new {
                        scale = scale, customCaption = custom, captionHeight = captionHeight,
                        boundsRead = read, window = Bounds(outer), client = Bounds(client), clientScreenOrigin = new { x = origin.X, y = origin.Y },
                        captionButtons = Bounds(buttons), icon = Bounds(icon), textHit = textHit, iconHit = iconHit,
                        minimizeHit = minimizeHit, closeHit = closeHit, minimized = minimized, restored = restored,
                        disposed = disposed, closingEvents = closing, cancelled = cancelled
                    });
                }
            }
            checks["settings_frame_fixtures_do_not_activate_or_create_taskbar_windows"] = Native.GetForegroundWindow() == foreground;
            Observation = new { dwmComposition = composed, highContrast = SystemInformation.HighContrast, icon = iconObservation, samples = observations };
        }

        private static int Hit(IntPtr handle, Point screen)
        {
            int packed = (screen.X & 0xffff) | ((screen.Y & 0xffff) << 16);
            return SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt32();
        }

        private static object Bounds(Native.RECT rect)
        { return new { left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom }; }
        private static object Bounds(Rectangle rect)
        { return new { left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom }; }

        private static object CheckIcon(Dictionary<string, bool> checks)
        {
            byte[] bytes;
            using (Stream stream = typeof(SettingsFrame).Assembly.GetManifestResourceStream("PersonalUsageOverlay.AppIcon"))
            using (var copy = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("Embedded icon resource was not built.");
                stream.CopyTo(copy); bytes = copy.ToArray();
            }
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            bool valid = bytes.Length >= 6 + sizes.Length * 16 && BitConverter.ToUInt16(bytes, 0) == 0
                && BitConverter.ToUInt16(bytes, 2) == 1 && BitConverter.ToUInt16(bytes, 4) == sizes.Length;
            int next = 6 + sizes.Length * 16;
            var frames = new List<object>();
            for (int index = 0; index < sizes.Length; index++)
            {
                int entry = 6 + index * 16, width = bytes[entry] == 0 ? 256 : bytes[entry], height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
                int length = BitConverter.ToInt32(bytes, entry + 8), offset = BitConverter.ToInt32(bytes, entry + 12);
                valid &= width == sizes[index] && height == width && offset == next && length > 0 && length <= bytes.Length - offset;
                next += length;
                using (var stream = new MemoryStream(bytes))
                using (var icon = new Icon(stream, width, height))
                using (var bitmap = DecodeFrame(bytes, offset, length, width, height))
                {
                    frames.Add(new { requested = width, selectedWidth = icon.Width, selectedHeight = icon.Height, bitmapWidth = bitmap.Width,
                        bitmapHeight = bitmap.Height, length = length, offset = offset, hash = PixelHash(bitmap) });
                    valid &= bitmap.Width == width && bitmap.Height == height;
                }
            }
            checks["application_icon_resource_has_valid_nine_size_directory_and_decodable_frames"] = valid && next == bytes.Length;
            using (var actual = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            using (var extracted = actual == null ? new Bitmap(1, 1) : actual.ToBitmap())
            using (var generic = SystemIcons.Application.ToBitmap())
            {
                // ExtractAssociatedIcon follows the real system DPI: this desktop
                // returns a 48px large icon. Compare its exact pixels with the
                // corresponding declared frame instead of assuming a 32px result.
                int matchedFrame = Array.IndexOf(sizes, extracted.Width);
                if (extracted.Width != extracted.Height) matchedFrame = -1;
                int entry = matchedFrame < 0 ? 0 : 6 + matchedFrame * 16;
                using (var product = matchedFrame < 0 ? new Bitmap(1, 1) : DecodeFrame(bytes,
                    BitConverter.ToInt32(bytes, entry + 12), BitConverter.ToInt32(bytes, entry + 8), extracted.Width, extracted.Height))
                {
                    bool same = actual != null && matchedFrame >= 0 && SamePixels(product, extracted);
                    checks["application_executable_uses_product_icon_instead_of_generic_windows_icon"] = same && !SamePixels(extracted, generic);
                    return new { frameSelections = frames, productWidth = product.Width, productHeight = product.Height, nativeWidth = extracted.Width,
                        nativeHeight = extracted.Height, nativeNull = actual == null, matchedResourceFrame = matchedFrame,
                        productHash = PixelHash(product), nativeHash = PixelHash(extracted), genericHash = PixelHash(generic),
                        matchesNativeSizeFrame = same };
                }
            }
        }

        private static Bitmap DecodeFrame(byte[] bytes, int offset, int length, int width, int height)
        {
            // Framework 4.8's multi-frame Icon chooser skips the 256 PNG frame and
            // Icon.ToBitmap cannot read the 128 PNG frame. Decode each declared
            // PNG frame directly, retaining chooser observations separately.
            if (bytes[offset] == 137 && bytes[offset + 1] == 80)
            {
                using (var stream = new MemoryStream(bytes, offset, length))
                using (var bitmap = new Bitmap(stream)) return new Bitmap(bitmap);
            }
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);
                    writer.Write((byte)(width == 256 ? 0 : width)); writer.Write((byte)(height == 256 ? 0 : height));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
                    writer.Write(length); writer.Write(22); writer.Write(bytes, offset, length);
                }
                stream.Position = 0;
                using (var icon = new Icon(stream)) return icon.ToBitmap();
            }
        }

        private static string PixelHash(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            using (var hash = SHA256.Create())
            {
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        stream.WriteByte(color.A);
                        stream.WriteByte(color.A == 0 ? (byte)0 : color.R);
                        stream.WriteByte(color.A == 0 ? (byte)0 : color.G);
                        stream.WriteByte(color.A == 0 ? (byte)0 : color.B);
                    }
                return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }

        private static bool SamePixels(Bitmap first, Bitmap second)
        {
            if (first.Size != second.Size) return false;
            for (int y = 0; y < first.Height; y++)
                for (int x = 0; x < first.Width; x++)
                {
                    Color a = first.GetPixel(x, y), b = second.GetPixel(x, y);
                    if (a.A != b.A || (a.A != 0 && a.ToArgb() != b.ToArgb())) return false;
                }
            return true;
        }
    }
}
