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
    internal sealed class OverlayContext : ApplicationContext
    {
        public Preferences Prefs = new Preferences();
        private bool quitting;
        public string MainText = "한도 확인 중", Details = "공식 조회 연결을 준비하고 있습니다.";
        private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Form dispatcher = new Form
        {
            ShowInTaskbar = false,
            Text = "Usage Overlay Dispatcher"
        };
        private readonly Badge badge = new Badge();
        private SettingsWindow settings;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private Process worker;
        private UsageState state = new UsageState
        {
            status = "loading"
        };
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly Native.EventProc eventCallback;
        private IntPtr target = IntPtr.Zero;
        private readonly Dictionary<uint, Tuple<bool, DateTime>> processCache = new Dictionary<uint, Tuple<bool, DateTime>>();
        private bool active;
        private Rectangle lastBounds;
        private readonly string diagnostics;
        private DateTime lastDiagnostic = DateTime.MinValue, workerRestartAfter = DateTime.MinValue;
        private readonly RuntimePaths runtime;
        private IntPtr workerJob;
        private EventWaitHandle openSettingsEvent;
        private RegisteredWaitHandle openSettingsWait;
        private EventWaitHandle shutdownEvent;
        private RegisteredWaitHandle shutdownWait;
        public OverlayContext(string diagnosticPath)
        {
            diagnostics = diagnosticPath;
            eventCallback = OnNativeEvent;
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

                if (Prefs.Position != "title")
                    Prefs.Position = "sidebar";
                runtime = LoadRuntime();
                settings = new SettingsWindow(this);
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
            return RuntimeResolver.Resolve(configured, folder, Environment.GetEnvironmentVariable("PATH"));
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

        private void Tick()
        {
            if (quitting)
                return;
            var foreground = Native.GetForegroundWindow();
            var root = Native.GetAncestor(foreground, 3);
            if (root == IntPtr.Zero)
                root = foreground;
            bool show = root != IntPtr.Zero && Native.IsWindowVisible(root) && !Native.IsIconic(root) && !IsCloaked(root) && TargetProcess(root);
            if (show)
                target = root;
            if (show != active)
            {
                active = show;
                Send(new { command = "active", value = active });
            }

            UpdateText();
            if (show)
            {
                Native.RECT client;
                var origin = new Native.POINT();
                if (Native.GetClientRect(target, out client) && Native.ClientToScreen(target, ref origin))
                {
                    float scale = Math.Max(96, Native.GetDpiForWindow(target)) / 96f;
                    var rect = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
                    var bounds = OverlayLayout.CalculateBounds(rect, scale, Prefs);
                    if (bounds.Width < 80 || bounds.Height < 25)
                    {
                        badge.Hide();
                    }
                    else
                    {
                        bool changed = lastBounds != bounds || Math.Abs(badge.ScaleFactor - scale) > 0.01;
                        badge.ScaleFactor = scale;
                        if (changed)
                        {
                            badge.Bounds = bounds;
                            badge.Invalidate();
                            lastBounds = bounds;
                        }

                        if (!badge.Visible)
                        {
                            badge.Show();
                            Native.SetWindowPos(badge.Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
                        }
                        else if (changed)
                            Native.SetWindowPos(badge.Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
                    }
                }
                else
                    badge.Hide();
            }
            else
                badge.Hide();
            if (worker == null && DateTime.UtcNow >= workerRestartAfter)
            {
                StartWorker();
                Send(new { command = "active", value = active });
            }

            WriteDiagnostic();
        }

        private void UpdateText()
        {
            var presentation = UsagePresentation.Create(state, DateTimeOffset.UtcNow);
            MainText = presentation.MainText;
            Details = presentation.Details;
            if (badge.MainText != presentation.MainText || badge.DetailText != presentation.DetailText || badge.LowWeeklyRemaining != presentation.LowWeeklyRemaining || badge.StateColor != presentation.StateColor)
            {
                badge.MainText = presentation.MainText;
                badge.DetailText = presentation.DetailText;
                badge.LowWeeklyRemaining = presentation.LowWeeklyRemaining;
                badge.StateColor = presentation.StateColor;
                badge.Invalidate();
            }

            if (settings != null && !settings.IsDisposed)
                settings.UpdateUsage();
        }

        private void StartWorker()
        {
            if (quitting)
                return;
            Process p = null;
            bool started = false;
            try
            {
                if (runtime == null || string.IsNullOrEmpty(runtime.Node) || string.IsNullOrEmpty(runtime.Codex) || !File.Exists(runtime.Node) || !File.Exists(runtime.Codex))
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
                        var incoming = new JavaScriptSerializer().Deserialize<UsageState>(e.Data);
                        if (incoming == null || incoming.type != "state")
                            return;
                        PostToUi(delegate
                        {
                            if (worker == p)
                            {
                                state = incoming;
                                UpdateText();
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
                            worker = null;
                            MarkWorkerFailure("worker_process_exited");
                            UpdateText();
                            p.Dispose();
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
                StartWorker();
            Send(new { command = "refresh" });
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
            ApplyPreferences(true);
        }

        public void SetCoordinates(int left, int vertical)
        {
            if (Prefs.Position == "title")
            {
                Prefs.TitleX = left;
                Prefs.TitleY = vertical;
            }
            else
            {
                Prefs.SidebarX = left;
                Prefs.SidebarY = vertical;
            }

            ApplyPreferences();
        }

        public void ResetPosition()
        {
            if (Prefs.Position == "title")
            {
                Prefs.TitleX = 560;
                Prefs.TitleY = 48;
            }
            else
            {
                Prefs.SidebarX = 64;
                Prefs.SidebarY = 82;
            }

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
                    mainText = MainText,
                    detailText = badge.DetailText,
                    reason = state.reason,
                    lowWeeklyRemaining = badge.LowWeeklyRemaining,
                    warningThresholdPercent = WeeklyQuotaWarning.ThresholdPercent,
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
                base.Dispose(disposing);
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
            TryCleanup(delegate
            {
                timer.Stop();
            });
            TryCleanup(delegate
            {
                badge.Hide();
            });
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

            TryCleanup(badge.Dispose);
            TryCleanup(dispatcher.Dispose);
            TryCleanup(timer.Dispose);
        }
    }
}
