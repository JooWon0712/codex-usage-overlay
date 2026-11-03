using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // Create only after the fixed-install-path guard. Keep this identity across releases.
    internal sealed class StableTray : NativeWindow, IDisposable
    {
        public const string IdentityString = "743b938d-a118-490c-8db5-a30c6dfc6250";
        public static readonly Guid Identity = new Guid(IdentityString);
        public static int NativeDataSize { get { return Marshal.SizeOf(typeof(NotifyIconData)); } }

        private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
        private const uint MessageFlag = 0x1, IconFlag = 0x2, TipFlag = 0x4, GuidFlag = 0x20, ShowTipFlag = 0x80;
        private const int CallbackMessage = 0x8000 + 73;
        private const int ContextMenuMessage = 0x7b, LeftDoubleClickMessage = 0x203;
        private const int KeySelectNotification = 0x400 + 1;
        private readonly uint taskbarCreated;
        private Icon icon;
        private string text = "";
        private ContextMenuStrip menu;
        private bool visible, disposed, registered, version4;

        public StableTray()
        {
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            // A hidden top-level window receives Explorer's TaskbarCreated broadcast.
            CreateHandle(new CreateParams { Caption = "Codex Usage Overlay Tray Callback" });
        }

        public event EventHandler DoubleClick;
        public bool IsRegistered { get { return registered; } }

        public Icon Icon
        {
            get { return icon; }
            set { ThrowIfDisposed(); if (ReferenceEquals(icon, value)) return; icon = value; Synchronize(); }
        }
        public string Text
        {
            get { return text; }
            set
            {
                ThrowIfDisposed();
                var next = value ?? "";
                if (next.Length > 127) next = next.Substring(0, 127);
                if (next.Length > 0 && char.IsHighSurrogate(next[next.Length - 1])) next = next.Substring(0, next.Length - 1);
                if (next == text) return;
                text = next; Synchronize();
            }
        }
        public ContextMenuStrip ContextMenuStrip
        {
            get { return menu; }
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(menu, value)) return;
                if (menu != null) menu.Closed -= OnMenuClosed;
                menu = value;
                if (menu != null) menu.Closed += OnMenuClosed;
            }
        }
        public bool Visible
        {
            get { return visible; }
            set { ThrowIfDisposed(); if (visible == value) return; visible = value; Synchronize(); }
        }

        private NotifyIconData Data()
        {
            return new NotifyIconData
            {
                cbSize = (uint)NativeDataSize,
                hWnd = Handle,
                uID = 1,
                uFlags = MessageFlag | IconFlag | TipFlag | GuidFlag | ShowTipFlag,
                uCallbackMessage = CallbackMessage,
                hIcon = icon == null ? IntPtr.Zero : icon.Handle,
                szTip = text,
                szInfo = "",
                szInfoTitle = "",
                guidItem = Identity
            };
        }

        private void Synchronize()
        {
            if (disposed) return;
            if (!visible || icon == null) { Remove(); return; }
            var data = Data();
            if (registered)
            {
                if (ShellNotifyIcon(Modify, ref data)) return;
                // Explorer may have restarted before its broadcast reached this window.
                registered = false; version4 = false;
            }
            registered = ShellNotifyIcon(Add, ref data);
            if (registered)
            {
                data.uVersion = 4;
                version4 = ShellNotifyIcon(SetVersion, ref data);
            }
        }

        private void Remove()
        {
            if (!registered) return;
            var data = Data();
            ShellNotifyIcon(Delete, ref data);
            registered = false; version4 = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (!disposed && taskbarCreated != 0 && message.Msg == taskbarCreated)
            {
                registered = false; version4 = false;
                Synchronize();
                message.Result = IntPtr.Zero;
                return;
            }
            if (!disposed && message.Msg == CallbackMessage)
            {
                int notification = (int)(message.LParam.ToInt64() & 0xffff);
                if (notification == ContextMenuMessage)
                {
                    Point location = version4 ? DecodeAnchor(message.WParam) : Cursor.Position;
                    if (location.X == -1 && location.Y == -1) location = Cursor.Position;
                    ShowContextMenu(location);
                }
                else if (notification == LeftDoubleClickMessage || notification == KeySelectNotification)
                {
                    var handler = DoubleClick;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        // Version 4 packs signed screen coordinates in wParam, including negative monitors.
        public static Point DecodeAnchor(IntPtr packed)
        {
            long value = packed.ToInt64();
            return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff)));
        }

        private void ShowContextMenu(Point location)
        {
            if (menu == null || menu.IsDisposed) return;
            SetForegroundWindow(Handle);
            menu.Show(location);
            PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
        }
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs args)
        {
            if (disposed) return;
            var data = Data();
            if (registered) ShellNotifyIcon(3, ref data); // NIM_SETFOCUS: return focus to notification area.
            PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
        }
        private void ThrowIfDisposed()
        { if (disposed) throw new ObjectDisposedException("StableTray"); }
        public void Dispose()
        {
            if (disposed) return;
            visible = false; Remove();
            if (menu != null) menu.Closed -= OnMenuClosed;
            disposed = true;
            DestroyHandle();
            GC.SuppressFinalize(this);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID, uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
