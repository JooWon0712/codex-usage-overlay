using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PersonalUsageOverlay
{
    internal sealed class ClaudeRefreshSchedule
    {
        private DateTime next, handledSignal;
        private int failures;
        private bool running;

        internal bool TryBegin(DateTime now, DateTime signal)
        {
            if (running || (now < next && signal <= handledSignal)) return false;
            running = true;
            if (signal > handledSignal) handledSignal = signal;
            return true;
        }

        internal void Finish(DateTime now, bool succeeded)
        {
            running = false;
            failures = succeeded ? 0 : Math.Min(3, failures + 1);
            next = now.AddMinutes(failures <= 1 ? 15 : failures == 2 ? 30 : 60);
        }
    }

    internal sealed class ClaudeAutoRefresh : IDisposable
    {
        private readonly string script, signalPath;
        private readonly Func<string> resolveNode;
        private ClaudeRefreshSchedule schedule = new ClaudeRefreshSchedule();
        private Process process;
        private IntPtr job;
        private DateTime nextTick, deadline;
        private bool wasEnabled;

        internal ClaudeAutoRefresh(string script, string signalPath, Func<string> resolveNode)
        {
            this.script = script;
            this.signalPath = signalPath;
            this.resolveNode = resolveNode;
        }

        internal void Tick(DateTime now, bool enabled)
        {
            if (!enabled)
            {
                if (wasEnabled)
                {
                    Dispose();
                    schedule = new ClaudeRefreshSchedule();
                    nextTick = DateTime.MinValue;
                }
                wasEnabled = false;
                return;
            }
            wasEnabled = true;
            if (now < nextTick && nextTick - now <= TimeSpan.FromSeconds(1)) return;
            nextTick = now.AddSeconds(1);
            if (process != null)
            {
                bool exited = process.HasExited;
                if (!exited && now < deadline && deadline - now <= TimeSpan.FromSeconds(25)) return;
                schedule.Finish(now, exited && process.ExitCode == 0);
                Dispose();
            }

            DateTime signal = SignalTime(signalPath, now);
            if (schedule.TryBegin(now, signal) && !Start(now))
                schedule.Finish(now, false);
        }

        private static DateTime SignalTime(string path, DateTime now)
        {
            try
            {
                DateTime signal = File.GetLastWriteTimeUtc(path);
                // Missing files report 1601 on Windows.
                if (signal.Year > 1601 && signal <= now) return signal;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return DateTime.MinValue;
        }

        private bool Start(DateTime now)
        {
            try
            {
                string node = resolveNode();
                if (String.IsNullOrEmpty(node) || !File.Exists(script)) return false;
                job = Native.CreateJobObject(IntPtr.Zero, null);
                var limits = new Native.JOB_LIMITS();
                limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
                if (job == IntPtr.Zero || !Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(limits)))
                    throw new IOException();
                var start = new ProcessStartInfo(node, "\"" + script + "\" --wait-for-start")
                {
                    WorkingDirectory = Path.GetDirectoryName(script),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true
                };
                process = Process.Start(start);
                if (process == null || !Native.AssignProcessToJobObject(job, process.Handle))
                    throw new IOException();
                // Arm Node only after Windows owns the process and its children.
                process.StandardInput.WriteLine("start");
                process.StandardInput.Close();
                deadline = now.AddSeconds(25);
                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            if (job != IntPtr.Zero)
            {
                Native.CloseHandle(job);
                job = IntPtr.Zero;
            }
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                process.Dispose();
                process = null;
            }
        }
    }
}
