using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.3.0.0")]
[assembly: System.Reflection.AssemblyTitle("Codex Usage Overlay")]

namespace PersonalUsageOverlay
{
    internal static class AppIdentity
    {
        // Keep these names across releases, including compatibility with the first prototype.
        public const string SingleInstance = "Local\\PersonalCodexUsageOverlayPrototype";
        public const string OpenSettings = "Local\\PersonalCodexUsageOverlayOpenSettings";
        public const string Shutdown = "Local\\PersonalCodexUsageOverlayShutdown";
        public const string UpdateLock = "Local\\PersonalCodexUsageOverlayUpdateLock";
        public static string InstallDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","CodexUsageOverlay"); } }
        public static string Executable { get { return Path.Combine(InstallDirectory,"UsageOverlay.exe"); } }
        public static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageOverlay"); } }
        public static string SettingsFile { get { return Path.Combine(DataDirectory,"settings.json"); } }
        public static bool IsInstalledPath(string path)
        { return string.Equals(Path.GetFullPath(path),Path.GetFullPath(Executable),StringComparison.OrdinalIgnoreCase); }
        public static bool Acquire(Mutex mutex,int timeout)
        { try { return mutex.WaitOne(timeout); } catch(AbandonedMutexException) { return true; } }
    }
    public class Preferences
    {
        public string Position = "sidebar";
        public int SidebarX = 64, SidebarY = 82, TitleX = 560, TitleY = 48;
    }
    public class RuntimePaths { public string Node; public string Codex; }
    public class LimitWindow
    {
        public string limitId, window, resetsAtKorea;
        public double usedPercent, remainingPercent;
        public double? windowDurationMins, resetsAt;
    }
    public class UsageState
    {
        public string type, status, observedAt, attemptedAt, reason;
        public List<LimitWindow> windows = new List<LimitWindow>();
    }

    internal static class WeeklyQuotaWarning
    {
        // Matches the warning colour in the inspected desktop version, not its icon visibility.
        public const double ThresholdPercent = 15;
        public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(3);
        public static double? Remaining(UsageState state, DateTimeOffset now)
        {
            DateTimeOffset observed;
            if(state==null || state.status!="ok" || !DateTimeOffset.TryParse(state.observedAt,out observed)
                || observed>now || now-observed>=FreshFor)return null;
            double? lowest=null;
            foreach(var w in state.windows??new List<LimitWindow>())
            {
                if(w==null || w.limitId!="codex" || w.windowDurationMins!=10080
                    || double.IsNaN(w.remainingPercent) || double.IsInfinity(w.remainingPercent)
                    || w.remainingPercent<0 || w.remainingPercent>=ThresholdPercent)continue;
                if(!lowest.HasValue || w.remainingPercent<lowest.Value)lowest=w.remainingPercent;
            }
            return lowest;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        public delegate void EventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT point);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr param);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int z, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CreateJobObject(IntPtr attrs,string name);
        [DllImport("kernel32.dll")] public static extern bool SetInformationJobObject(IntPtr job,int info,ref JOB_LIMITS data,uint size);
        [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] public struct BASIC_JOB_LIMITS
        {
            public long ProcessTime,JobTime; public uint Flags; public UIntPtr MinWorkingSet,MaxWorkingSet;
            public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority,Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] public struct IO_COUNTS { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] public struct JOB_LIMITS
        { public BASIC_JOB_LIMITS Basic; public IO_COUNTS Io; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    }

