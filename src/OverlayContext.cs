using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class OverlayContext : ApplicationContext, ISettingsHost
    {
        public Preferences Prefs = new Preferences();
        private bool quitting;
        public UsagePresentation Presentation { get; private set; }
        public DateTimeOffset StartedAt { get; private set; }
        private bool layoutSavePending;
        private DateTime layoutSaveAt;
        private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Form dispatcher = new Form
        {
            ShowInTaskbar = false,
            Text = "Usage Overlay Dispatcher"
        };
        private readonly Badge badge = new Badge();
        private readonly BadgeFormatButton formatButton = new BadgeFormatButton();
        private readonly ServiceIcons serviceIcons;
        private readonly TitleAnchorTracker titleTracker = new TitleAnchorTracker();
        private readonly ThemeTracker themeTracker = new ThemeTracker();
        public VisualTheme Theme { get; private set; }
        public string ThemeStatus { get; private set; }
        private bool? darkTheme, highContrast;
        private int contrastSignature;
        private string themeSource = "pending";
        Preferences ISettingsHost.Prefs { get { return Prefs; } }
        private TitleAnchorObservation titleAnchor;
        private string anchorStatus = "pending";
        private SettingsWindow settings;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly UsageLookupHistory lookupHistory = new UsageLookupHistory();
        private readonly ClaudeUsageReader claudeUsage = new ClaudeUsageReader(AppIdentity.ClaudeUsageFile, AppIdentity.ClaudeAutomaticUsageFile);
        private readonly ClaudeAutoRefresh claudeRefresh;
        private Process worker;
        private string workerCodexPath;
        private bool workerPathRefreshPending, refreshAfterWorkerExit, workerHasReportedState;
        private UsageState state = new UsageState
        {
            status = "loading"
        };
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly Native.EventProc eventCallback;
        private IntPtr target = IntPtr.Zero;
        private readonly Dictionary<uint, Tuple<bool, DateTime>> processCache = new Dictionary<uint, Tuple<bool, DateTime>>();
        private bool active;
        private bool settingsOverlayActive;
        private bool badgeWasSettingsOverlay;
        private Rectangle lastBounds;
        private readonly string diagnostics;
        private DateTime lastDiagnostic = DateTime.MinValue, workerRestartAfter = DateTime.MinValue;
        private IntPtr workerJob;
        private EventWaitHandle openSettingsEvent;
        private RegisteredWaitHandle openSettingsWait;
        private EventWaitHandle shutdownEvent;
        private RegisteredWaitHandle shutdownWait;
        public string CodexExecutablePath { get { return workerCodexPath; } }
        public string LookupHistoryText { get { return lookupHistory.Text; } }
        public string CodexConnectionStatus
        {
            get
            {
                if (workerPathRefreshPending)
                    return "새 경로로 다시 연결 중";
                if (string.IsNullOrEmpty(workerCodexPath))
                    return "실행 경로 확인 불가";
                if (worker == null)
                    return "경로 선택됨 · 조회 프로세스 연결 끊김";
                if (!workerHasReportedState || (state.status == "loading" && state.attemptedAt == null))
                    return "경로 확인 · 조회 대기";
                if (state.status == "loading")
                    return "경로 확인 · 조회 중";
                if (state.reason == "last_update_expired" || state.reason == "account_changed")
                    return "경로 확인 · 다음 조회 대기";
                if (state.status == "ok" && state.windows != null && state.windows.Exists(UsageLookupHistory.IsCodexWindow))
                    return "경로 확인 · 최근 조회 성공";
                return "경로 선택됨 · 조회 실패";
            }
        }

        public string PositionStatus
        {
            get
            {
                if (anchorStatus == "no_space") return "배지 전체를 표시할 공간 부족 · 크기 조절 가능";
                if (Prefs.Position == "sidebar") return "사이드바 · 창 기준 위치";
                if (anchorStatus == "ok") return "본문 제목 아래 · 자동 위치 확인됨";
                if (anchorStatus == "pending") return "제목 위치 확인 대기 · 앱 활성 시 읽기";
                if (anchorStatus == "search_timeout") return "제목 위치 조회 지연 · 잠시 후 재확인";
                if (anchorStatus == "ambiguous") return "제목 위치가 모호함 · 사이드바 선택 가능";
                return "제목 위치 확인 불가 · 사이드바 선택 가능";
            }
        }
        public OverlayContext(string diagnosticPath, DateTimeOffset startedAt)
        {
            StartedAt = startedAt;
            diagnostics = diagnosticPath;
            eventCallback = OnNativeEvent;
            claudeRefresh = new ClaudeAutoRefresh(Path.Combine(folder, "claude-usage-probe.mjs"),
                AppIdentity.ClaudeRefreshRequestFile, () => LoadRuntime().Node);
            try
            {
                dispatcher.CreateControl();
                var handle = dispatcher.Handle;
                openSettingsEvent = RegisterCommand(AppIdentity.OpenSettings, OpenSettings, out openSettingsWait);
                shutdownEvent = RegisterCommand(AppIdentity.Shutdown, Quit, out shutdownWait);
                Directory.CreateDirectory(AppIdentity.DataDirectory);
                try
                {
                    var loaded = json.Deserialize<Preferences>(File.ReadAllText(AppIdentity.SettingsFile));
                    if (loaded != null)
                        Prefs = loaded;
                }
                catch
                {
                }

                Prefs.NormalizePosition();
                Presentation = UsagePresentation.Create(state, DateTimeOffset.UtcNow);
                badge.Presentation = Presentation;
                badge.LayoutSettings = Prefs.BadgeLayout;
                serviceIcons = ServiceIcons.Load();
                badge.Icons = serviceIcons;
                formatButton.ToggleRequested += ToggleBadgeFormat;
                UpdateTheme();
                settings = new SettingsWindow(this, null, serviceIcons);
                // The taskbar window owns the application's lifetime. Minimizing does not close it.
                MainForm = settings;
                workerJob = Native.CreateJobObject(IntPtr.Zero, null);
                var limits = new Native.JOB_LIMITS();
                limits.Basic.Flags = 0x2000; // Only our worker tree dies with this GUI.
                if (workerJob != IntPtr.Zero && !Native.SetInformationJobObject(workerJob, 9, ref limits, (uint)Marshal.SizeOf(limits)))
                {
                    Native.CloseHandle(workerJob);
                    workerJob = IntPtr.Zero;
                }

                AddHook(3, 3);
                AddHook(0x16, 0x17);
                AddHook(0x8002, 0x8003);
                AddHook(0x800B, 0x800B);
                StartWorker();
                timer.Interval = 200;
                timer.Tick += delegate
                {
                    Tick();
                };
                timer.Start();
                OpenSettings();
            }
            catch
            {
                // Construction can fail before Program receives the context reference.
                Dispose();
                throw;
            }
        }

        private RuntimePaths LoadRuntime()
        {
            RuntimePaths configured = null;
            try
            {
                configured = json.Deserialize<RuntimePaths>(File.ReadAllText(Path.Combine(folder, "runtime-paths.json")));
            }
            catch
            {
            }
            return RuntimeResolver.Resolve(configured, folder, Environment.GetEnvironmentVariable("PATH"), InstalledCodex.FindCli);
        }

        private void AddHook(uint min, uint max)
        {
            var hook = Native.SetWinEventHook(min, max, IntPtr.Zero, eventCallback, 0, 0, 2);
            if (hook != IntPtr.Zero)
                hooks.Add(hook);
        }

        private EventWaitHandle RegisterCommand(string name, Action action, out RegisteredWaitHandle registration)
        {
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            try
            {
                registration = ThreadPool.RegisterWaitForSingleObject(signal, delegate
                {
                    PostToUi(action);
                }, null, Timeout.Infinite, false);
                return signal;
            }
            catch
            {
                signal.Dispose();
                throw;
            }
        }

        private void PostToUi(Action action)
        {
            try
            {
                dispatcher.BeginInvoke((Action)delegate
                {
                    if (!quitting)
                        action();
                });
            }
            catch
            {
                // A queued native/process callback may arrive while the window is closing.
            }
        }

        private void OnNativeEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (quitting || (ev != 3 && hwnd != target))
                return;
            PostToUi(Tick);
        }

        private bool TargetProcess(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0)
                return false;
            Tuple<bool, DateTime> cached;
            if (processCache.TryGetValue(pid, out cached) && DateTime.UtcNow - cached.Item2 < TimeSpan.FromSeconds(2))
                return cached.Item1;
            bool match = false;
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                {
                    var path = p.MainModule.FileName;
                    match = (p.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)) && path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
            }

            processCache[pid] = Tuple.Create(match, DateTime.UtcNow);
            if (processCache.Count > 100)
                processCache.Clear();
            return match;
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int value;
            try
            {
                return Native.DwmGetWindowAttribute(hwnd, 14, out value, 4) == 0 && value != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsAvailableTarget(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && Native.IsWindow(hwnd) && Native.IsWindowVisible(hwnd)
                && !Native.IsIconic(hwnd) && !IsCloaked(hwnd) && TargetProcess(hwnd);
        }

        private bool FindSettingsTarget()
        {
            if (IsAvailableTarget(target)) return true;
            target = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (Native.GetAncestor(hwnd, 3) == hwnd && IsAvailableTarget(hwnd))
                {
                    target = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return target != IntPtr.Zero;
        }

        private void Tick()
        {
            if (quitting)
                return;
            if (layoutSavePending && DateTime.UtcNow >= layoutSaveAt)
                SavePreferences();
            UpdateTheme();
            var foreground = Native.GetForegroundWindow();
            var root = Native.GetAncestor(foreground, 3);
            if (root == IntPtr.Zero)
                root = foreground;
            bool codexForeground = IsAvailableTarget(root);
            if (codexForeground)
                target = root;
            bool settingsNormal = settings != null && !settings.IsDisposed && settings.Visible && settings.WindowState == FormWindowState.Normal;
            bool settingsForeground = settingsNormal && settings.IsHandleCreated && foreground == settings.Handle;
            bool settingsTargetAvailable = settingsForeground && FindSettingsTarget();
            settingsOverlayActive = !codexForeground && settingsTargetAvailable;
            bool show = PositionAdjustmentPolicy.CanShow(codexForeground, settingsForeground, settingsNormal, settingsTargetAvailable);
            // Both settings pages are display-only exceptions. Keep regular
            // account polling tied to Codex focus.
            if (codexForeground != active)
            {
                active = codexForeground;
                Send(new { command = "active", value = active });
            }

            claudeRefresh.Tick(DateTime.UtcNow, Prefs.BadgeLayout.ShowClaude);
            UpdatePresentation();
            if (show)
                UpdateBadge();
            else
                HideBadge();
            if (worker == null && DateTime.UtcNow >= workerRestartAfter)
            {
                StartWorker();
                Send(new { command = "active", value = active });
            }

            WriteDiagnostic();
            UpdateSettings();
        }

        private void UpdateBadge()
        {
            Native.RECT client;
            var origin = new Native.POINT();
            if (!Native.GetClientRect(target, out client) || !Native.ClientToScreen(target, ref origin))
            {
                HideBadge();
                return;
            }

            float scale = Math.Max(96, Native.GetDpiForWindow(target)) / 96f;
            var rect = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
            titleAnchor = Prefs.Position == "sidebar" ? null : titleTracker.GetOrRequest(target, rect, scale);
            anchorStatus = titleAnchor == null ? "sidebar" : titleAnchor.Status;
            float badgeScale;
            var bounds = OverlayLayout.CalculateBounds(rect, scale, Prefs, titleAnchor, out badgeScale);
            if (bounds.Width < 80 || bounds.Height < 25)
            {
                if (anchorStatus == "ok" || anchorStatus == "sidebar") anchorStatus = "no_space";
                HideBadge();
                return;
            }

            bool changed = lastBounds != bounds || Math.Abs(badge.ScaleFactor - badgeScale) > 0.01;
            badge.ScaleFactor = badgeScale;
            if (changed)
            {
                badge.Bounds = bounds;
                lastBounds = bounds;
            }

            bool reveal = !badge.Visible || badgeWasSettingsOverlay != settingsOverlayActive;
            try
            {
                bool prepared = !reveal || badge.Present(0);
                if (prepared && !badge.Visible) badge.Show();
                bool placed = prepared && (settingsOverlayActive ? BadgeZOrder.Place(badge.Handle, target, bounds)
                    : Native.SetWindowPos(badge.Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40));
                if (!placed || !badge.Present()) { HideBadge(); return; }
                badgeWasSettingsOverlay = settingsOverlayActive;
            }
            catch { HideBadge(); return; }
            UpdateBadgeFormatButton(bounds, badgeScale);
        }

        private void HideBadge()
        {
            formatButton.Hide();
            badge.Hide();
        }

        private void UpdateBadgeFormatButton(Rectangle bounds, float scale)
        {
            Rectangle buttonBounds = BadgeFormatButton.CalculateBounds(bounds, Prefs.BadgeLayout, scale);
            if (buttonBounds.IsEmpty)
            {
                formatButton.Hide();
                return;
            }

            try
            {
                formatButton.SetAppearance(Theme, Prefs.BadgeLayout.Compact);
                if (formatButton.Bounds != buttonBounds) formatButton.Bounds = buttonBounds;
                if (!formatButton.Present(scale)) { formatButton.Hide(); return; }
                if (!formatButton.Visible) formatButton.Show();
                if (Native.GetWindow(badge.Handle, 3) != formatButton.Handle &&
                    !BadgeZOrder.Place(formatButton.Handle, badge.Handle, buttonBounds)) formatButton.Hide();
            }
            catch { formatButton.Hide(); }
        }

        private void ToggleBadgeFormat(object sender, EventArgs e)
        {
            if (quitting || !formatButton.Visible) return;
            IntPtr foreground = Native.GetForegroundWindow();
            bool settingsForeground = settingsOverlayActive && settings.IsHandleCreated && foreground == settings.Handle;
            if (!settingsForeground && foreground != target && Native.GetAncestor(foreground, 3) != target) return;
            var layout = Prefs.BadgeLayout.Copy();
            layout.Compact = !layout.Compact;
            SetBadgeLayout(layout);
            settings.LoadPreferences();
        }

        private void UpdatePresentation()
        {
            var now = DateTimeOffset.UtcNow;
            var claude = Prefs.BadgeLayout.ShowClaude ? claudeUsage.Read(now) : null;
            var presentation = UsagePresentation.Create(state, now, TimeZoneInfo.Local, claude);
            var prior = Presentation;
            if (!presentation.HasSameDisplayAs(prior))
            {
                Presentation = presentation;
                badge.Presentation = presentation;
            }
        }

        private void UpdateSettings()
        {
            if (settings != null && !settings.IsDisposed)
                settings.UpdateUsage();
        }

        private void StartWorker()
        {
            if (quitting || worker != null)
                return;
            Process p = null;
            bool started = false;
            try
            {
                // Wait for any children of the previous worker to leave our job.
                Native.JOB_ACCOUNTING accounting;
                if (workerJob == IntPtr.Zero || !Native.QueryInformationJobObject(workerJob, 1,
                    out accounting, (uint)Marshal.SizeOf(typeof(Native.JOB_ACCOUNTING)), IntPtr.Zero)
                    || accounting.ActiveProcesses != 0)
                    throw new IOException();
                RuntimePaths runtime = LoadRuntime();
                workerCodexPath = runtime.Codex;
                workerHasReportedState = false;
                if (!File.Exists(runtime.Node) || !File.Exists(runtime.Codex))
                    throw new IOException();
                p = new Process();
                p.StartInfo = new ProcessStartInfo(runtime.Node, Quote(Path.Combine(folder, "worker.mjs")) + " --codex " + Quote(runtime.Codex) + " --parent " + Process.GetCurrentProcess().Id)
                {
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                p.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null || quitting)
                        return;
                    try
                    {
                        var serializer = new JavaScriptSerializer();
                        var incoming = serializer.Deserialize<UsageState>(e.Data);
                        if (incoming != null && incoming.type == "lookup-result")
                        {
                            var result = serializer.Deserialize<UsageLookupResult>(e.Data);
                            PostToUi(delegate
                            {
                                if (worker == p)
                                    lookupHistory.Add(result);
                            });
                            return;
                        }
                        if (incoming == null || incoming.type != "state")
                            return;
                        PostToUi(delegate
                        {
                            if (worker == p)
                            {
                                state = incoming;
                                workerHasReportedState = true;
                                if (active && (incoming.status == "stale" || incoming.status == "unavailable"))
                                    TryRefreshWorkerPath(false);
                                UpdatePresentation();
                                UpdateSettings();
                            }
                        });
                    }
                    catch
                    {
                    }
                };
                p.ErrorDataReceived += delegate
                {
                }; // Never surface raw server/credential logs.
                p.EnableRaisingEvents = true;
                p.Exited += delegate
                {
                    PostToUi(delegate
                    {
                        if (worker == p)
                        {
                            bool restart = workerPathRefreshPending && p.ExitCode == 0;
                            bool refresh = refreshAfterWorkerExit;
                            worker = null;
                            workerHasReportedState = false;
                            workerPathRefreshPending = false;
                            refreshAfterWorkerExit = false;
                            p.Dispose();
                            if (restart)
                            {
                                StartWorker();
                                if (refresh)
                                    Send(new { command = "refresh" });
                                Send(new { command = "active", value = active });
                            }
                            else
                                MarkWorkerFailure("worker_process_exited");
                            UpdatePresentation();
                            UpdateSettings();
                        }
                    });
                };
                started = p.Start();
                if (!started)
                    throw new IOException();
                // The worker waits for an active/refresh command before spawning app-server.
                if (workerJob == IntPtr.Zero || !Native.AssignProcessToJobObject(workerJob, p.Handle))
                    throw new IOException();
                worker = p;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch
            {
                worker = null;
                // Retain ownership even if pipe setup fails after the process has started.
                if (p != null)
                {
                    if (started)
                        TryCleanup(delegate
                        {
                            if (!p.HasExited)
                            {
                                p.Kill();
                                p.WaitForExit(2000);
                            }
                        });
                    TryCleanup(p.Dispose);
                }

                MarkWorkerFailure("process_start_failed");
            }
        }

        private void MarkWorkerFailure(string reason)
        {
            state.status = state.observedAt == null ? "unavailable" : "stale";
            state.reason = reason;
            workerRestartAfter = DateTime.UtcNow.AddSeconds(60);
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "") + "\"";
        }

        private void Send(object command)
        {
            try
            {
                if (worker != null && !worker.HasExited)
                {
                    worker.StandardInput.WriteLine(json.Serialize(command));
                    worker.StandardInput.Flush();
                }
            }
            catch
            {
            }
        }

        public void ManualRefresh()
        {
            if (worker == null)
            {
                StartWorker();
                Send(new { command = "refresh" });
                Send(new { command = "active", value = active });
                return;
            }
            if (TryRefreshWorkerPath(true))
                return;
            Send(new { command = "refresh" });
        }

        private bool TryRefreshWorkerPath(bool manual)
        {
            if (workerPathRefreshPending)
            {
                refreshAfterWorkerExit |= manual;
                return true;
            }
            if (File.Exists(workerCodexPath))
                return false;

            try
            {
                string replacement = LoadRuntime().Codex;
                if (!File.Exists(replacement) || string.Equals(workerCodexPath, replacement, StringComparison.OrdinalIgnoreCase))
                    return false;

                worker.StandardInput.WriteLine("{\"command\":\"quit\"}");
                worker.StandardInput.Flush();
                workerPathRefreshPending = true;
                refreshAfterWorkerExit = manual;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Quit()
        {
            if (quitting)
                return;
            if (settings != null && !settings.IsDisposed)
                settings.Close();
            else
                ExitThread();
        }

        public void SetPosition(string position)
        {
            Prefs.Position = position;
            Prefs.NormalizePosition();
            ApplyPreferences(true);
        }

        public void SetCoordinates(int left, int vertical)
        {
            if (Prefs.Position == Preferences.BelowTitle)
            {
                Prefs.TitleOffsetX = left;
                Prefs.TitleGap = vertical;
            }
            else
            {
                Prefs.SidebarX = left;
                Prefs.SidebarBottomGap = vertical;
                Prefs.SidebarY = vertical + Preferences.LegacySidebarBadgeHeight;
            }

            ApplyPreferences();
        }

        public void SetTheme(string mode)
        {
            Prefs.ThemeMode = mode;
            Prefs.NormalizePosition();
            ApplyPreferences();
        }

        public void SetBadgeLayout(BadgeLayoutSettings layout)
        {
            if (layout == null) return;
            Prefs.BadgeLayout = layout.Copy();
            Prefs.BadgeLayout.Normalize();
            badge.LayoutSettings = Prefs.BadgeLayout;
            layoutSavePending = true;
            layoutSaveAt = DateTime.UtcNow.AddMilliseconds(400);
            lastBounds = Rectangle.Empty;
            Tick();
        }

        private void UpdateTheme()
        {
            bool manual = Prefs.ThemeMode == "light" || Prefs.ThemeMode == "dark";
            var value = manual ? new ThemeSnapshot { IsDark = Prefs.ThemeMode == "dark", Status = Prefs.ThemeMode == "dark" ? "다크 · 직접 선택" : "라이트 · 직접 선택", Source = "manual" }
                : themeTracker.GetOrRequest(Prefs.ThemeMode != "system");
            bool contrast = SystemInformation.HighContrast;
            int signature = contrast ? unchecked(SystemColors.Window.ToArgb() * 397 ^ SystemColors.WindowText.ToArgb() * 17 ^ SystemColors.Highlight.ToArgb()) : 0;
            if (Theme == null || darkTheme != value.IsDark || highContrast != contrast || contrastSignature != signature)
            {
                darkTheme = value.IsDark;
                highContrast = contrast;
                contrastSignature = signature;
                Theme = VisualTheme.For(value.IsDark, contrast);
                badge.Theme = Theme;
            }
            ThemeStatus = contrast ? "Windows 고대비 색상 사용" : value.Status;
            themeSource = contrast ? "high_contrast" : value.Source;
        }

        public void ResetPosition()
        {
            Prefs.ResetCurrentPosition();
            ApplyPreferences();
        }

        private void ApplyPreferences(bool reloadControls = false)
        {
            SavePreferences();
            lastBounds = Rectangle.Empty;
            if (reloadControls && settings != null && !settings.IsDisposed)
                settings.LoadPreferences();
            Tick();
        }

        private void SavePreferences()
        {
            try
            {
                File.WriteAllText(AppIdentity.SettingsFile, json.Serialize(Prefs), new UTF8Encoding(false));
                layoutSavePending = false;
            }
            catch
            {
            }
        }

        public void OpenSettings()
        {
            if (quitting || settings == null || settings.IsDisposed)
                return;
            settings.UpdateUsage();
            settings.Show();
            settings.WindowState = FormWindowState.Normal;
            Native.ShowWindow(settings.Handle, 9);
            settings.Activate();
        }

        public void ActivateTarget()
        {
            if (target == IntPtr.Zero || !Native.IsWindow(target))
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr unused)
                {
                    if (Native.IsWindowVisible(h) && !Native.IsIconic(h) && TargetProcess(h))
                    {
                        target = h;
                        return false;
                    }

                    return true;
                }, IntPtr.Zero);
            }

            if (target != IntPtr.Zero)
                Native.SetForegroundWindow(target);
        }

        private void WriteDiagnostic()
        {
            if (string.IsNullOrEmpty(diagnostics) || DateTime.UtcNow - lastDiagnostic < TimeSpan.FromSeconds(1))
                return;
            lastDiagnostic = DateTime.UtcNow;
            try
            {
                uint targetPid = 0;
                if (target != IntPtr.Zero)
                    Native.GetWindowThreadProcessId(target, out targetPid);
                var data = new
                {
                    capturedAt = DateTime.UtcNow.ToString("o"),
                    visible = badge.Visible,
                    active = active,
                    settingsOverlayActive = settingsOverlayActive,
                    position = Prefs.Position,
                    targetPid = targetPid,
                    targetHandle = target.ToInt64(),
                    overlayHandle = badge.IsHandleCreated ? badge.Handle.ToInt64() : 0,
                    scale = badge.ScaleFactor,
                    bounds = new
                    {
                        x = badge.Left,
                        y = badge.Top,
                        width = badge.Width,
                        height = badge.Height
                    },
                    extendedStyle = badge.IsHandleCreated ? Native.GetWindowLongPtr(badge.Handle, -20).ToInt64() : 0,
                    workerPid = worker != null && !worker.HasExited ? worker.Id : 0,
                    status = state.status,
                    observedAt = state.observedAt,
                    mainText = Presentation.MainText,
                    detailText = Presentation.DetailText,
                    resetText = Presentation.ResetText,
                    reason = state.reason,
                    lowWeeklyRemaining = Presentation.LowWeeklyRemaining,
                    warningThresholdPercent = WeeklyQuotaWarning.ThresholdPercent,
                    themeMode = Prefs.ThemeMode,
                    themeSource = themeSource,
                    darkTheme = Theme.IsDark,
                    titleAnchorStatus = anchorStatus,
                    titleAnchorObservedAt = titleAnchor != null ? titleAnchor.ObservedAtUtc.ToString("o") : null,
                    titleBounds = titleAnchor != null ? new { x=titleAnchor.TitleBounds.X,y=titleAnchor.TitleBounds.Y,width=titleAnchor.TitleBounds.Width,height=titleAnchor.TitleBounds.Height } : null,
                    headerBounds = titleAnchor != null ? new { x=titleAnchor.HeaderBounds.X,y=titleAnchor.HeaderBounds.Y,width=titleAnchor.HeaderBounds.Width,height=titleAnchor.HeaderBounds.Height } : null,
                    executionMode = "window",
                    mainWindowVisible = settings != null && !settings.IsDisposed && settings.Visible,
                    mainWindowState = settings != null && !settings.IsDisposed ? settings.WindowState.ToString() : "closed",
                    installedExecutable = Application.ExecutablePath,
                    version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3)
                };
                File.WriteAllText(diagnostics, json.Serialize(data), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        protected override void ExitThreadCore()
        {
            if (quitting)
                return;
            try
            {
                ReleaseOwnedResources();
            }
            finally
            {
                base.ExitThreadCore();
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                    ReleaseOwnedResources();
            }
            // MainForm is disposed here, after the close event/message loop has unwound.
            finally
            {
                try { base.Dispose(disposing); }
                finally { if (disposing && serviceIcons != null) TryCleanup(serviceIcons.Dispose); }
            }
        }

        private static void TryCleanup(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch
            {
            }
        }

        private void ReleaseOwnedResources()
        {
            if (quitting)
                return;
            quitting = true;
            if (layoutSavePending) TryCleanup(SavePreferences);
            TryCleanup(delegate
            {
                timer.Stop();
            });
            TryCleanup(titleTracker.Dispose);
            TryCleanup(themeTracker.Dispose);
            TryCleanup(claudeRefresh.Dispose);
            TryCleanup(HideBadge);
            if (openSettingsWait != null)
                TryCleanup(delegate
                {
                    openSettingsWait.Unregister(null);
                });
            if (openSettingsEvent != null)
                TryCleanup(openSettingsEvent.Dispose);
            if (shutdownWait != null)
                TryCleanup(delegate
                {
                    shutdownWait.Unregister(null);
                });
            if (shutdownEvent != null)
                TryCleanup(shutdownEvent.Dispose);
            foreach (var hook in hooks)
                TryCleanup(delegate
                {
                    Native.UnhookWinEvent(hook);
                });
            if (worker != null)
            {
                var ownedWorker = worker;
                worker = null;
                TryCleanup(delegate
                {
                    ownedWorker.StandardInput.WriteLine("{\"command\":\"quit\"}");
                    ownedWorker.StandardInput.Flush();
                    ownedWorker.StandardInput.Close();
                    if (!ownedWorker.WaitForExit(4000))
                    {
                        ownedWorker.Kill();
                        ownedWorker.WaitForExit(2000);
                    }
                });
                TryCleanup(ownedWorker.Dispose);
            }

            if (workerJob != IntPtr.Zero)
            {
                Native.CloseHandle(workerJob);
                workerJob = IntPtr.Zero;
            }

            TryCleanup(formatButton.Dispose);
            TryCleanup(badge.Dispose);
            TryCleanup(dispatcher.Dispose);
            TryCleanup(timer.Dispose);
        }
    }
}
