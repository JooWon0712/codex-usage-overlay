using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.5.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.5.4.0")]
[assembly: System.Reflection.AssemblyTitle("Codex Usage Overlay")]
namespace PersonalUsageOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                DeveloperChecks.SelfTest(args.Length > 1 ? args[1] : null);
                return;
            }

            if (args.Length == 2 && args[0] == "--render-samples")
            {
                DeveloperChecks.RenderSamples(Path.GetFullPath(args[1]));
                return;
            }

            if (args.Length > 0 && args[0] == "--shutdown")
            {
                int wait = 15000;
                if (args.Length != 1 && (args.Length != 3 || args[1] != "--wait" || !int.TryParse(args[2], out wait) || wait < 1000 || wait > 60000))
                {
                    Environment.ExitCode = 64;
                    return;
                }

                Environment.ExitCode = ShutdownAndWait(wait);
                return;
            }

            string diagnostic = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--diagnostics" && i + 1 < args.Length)
                    diagnostic = Path.GetFullPath(args[++i]);
                else
                {
                    Environment.ExitCode = 64;
                    return;
                }
            }

            using (var updateLock = new Mutex(false, AppIdentity.UpdateLock))
            {
                if (!AppIdentity.Acquire(updateLock, 0))
                {
                    Environment.ExitCode = 4;
                    return;
                }

                bool updateOwned = true;
                try
                {
                    using (var mutex = new Mutex(false, AppIdentity.SingleInstance))
                    {
                        if (!AppIdentity.Acquire(mutex, 0))
                        {
                            try
                            {
                                using (var signal = EventWaitHandle.OpenExisting(AppIdentity.OpenSettings))
                                    signal.Set();
                            }
                            catch
                            {
                            }

                            return;
                        }

                        try
                        {
                            try
                            {
                                Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
                            }
                            catch
                            {
                            }

                            Application.EnableVisualStyles();
                            Application.SetCompatibleTextRenderingDefault(false);
                            using (var context = new OverlayContext(diagnostic))
                            {
                                updateLock.ReleaseMutex();
                                updateOwned = false;
                                Application.Run(context);
                            }
                        }
                        catch
                        {
                            Environment.ExitCode = 1;
                            MessageBox.Show("보조 프로그램을 시작하지 못했습니다. 압축을 모두 풀었는지 실행 파일과 구성 파일을 확인해 주세요.", "Codex 잔여 한도");
                        }
                        finally
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
                finally
                {
                    if (updateOwned)
                        updateLock.ReleaseMutex();
                }
            }
        }

        private static int ShutdownAndWait(int timeout)
        {
            try
            {
                using (var mutex = Mutex.OpenExisting(AppIdentity.SingleInstance))
                {
                    if (AppIdentity.Acquire(mutex, 0))
                    {
                        mutex.ReleaseMutex();
                        return 0;
                    }

                    try
                    {
                        using (var signal = EventWaitHandle.OpenExisting(AppIdentity.Shutdown))
                            signal.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        return 3;
                    } // Legacy process: require normal UI exit.

                    if (!AppIdentity.Acquire(mutex, timeout))
                        return 2;
                    mutex.ReleaseMutex();
                    return 0;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return 0;
            }
            catch
            {
                return 1;
            }
        }
    }
}