    internal sealed class Badge : Form
    {
        public string MainText = "한도 확인 중", DetailText = "공식 조회 · 연결 대기";
        public Color StateColor = Color.FromArgb(116, 192, 174);
        public double? LowWeeklyRemaining;
        public float ScaleFactor = 1;
        public Badge()
        {
            Text = "Codex Usage Overlay · Badge";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(1,1,1); TransparencyKey = BackColor;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000 | 0x00000020; return p; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = ScaleFactor, radius = 9 * s;
            var box = new RectangleF(0.7f, 0.7f, Width - 1.4f, Height - 1.4f);
            using (var path = Rounded(box, radius))
            using (var fill = new SolidBrush(Color.FromArgb(23, 31, 43)))
            using (var stroke = new Pen(Color.FromArgb(53, 65, 82), Math.Max(1, s)))
            { g.FillPath(fill, path); g.DrawPath(stroke, path); }
            if(LowWeeklyRemaining.HasValue)
            {
                var ring=new RectangleF(8*s,7*s,15*s,15*s);
                using(var track=new Pen(Color.FromArgb(74,60,65),2*s))g.DrawEllipse(track,ring);
                using(var arc=new Pen(StateColor,2*s))
                {
                    arc.StartCap=LineCap.Round; arc.EndCap=LineCap.Round;
                    if(LowWeeklyRemaining.Value>0)g.DrawArc(arc,ring,-90,(float)(360*LowWeeklyRemaining.Value/100));
                }
                if(LowWeeklyRemaining.Value==0)
                {
                    using(var mark=new Pen(StateColor,1.6f*s))
                    { g.DrawLine(mark,15.5f*s,11*s,15.5f*s,15*s); g.DrawLine(mark,15.5f*s,17*s,15.5f*s,17.5f*s); }
                }
                else using(var center=new SolidBrush(Color.FromArgb(136,118,125)))
                    g.FillPolygon(center,new [] {new PointF(15.5f*s,11*s),new PointF(19*s,14.5f*s),new PointF(15.5f*s,18*s),new PointF(12*s,14.5f*s)});
            }
            else using (var dot = new SolidBrush(StateColor)) g.FillEllipse(dot, 10*s, 11*s, 5*s, 5*s);
            using (var mainFont = new Font("맑은 고딕", 12.1f*s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subFont = new Font("맑은 고딕", 10*s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int mainLeft=(int)((LowWeeklyRemaining.HasValue?29:22)*s);
                TextRenderer.DrawText(g, MainText, mainFont, new Rectangle(mainLeft,(int)(5*s),Width-mainLeft-(int)(8*s),(int)(19*s)), LowWeeklyRemaining.HasValue?Color.FromArgb(255,183,183):Color.FromArgb(231,237,245), TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, DetailText, subFont, new Rectangle((int)(10*s),(int)(25*s),Width-(int)(20*s),(int)(16*s)), StateColor, TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
            }
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
    }

    internal sealed class SettingsWindow : Form
    {
        private readonly OverlayContext app;
        private readonly RadioButton sidebar = new RadioButton(), title = new RadioButton();
        private readonly NumericUpDown x = new NumericUpDown(), y = new NumericUpDown();
        private readonly Label vertical = new Label(), detail = new Label(), summary = new Label();
        private bool updating;
        public SettingsWindow(OverlayContext owner)
        {
            app = owner; Text = "잔여 한도 · 프로토타입";
            ClientSize = new Size(460, 490); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 12f,FontStyle.Regular,GraphicsUnit.Pixel); BackColor = Color.FromArgb(248,250,252);
            AutoScaleMode = AutoScaleMode.None;
            AddLabel("Codex 잔여 한도", 22, 18, 410, 30, 15, true);
            AddLabel("ChatGPT 창이 활성화되면 표시됩니다. 클릭은 아래 앱으로 통과합니다.", 22, 54, 416, 38, 9, false);
            summary.SetBounds(22, 100, 410, 30); summary.Font = new Font(Font.FontFamily, 16, FontStyle.Bold,GraphicsUnit.Pixel); Controls.Add(summary);
            detail.SetBounds(22, 135, 416, 92); detail.ForeColor = Color.FromArgb(71,85,105); Controls.Add(detail);
            AddLabel("표시 위치",22,232,410,23,9,true);
            sidebar.Text = "사이드바"; sidebar.SetBounds(24,261,180,27); Controls.Add(sidebar);
            title.Text = "세션 제목 옆"; title.SetBounds(222,261,200,27); Controls.Add(title);
            AddLabel("가로: 창 왼쪽에서",24,307,180,24,9,false);
            vertical.SetBounds(222,307,200,24); Controls.Add(vertical);
            x.AccessibleName="가로 위치"; x.Minimum=0; x.Maximum=4000; x.Increment=4; x.SetBounds(24,335,180,28); Controls.Add(x);
            y.AccessibleName="세로 위치"; y.Minimum=0; y.Maximum=3000; y.Increment=4; y.SetBounds(222,335,200,28); Controls.Add(y);
            AddLabel("위치는 화면 배율에 맞춰 조정됩니다. 긴 제목은 가로 값을 보정하세요.",24,375,410,35,8,false);
            AddButton("기본 위치",24,418,94,31,delegate { app.ResetPosition(); LoadPreferences(); });
            AddButton("지금 갱신",126,418,94,31,delegate { app.ManualRefresh(); });
            AddButton("앱에서 확인",228,418,102,31,delegate { Hide(); app.ActivateTarget(); });
            AddButton("닫기",338,418,84,31,delegate { Hide(); });
            AddLabel("알림 영역 아이콘을 두 번 눌러 설정을 다시 엽니다.",24,460,310,22,8,false);
            AddButton("종료",350,456,72,28,delegate { app.Quit(); });
            sidebar.CheckedChanged += delegate { if (!updating && sidebar.Checked) { app.SetPosition("sidebar"); LoadPreferences(); } };
            title.CheckedChanged += delegate { if (!updating && title.Checked) { app.SetPosition("title"); LoadPreferences(); } };
            x.ValueChanged += delegate { SaveCoordinates(); }; y.ValueChanged += delegate { SaveCoordinates(); };
            float uiScale=Math.Max(96,Native.GetDpiForWindow(Handle))/96f;
            if(uiScale!=1)
            {
                foreach(Control control in Controls)
                {
                    control.SetBounds((int)Math.Round(control.Left*uiScale),(int)Math.Round(control.Top*uiScale),(int)Math.Round(control.Width*uiScale),(int)Math.Round(control.Height*uiScale));
                    control.Font=new Font(control.Font.FontFamily,control.Font.Size*uiScale,control.Font.Style,GraphicsUnit.Pixel);
                }
                ClientSize=new Size((int)Math.Round(460*uiScale),(int)Math.Round(490*uiScale));
            }
            LoadPreferences(); UpdateUsage();
        }
        private void AddLabel(string text,int left,int top,int width,int height,float size,bool bold)
        { var l=new Label { Text=text, Font=new Font(Font.FontFamily,size*4/3,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel) }; l.SetBounds(left,top,width,height); Controls.Add(l); }
        private void AddButton(string text,int left,int top,int width,int height,EventHandler click)
        { var b=new Button { Text=text }; b.SetBounds(left,top,width,height); b.Click+=click; Controls.Add(b); }
        private void SaveCoordinates()
        { if (!updating) { app.SetCoordinates((int)x.Value,(int)y.Value); } }
        public void LoadPreferences()
        {
            updating=true; bool side=app.Prefs.Position=="sidebar";
            sidebar.Checked=side; title.Checked=!side;
            x.Value=Math.Max(0,Math.Min(4000,side?app.Prefs.SidebarX:app.Prefs.TitleX));
            y.Value=Math.Max(0,Math.Min(3000,side?app.Prefs.SidebarY:app.Prefs.TitleY));
            vertical.Text=side?"세로: 창 아래에서":"세로: 창 위에서";
            updating=false;
        }
        public void UpdateUsage()
        { summary.Text=app.MainText; detail.Text=app.Details; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { if (!app.Quitting) { e.Cancel=true; Hide(); } base.OnFormClosing(e); }
    }

    internal sealed class OverlayContext : ApplicationContext
    {
        public Preferences Prefs = new Preferences();
        public bool Quitting;
        public string MainText="한도 확인 중", Details="공식 조회 연결을 준비하고 있습니다.";
        private readonly string folder=AppDomain.CurrentDomain.BaseDirectory;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer();
        private readonly Form dispatcher=new Form { ShowInTaskbar=false, Text="Usage Overlay Dispatcher" };
        private readonly Badge badge=new Badge();
        private SettingsWindow settings;
        private readonly StableTray tray=new StableTray();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private Process worker;
        private UsageState state = new UsageState { status="loading" };
        private readonly List<IntPtr> hooks=new List<IntPtr>();
        private readonly Native.EventProc eventCallback;
        private IntPtr target=IntPtr.Zero;
        private readonly Dictionary<uint, Tuple<bool, DateTime>> processCache=new Dictionary<uint, Tuple<bool, DateTime>>();
        private bool active;
        private Rectangle lastBounds;
        private string lastMain, lastDetail, diagnostics;
        private DateTime lastDiagnostic=DateTime.MinValue, workerRestartAfter=DateTime.MinValue;
        private readonly RuntimePaths runtime;
        private IntPtr workerJob;
        private EventWaitHandle openSettingsEvent;
        private RegisteredWaitHandle openSettingsWait;
        private EventWaitHandle shutdownEvent;
        private RegisteredWaitHandle shutdownWait;
        public OverlayContext(string diagnosticPath,bool background)
        {
            diagnostics=diagnosticPath;
            dispatcher.CreateControl(); var handle=dispatcher.Handle;
            openSettingsEvent=new EventWaitHandle(false,EventResetMode.AutoReset,AppIdentity.OpenSettings);
            openSettingsWait=ThreadPool.RegisterWaitForSingleObject(openSettingsEvent,delegate
            { try { dispatcher.BeginInvoke((Action)delegate { if(!Quitting)OpenSettings(); }); } catch { } },null,Timeout.Infinite,false);
            shutdownEvent=new EventWaitHandle(false,EventResetMode.AutoReset,AppIdentity.Shutdown);
            shutdownWait=ThreadPool.RegisterWaitForSingleObject(shutdownEvent,delegate
            { try { dispatcher.BeginInvoke((Action)delegate { if(!Quitting)ExitThread(); }); } catch { } },null,Timeout.Infinite,false);
            Directory.CreateDirectory(AppIdentity.DataDirectory);
            try { var loaded=json.Deserialize<Preferences>(File.ReadAllText(AppIdentity.SettingsFile)); if(loaded!=null)Prefs=loaded; } catch { }
            if(Prefs.Position!="title")Prefs.Position="sidebar";
            runtime=LoadRuntime();
            workerJob=Native.CreateJobObject(IntPtr.Zero,null);
            var limits=new Native.JOB_LIMITS(); limits.Basic.Flags=0x2000; // Only our worker tree dies with this GUI.
            if(workerJob!=IntPtr.Zero && !Native.SetInformationJobObject(workerJob,9,ref limits,(uint)Marshal.SizeOf(limits)))
            { Native.CloseHandle(workerJob); workerJob=IntPtr.Zero; }
            var menu=new ContextMenuStrip();
            menu.Items.Add("사이드바에 표시",null,delegate { SetPosition("sidebar"); });
            menu.Items.Add("세션 제목 옆에 표시",null,delegate { SetPosition("title"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("지금 갱신",null,delegate { ManualRefresh(); });
            menu.Items.Add("위치 및 상태 설정…",null,delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료",null,delegate { ExitThread(); });
            tray.Icon=SystemIcons.Information; tray.Text="Codex 잔여 한도";
            tray.ContextMenuStrip=menu; tray.Visible=true; tray.DoubleClick+=delegate { OpenSettings(); };
            if(!tray.IsRegistered)throw new InvalidOperationException("tray_registration_failed");
            eventCallback=OnNativeEvent;
            AddHook(3,3); AddHook(0x16,0x17); AddHook(0x8002,0x8003); AddHook(0x800B,0x800B);
            StartWorker();
            timer.Interval=200; timer.Tick+=delegate { Tick(); }; timer.Start();
            if(!background)OpenSettings();
        }
        private RuntimePaths LoadRuntime()
        {
            try { return json.Deserialize<RuntimePaths>(File.ReadAllText(Path.Combine(folder,"runtime-paths.json"))); }
            catch { return new RuntimePaths(); }
        }
        private void AddHook(uint min,uint max)
        { var hook=Native.SetWinEventHook(min,max,IntPtr.Zero,eventCallback,0,0,2); if(hook!=IntPtr.Zero)hooks.Add(hook); }
        private void OnNativeEvent(IntPtr hook,uint ev,IntPtr hwnd,int obj,int child,uint thread,uint time)
        {
            if(Quitting || (ev!=3 && hwnd!=target))return;
            try { dispatcher.BeginInvoke((Action)delegate { if(!Quitting)Tick(); }); } catch { }
        }
        private bool TargetProcess(IntPtr hwnd)
        {
            uint pid; Native.GetWindowThreadProcessId(hwnd,out pid); if(pid==0)return false;
            Tuple<bool,DateTime> cached;
            if(processCache.TryGetValue(pid,out cached) && DateTime.UtcNow-cached.Item2<TimeSpan.FromSeconds(2))return cached.Item1;
            bool match=false;
            try
            {
                using(var p=Process.GetProcessById((int)pid))
                {
                    var path=p.MainModule.FileName;
                    match=(p.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)||p.ProcessName.Equals("Codex",StringComparison.OrdinalIgnoreCase))
                        && path.IndexOf("\\WindowsApps\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0;
                }
            } catch { }
            processCache[pid]=Tuple.Create(match,DateTime.UtcNow);
            if(processCache.Count>100)processCache.Clear();
            return match;
        }
        private static bool IsCloaked(IntPtr hwnd)
        { int value; try { return Native.DwmGetWindowAttribute(hwnd,14,out value,4)==0 && value!=0; } catch { return false; } }
        private void Tick()
        {
            if(Quitting)return;
            var foreground=Native.GetForegroundWindow();
            var root=Native.GetAncestor(foreground,3); if(root==IntPtr.Zero)root=foreground;
            bool show=root!=IntPtr.Zero && Native.IsWindowVisible(root) && !Native.IsIconic(root) && !IsCloaked(root) && TargetProcess(root);
            if(show)target=root;
            if(show!=active) { active=show; Send(new { command="active", value=active }); }
            UpdateText();
            if(show)
            {
                Native.RECT client; var origin=new Native.POINT();
                if(Native.GetClientRect(target,out client) && Native.ClientToScreen(target,ref origin))
                {
                    float scale=Math.Max(96,Native.GetDpiForWindow(target))/96f;
                    var rect=new Rectangle(origin.X,origin.Y,client.Right-client.Left,client.Bottom-client.Top);
                    var bounds=CalculateBounds(rect,scale,Prefs);
                    if(bounds.Width<80 || bounds.Height<25) { badge.Hide(); }
                    else
                    {
                        bool changed=lastBounds!=bounds || Math.Abs(badge.ScaleFactor-scale)>0.01;
                        badge.ScaleFactor=scale;
                        if(changed) { badge.Bounds=bounds; badge.Invalidate(); lastBounds=bounds; }
                        if(!badge.Visible) { badge.Show(); Native.SetWindowPos(badge.Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10|0x40); }
                        else if(changed)Native.SetWindowPos(badge.Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10);
                    }
                } else badge.Hide();
            } else badge.Hide();
            if(worker==null && DateTime.UtcNow>=workerRestartAfter) { StartWorker(); Send(new {command="active",value=active}); }
            WriteDiagnostic();
        }
        public static Rectangle CalculateBounds(Rectangle client,float scale,Preferences p)
        {
            int width=Math.Min(client.Width,(int)Math.Round(224*scale));
            int height=Math.Min(client.Height,(int)Math.Round(46*scale));
            bool side=p.Position!="title";
            int left=client.Left+(int)Math.Round((side?p.SidebarX:p.TitleX)*scale);
            int top=side?client.Bottom-(int)Math.Round(p.SidebarY*scale):client.Top+(int)Math.Round(p.TitleY*scale);
            left=Math.Max(client.Left,Math.Min(left,client.Right-width));
            top=Math.Max(client.Top,Math.Min(top,client.Bottom-height));
            return new Rectangle(left,top,width,height);
        }
        private static string TimeText(string iso)
        { DateTimeOffset value; return DateTimeOffset.TryParse(iso,out value)?value.ToLocalTime().ToString("HH:mm:ss"):"—"; }
        private List<LimitWindow> CodexWindows()
        { return (state.windows??new List<LimitWindow>()).FindAll(w=>w.limitId=="codex"); }
        private void UpdateText()
        {
            var windows=CodexWindows();
            DateTimeOffset observed;
            var now=DateTimeOffset.UtcNow;
            bool aged=DateTimeOffset.TryParse(state.observedAt,out observed) && now-observed>=WeeklyQuotaWarning.FreshFor;
            string status=state.status??"unavailable"; if(status=="ok"&&aged)status="stale";
            double? lowWeekly=WeeklyQuotaWarning.Remaining(state,now);
            var parts=new List<string>(); var resets=new List<string>();
            foreach(var w in windows)
            {
                string label=w.windowDurationMins==10080?"주간":w.windowDurationMins==300?"5시간":w.windowDurationMins.HasValue?w.windowDurationMins.Value.ToString("0",CultureInfo.InvariantCulture)+"분":w.window;
                parts.Add(label+" "+w.remainingPercent.ToString("0.#",CultureInfo.InvariantCulture)+"%");
                if(w.resetsAt.HasValue && w.resetsAt.Value>=-62135596800 && w.resetsAt.Value<=253402300799)
                {
                    var reset=DateTimeOffset.FromUnixTimeSeconds((long)w.resetsAt.Value).ToLocalTime();
                    resets.Add(label+" 초기화: "+reset.ToString("MM/dd HH:mm:ss zzz"));
                }
            }
            if(parts.Count>0)MainText=(status=="stale"?"이전 값 · ":"")+string.Join(" · ",parts.ToArray())+" 남음";
            else MainText=status=="loading"?"한도 확인 중":"한도 확인 불가";
            string sub;
            if(status=="loading")sub=state.observedAt==null?"공식 조회 · 연결 중":"갱신 중 · 마지막 "+TimeText(state.observedAt);
            else if(status=="ok"&&parts.Count>0)sub="갱신 "+TimeText(state.observedAt)+" · Codex 계정";
            else if(parts.Count>0)sub=((state.reason!=null&&state.reason!="last_update_expired")?"갱신 실패":"오래된 값")+" · 마지막 "+TimeText(state.observedAt);
            else sub="조회 실패 · 알림 영역에서 다시 갱신";
            if(lowWeekly.HasValue)sub=(lowWeekly.Value==0?"주간 한도 소진":"주간 한도 부족")+" · 갱신 "+TimeText(state.observedAt);
            Details="연결된 Codex 계정의 한도\n"+sub+"\n"+string.Join("\n",resets.ToArray());
            if(state.reason!=null)Details+="\n상태: "+FriendlyReason(state.reason);
            var color=lowWeekly.HasValue?Color.FromArgb(242,100,112):status=="ok"&&parts.Count>0?Color.FromArgb(127,199,174):Color.FromArgb(238,183,98);
            if(lastMain!=MainText || lastDetail!=sub || badge.LowWeeklyRemaining!=lowWeekly || badge.StateColor!=color)
            { badge.MainText=MainText; badge.DetailText=sub; badge.LowWeeklyRemaining=lowWeekly; badge.StateColor=color; badge.Invalidate(); lastMain=MainText; lastDetail=sub; }
            if(settings!=null && !settings.IsDisposed)settings.UpdateUsage();
            string tip="잔여 한도 · "+MainText; tray.Text=tip.Length>63?tip.Substring(0,63):tip;
        }
        private static string FriendlyReason(string reason)
        {
            if(reason=="account_changed")return "로그인 상태가 바뀌어 다시 확인합니다";
            if(reason.IndexOf("timeout",StringComparison.OrdinalIgnoreCase)>=0)return "조회 시간 초과";
            if(reason.IndexOf("aged",StringComparison.OrdinalIgnoreCase)>=0 || reason.IndexOf("expired",StringComparison.OrdinalIgnoreCase)>=0)return "마지막 확인 후 시간이 지났습니다";
            if(reason.IndexOf("process",StringComparison.OrdinalIgnoreCase)>=0)return "조회 연결을 사용할 수 없습니다";
            if(reason.IndexOf("missing",StringComparison.OrdinalIgnoreCase)>=0 || reason.IndexOf("unavailable",StringComparison.OrdinalIgnoreCase)>=0)return "한도 데이터 확인 불가";
            return "공식 조회 실패 ("+reason+")";
        }
        private void StartWorker()
        {
            if(Quitting)return;
            try
            {
                if(runtime==null || string.IsNullOrEmpty(runtime.Node)||string.IsNullOrEmpty(runtime.Codex)||!File.Exists(runtime.Node)||!File.Exists(runtime.Codex))throw new IOException();
                var p=new Process(); p.StartInfo=new ProcessStartInfo(runtime.Node,Quote(Path.Combine(folder,"worker.mjs"))+" --codex "+Quote(runtime.Codex)+" --parent "+Process.GetCurrentProcess().Id)
                { WorkingDirectory=folder, UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
                p.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e)
                {
                    if(e.Data==null || Quitting)return;
                    try
                    {
                        var incoming=new JavaScriptSerializer().Deserialize<UsageState>(e.Data);
                        if(incoming==null||incoming.type!="state")return;
                        dispatcher.BeginInvoke((Action)delegate { if(!Quitting && worker==p) { state=incoming; UpdateText(); } });
                    } catch { }
                };
                p.ErrorDataReceived+=delegate { }; // Never surface raw server/credential logs.
                p.EnableRaisingEvents=true;
                p.Exited+=delegate
                {
                    try { dispatcher.BeginInvoke((Action)delegate
                    { if(!Quitting && worker==p) { worker=null; state.status=state.observedAt==null?"unavailable":"stale"; state.reason="worker_process_exited"; workerRestartAfter=DateTime.UtcNow.AddSeconds(60); UpdateText(); p.Dispose(); } }); } catch { }
                };
                if(!p.Start())throw new IOException();
                // The worker waits for an active/refresh command before spawning app-server.
                if(workerJob==IntPtr.Zero || !Native.AssignProcessToJobObject(workerJob,p.Handle))
                { try { p.Kill(); p.WaitForExit(2000); } catch { } p.Dispose(); throw new IOException(); }
                worker=p; p.BeginOutputReadLine(); p.BeginErrorReadLine();
            }
            catch
            { worker=null; state.status=state.observedAt==null?"unavailable":"stale"; state.reason="process_start_failed"; workerRestartAfter=DateTime.UtcNow.AddSeconds(60); }
        }
        private static string Quote(string path) { return "\""+path.Replace("\"", "")+"\""; }
        private void Send(object command)
        { try { if(worker!=null && !worker.HasExited) { worker.StandardInput.WriteLine(json.Serialize(command)); worker.StandardInput.Flush(); } } catch { } }
        public void ManualRefresh() { if(worker==null)StartWorker(); Send(new { command="refresh" }); }
        public void Quit() { ExitThread(); }
        public void SetPosition(string position)
        { Prefs.Position=position; SavePreferences(); lastBounds=Rectangle.Empty; if(settings!=null&&!settings.IsDisposed)settings.LoadPreferences(); Tick(); }
        public void SetCoordinates(int left,int vertical)
        { if(Prefs.Position=="title") { Prefs.TitleX=left; Prefs.TitleY=vertical; } else { Prefs.SidebarX=left; Prefs.SidebarY=vertical; } SavePreferences(); lastBounds=Rectangle.Empty; Tick(); }
        public void ResetPosition()
        { if(Prefs.Position=="title") { Prefs.TitleX=560; Prefs.TitleY=48; } else { Prefs.SidebarX=64; Prefs.SidebarY=82; } SavePreferences(); lastBounds=Rectangle.Empty; Tick(); }
        private void SavePreferences()
        { try { File.WriteAllText(AppIdentity.SettingsFile,json.Serialize(Prefs),new UTF8Encoding(false)); } catch { } }
        public void OpenSettings()
        { if(settings==null||settings.IsDisposed)settings=new SettingsWindow(this); settings.UpdateUsage(); settings.Show(); Native.ShowWindow(settings.Handle,5); settings.Activate(); }
        public void ActivateTarget()
        {
            if(target==IntPtr.Zero || !Native.IsWindow(target))
            {
                Native.EnumWindows(delegate(IntPtr h,IntPtr unused)
                { if(Native.IsWindowVisible(h)&&!Native.IsIconic(h)&&TargetProcess(h)) { target=h; return false; } return true; },IntPtr.Zero);
            }
            if(target!=IntPtr.Zero)Native.SetForegroundWindow(target);
        }
        private void WriteDiagnostic()
        {
            if(string.IsNullOrEmpty(diagnostics)||DateTime.UtcNow-lastDiagnostic<TimeSpan.FromSeconds(1))return;
            lastDiagnostic=DateTime.UtcNow;
            try
            {
                uint targetPid=0; if(target!=IntPtr.Zero)Native.GetWindowThreadProcessId(target,out targetPid);
                var data=new { capturedAt=DateTime.UtcNow.ToString("o"), visible=badge.Visible, active=active, position=Prefs.Position,
                    targetPid=targetPid, targetHandle=target.ToInt64(), overlayHandle=badge.IsHandleCreated?badge.Handle.ToInt64():0,
                    scale=badge.ScaleFactor, bounds=new { x=badge.Left,y=badge.Top,width=badge.Width,height=badge.Height },
                    extendedStyle=badge.IsHandleCreated?Native.GetWindowLongPtr(badge.Handle,-20).ToInt64():0,
                    workerPid=worker!=null&&!worker.HasExited?worker.Id:0, status=state.status, observedAt=state.observedAt,
                    mainText=MainText, detailText=lastDetail, reason=state.reason,
                    lowWeeklyRemaining=badge.LowWeeklyRemaining, warningThresholdPercent=WeeklyQuotaWarning.ThresholdPercent,
                    trayIdentity=StableTray.IdentityString, trayRegistered=tray.IsRegistered,
                    installedExecutable=Application.ExecutablePath, version=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) };
                File.WriteAllText(diagnostics,json.Serialize(data),new UTF8Encoding(false));
            } catch { }
        }
        protected override void ExitThreadCore()
        {
            if(Quitting)return; Quitting=true;
            timer.Stop(); badge.Hide();
            if(openSettingsWait!=null)openSettingsWait.Unregister(null);
            if(openSettingsEvent!=null)openSettingsEvent.Dispose();
            if(shutdownWait!=null)shutdownWait.Unregister(null);
            if(shutdownEvent!=null)shutdownEvent.Dispose();
            foreach(var hook in hooks)Native.UnhookWinEvent(hook);
            if(worker!=null)
            {
                try { worker.StandardInput.WriteLine("{\"command\":\"quit\"}"); worker.StandardInput.Flush(); worker.StandardInput.Close(); if(!worker.WaitForExit(4000)) { worker.Kill(); worker.WaitForExit(2000); } } catch { }
                worker.Dispose(); worker=null;
            }
            if(workerJob!=IntPtr.Zero) { Native.CloseHandle(workerJob); workerJob=IntPtr.Zero; }
            tray.Visible=false; tray.Dispose(); badge.Dispose(); if(settings!=null)settings.Dispose(); dispatcher.Dispose(); timer.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--self-test") { SelfTest(args.Length>1?args[1]:null); return; }
            if(args.Length==2 && args[0]=="--render-samples") { RenderSamples(Path.GetFullPath(args[1])); return; }
            if(args.Length>0 && args[0]=="--shutdown")
            {
                int wait=15000;
                if(args.Length!=1 && (args.Length!=3 || args[1]!="--wait" || !int.TryParse(args[2],out wait) || wait<1000 || wait>60000))
                { Environment.ExitCode=64; return; }
                Environment.ExitCode=ShutdownAndWait(wait); return;
            }
            bool background=false; string diagnostic=null;
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="--background")background=true;
                else if(args[i]=="--diagnostics" && i+1<args.Length)diagnostic=Path.GetFullPath(args[++i]);
                else { Environment.ExitCode=64; return; }
            }
            // A GUID icon must never bind to a versioned checkout/build path.
            if(!AppIdentity.IsInstalledPath(Application.ExecutablePath))
            {
                if(File.Exists(AppIdentity.Executable))
                {
                    try { Process.Start(new ProcessStartInfo(AppIdentity.Executable,background?"--background":"") { UseShellExecute=false,CreateNoWindow=true }); }
                    catch { Environment.ExitCode=1; }
                }
                else { MessageBox.Show("고정 설치 폴더에서 실행해야 합니다. 먼저 install.ps1을 실행해 주세요.","Codex 잔여 한도"); Environment.ExitCode=5; }
                return;
            }
            using(var updateLock=new Mutex(false,AppIdentity.UpdateLock))
            {
                if(!AppIdentity.Acquire(updateLock,0)) { Environment.ExitCode=4; return; }
                bool updateOwned=true;
                try
                {
                    using(var mutex=new Mutex(false,AppIdentity.SingleInstance))
                    {
                        if(!AppIdentity.Acquire(mutex,0))
                        {
                            if(!background)try { using(var signal=EventWaitHandle.OpenExisting(AppIdentity.OpenSettings))signal.Set(); } catch { }
                            return;
                        }
                        try
                        {
                            try { Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
                            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                            var context=new OverlayContext(diagnostic,background);
                            updateLock.ReleaseMutex(); updateOwned=false;
                            Application.Run(context);
                        }
                        catch { Environment.ExitCode=1; MessageBox.Show("보조 프로그램을 시작하지 못했습니다. 설치 상태와 트레이 등록을 확인해 주세요.","Codex 잔여 한도"); }
                        finally { mutex.ReleaseMutex(); }
                    }
                }
                finally { if(updateOwned)updateLock.ReleaseMutex(); }
            }
        }
        private static int ShutdownAndWait(int timeout)
        {
            try
            {
                using(var mutex=Mutex.OpenExisting(AppIdentity.SingleInstance))
                {
                    if(AppIdentity.Acquire(mutex,0)) { mutex.ReleaseMutex(); return 0; }
                    try { using(var signal=EventWaitHandle.OpenExisting(AppIdentity.Shutdown))signal.Set(); }
                    catch(WaitHandleCannotBeOpenedException) { return 3; } // Legacy process: require normal UI exit.
                    if(!AppIdentity.Acquire(mutex,timeout))return 2;
                    mutex.ReleaseMutex(); return 0;
                }
            }
            catch(WaitHandleCannotBeOpenedException) { return 0; }
            catch { return 1; }
        }
        private static void SelfTest(string output)
        {
            var checks=new Dictionary<string,bool>();
            var p=new Preferences();
            var normal=OverlayContext.CalculateBounds(new Rectangle(100,200,1200,800),1,p);
            checks["sidebar_relative_to_client"]=normal==new Rectangle(164,918,224,46);
            var negative=OverlayContext.CalculateBounds(new Rectangle(-2560,-200,2000,1200),1.5f,p);
            checks["mixed_dpi_negative_monitor"]=negative==new Rectangle(-2464,877,336,69);
            p.Position="title"; p.TitleX=3900; p.TitleY=2900;
            var small=OverlayContext.CalculateBounds(new Rectangle(-900,0,600,400),2,p);
            checks["title_clamped_inside_window"]=small==new Rectangle(-748,308,448,92);
            var tiny=OverlayContext.CalculateBounds(new Rectangle(0,0,100,30),1,p);
            checks["tiny_window_stays_inside"]=tiny==new Rectangle(0,0,100,30);
            checks["stable_tray_native_layout"]=StableTray.NativeDataSize==976;
            checks["stable_tray_identity"]=StableTray.Identity==new Guid("743b938d-a118-490c-8db5-a30c6dfc6250");
            checks["negative_tray_coordinates"]=StableTray.DecodeAnchor(new IntPtr(unchecked((int)(0x0078f8f8))))==new Point(-1800,120);
            checks["fixed_install_path_accepts_canonical_exe"]=AppIdentity.IsInstalledPath(AppIdentity.Executable);
            checks["checkout_cannot_register_stable_icon"]=!AppIdentity.IsInstalledPath(Path.Combine(Path.GetTempPath(),"checkout","UsageOverlay.exe"));
            var now=DateTimeOffset.Parse("2020-01-01T00:03:00Z",CultureInfo.InvariantCulture);
            var quota=new UsageState {status="ok",observedAt=now.ToString("o")};
            var weekly=new LimitWindow {limitId="codex",window="primary",windowDurationMins=10080,remainingPercent=14};
            quota.windows.Add(weekly);
            checks["weekly_fourteen_percent_warns"]=WeeklyQuotaWarning.Remaining(quota,now)==14;
            weekly.remainingPercent=15;
            checks["exactly_fifteen_does_not_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            weekly.remainingPercent=15.001;
            checks["above_fifteen_does_not_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            weekly.remainingPercent=14.999;
            checks["unrounded_value_controls_warning"]=WeeklyQuotaWarning.Remaining(quota,now)==14.999 && weekly.remainingPercent.ToString("0.#",CultureInfo.InvariantCulture)=="15";
            weekly.remainingPercent=0;
            checks["zero_is_exhausted_not_unavailable"]=WeeklyQuotaWarning.Remaining(quota,now)==0;
            weekly.remainingPercent=14; weekly.window="secondary";
            checks["weekly_secondary_is_supported"]=WeeklyQuotaWarning.Remaining(quota,now)==14;
            weekly.windowDurationMins=300;
            checks["five_hour_is_not_weekly_warning"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            weekly.windowDurationMins=null;
            checks["missing_period_is_not_guessed"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            weekly.windowDurationMins=10080; weekly.limitId="other";
            checks["other_account_bucket_does_not_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            weekly.limitId="codex";
            foreach(var status in new [] {"loading","stale","unavailable"})
            { quota.status=status; checks["no_live_warning_when_"+status]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue; }
            quota.status="ok"; quota.observedAt=now.AddMinutes(-3).ToString("o");
            checks["three_minute_boundary_is_stale"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            quota.observedAt=now.AddMinutes(-3).AddMilliseconds(1).ToString("o");
            checks["fresh_last_millisecond_can_warn"]=WeeklyQuotaWarning.Remaining(quota,now)==14;
            quota.observedAt=null;
            checks["missing_success_time_cannot_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            quota.observedAt="invalid";
            checks["invalid_success_time_cannot_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            quota.observedAt=now.AddMinutes(1).ToString("o");
            checks["future_success_time_cannot_warn"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            quota.observedAt=now.ToString("o");
            foreach(var value in new [] {double.NaN,double.PositiveInfinity,-1,101})
            { weekly.remainingPercent=value; checks["invalid_remaining_"+value.ToString(CultureInfo.InvariantCulture)]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue; }
            weekly.remainingPercent=14;
            checks["successful_recovery_restores_warning"]=WeeklyQuotaWarning.Remaining(quota,now)==14;
            weekly.remainingPercent=80;
            checks["quota_recovery_clears_warning"]=!WeeklyQuotaWarning.Remaining(quota,now).HasValue;
            bool passed=true; foreach(bool result in checks.Values)passed&=result;
            var text=new JavaScriptSerializer().Serialize(new {passed=passed,checks=checks});
            if(output!=null)File.WriteAllText(output,text,new UTF8Encoding(false));
            Environment.ExitCode=passed?0:1;
        }
        private static void RenderSamples(string directory)
        {
            // Developer-only visual verification: no tray, worker, account read or settings writes.
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var samples=new [] {
                new {Name="normal",Main="주간 75% 남음",Detail="갱신 12:34:56 · Codex 계정",Low=(double?)null,Color=Color.FromArgb(127,199,174)},
                new {Name="weekly-low",Main="주간 14% 남음",Detail="주간 한도 부족 · 갱신 12:34:56",Low=(double?)14,Color=Color.FromArgb(242,100,112)},
                new {Name="weekly-exhausted",Main="주간 0% 남음",Detail="주간 한도 소진 · 갱신 12:34:56",Low=(double?)0,Color=Color.FromArgb(242,100,112)},
                new {Name="stale",Main="이전 값 · 주간 14% 남음",Detail="갱신 실패 · 마지막 12:34:56",Low=(double?)null,Color=Color.FromArgb(238,183,98)}
            };
            foreach(var sample in samples)foreach(var scale in new [] {1f,1.5f,2f})
            using(var badge=new Badge {ClientSize=new Size((int)(224*scale),(int)(46*scale)),ScaleFactor=scale,
                MainText=sample.Main,DetailText=sample.Detail,LowWeeklyRemaining=sample.Low,StateColor=sample.Color})
            using(var bitmap=new Bitmap(badge.Width,badge.Height))
            {
                badge.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.Combine(directory,sample.Name+"-"+(int)(scale*100)+".png"),System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
