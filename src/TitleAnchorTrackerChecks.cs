using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace PersonalUsageOverlay
{
    internal static class TitleAnchorTrackerChecks
    {
        private static TitleAnchorObservation Ready(Rectangle client, DateTime now)
        {
            return new TitleAnchorObservation { Status="ok",TitleBounds=new Rectangle(client.Left+50,client.Top+20,120,20),HeaderBounds=new Rectangle(client.Left,client.Top,client.Width,50),ObservedAtUtc=now };
        }

        public static void Run(Dictionary<string, bool> checks)
        {
            var client = new Rectangle(100,100,1000,800);
            DateTime epoch = new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
            long ticks = epoch.Ticks;
            Func<DateTime> now = () => new DateTime(Interlocked.Read(ref ticks),DateTimeKind.Utc);
            int calls = 0;
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var refreshed = new ManualResetEvent(false))
            using (var finishRefresh = new ManualResetEvent(false))
            using (var tracker = new TitleAnchorTracker((window, rect, scale) =>
            {
                if (Interlocked.Increment(ref calls) == 1) { entered.Set(); release.WaitOne(); }
                else { refreshed.Set(); finishRefresh.WaitOne(); }
                return Ready(rect,now());
            },now))
            {
                try
                {
                    checks["anchor_initial_query_is_pending"] = tracker.GetOrRequest(new IntPtr(1),client,1).Status == "pending";
                    checks["anchor_query_runs_on_worker"] = entered.WaitOne(1000);
                    for (int i=0;i<30;i++) tracker.GetOrRequest(new IntPtr(1),client,1);
                    checks["anchor_single_flight_during_slow_provider"] = Interlocked.CompareExchange(ref calls,0,0) == 1;
                    release.Set();
                    checks["anchor_completed_query_is_published"] = SpinWait.SpinUntil(() => tracker.GetOrRequest(new IntPtr(1),client,1).Status == "ok",1000);
                    Interlocked.Exchange(ref ticks,epoch.AddMilliseconds(1999).Ticks);
                    checks["fresh_anchor_is_kept_during_refresh"] = tracker.GetOrRequest(new IntPtr(1),client,1).Status == "ok";
                    checks["anchor_refresh_started"] = refreshed.WaitOne(1000);
                    Interlocked.Exchange(ref ticks,epoch.AddSeconds(2).Ticks);
                    checks["expired_anchor_is_not_reused_while_busy"] = tracker.GetOrRequest(new IntPtr(1),client,1).Status != "ok";
                    Interlocked.Exchange(ref ticks,epoch.AddSeconds(5).Ticks);
                    checks["slow_anchor_reports_timeout"] = tracker.GetOrRequest(new IntPtr(1),client,1).Status == "search_timeout";
                    for (int i=0;i<30;i++) tracker.GetOrRequest(new IntPtr(1),client,1);
                    checks["timed_out_provider_does_not_spawn_more_reads"] = Interlocked.CompareExchange(ref calls,0,0) == 2;
                }
                finally { tracker.Dispose(); release.Set(); finishRefresh.Set(); }
            }

            using (var first = new ManualResetEvent(false))
            using (var releaseFirst = new ManualResetEvent(false))
            using (var second = new ManualResetEvent(false))
            using (var releaseSecond = new ManualResetEvent(false))
            using (var tracker = new TitleAnchorTracker((window, rect, scale) =>
            {
                if (window == new IntPtr(1)) { first.Set(); releaseFirst.WaitOne(); }
                else { second.Set(); releaseSecond.WaitOne(); }
                return Ready(rect,now());
            },now))
            {
                var moved = new Rectangle(300,200,1000,800);
                try
                {
                    tracker.GetOrRequest(new IntPtr(1),client,1); first.WaitOne(1000);
                    tracker.GetOrRequest(new IntPtr(2),moved,1.5f); releaseFirst.Set();
                    checks["changed_anchor_request_is_processed"] = second.WaitOne(1000);
                    checks["old_window_query_cannot_publish_to_new_window"] = tracker.GetOrRequest(new IntPtr(2),moved,1.5f).Status != "ok";
                    releaseSecond.Set();
                    checks["new_window_anchor_uses_new_geometry"] = SpinWait.SpinUntil(() => { var value=tracker.GetOrRequest(new IntPtr(2),moved,1.5f);return value.Status == "ok" && value.TitleBounds.Left == 350; },1000);
                }
                finally { tracker.Dispose(); releaseFirst.Set(); releaseSecond.Set(); }
            }

            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var tracker = new TitleAnchorTracker((window, rect, scale) => { entered.Set();release.WaitOne();return Ready(rect,now()); },now))
            {
                try
                {
                    tracker.GetOrRequest(new IntPtr(1),client,1); entered.WaitOne(1000);
                    var clock = Stopwatch.StartNew(); tracker.Dispose();
                    checks["anchor_dispose_does_not_wait_for_provider"] = clock.ElapsedMilliseconds < 500;
                    release.Set();
                    checks["disposed_anchor_cannot_publish_late_result"] = tracker.GetOrRequest(new IntPtr(1),client,1).Status == "header_unavailable";
                }
                finally { release.Set(); }
            }

            using (var tracker = new TitleAnchorTracker((window, rect, scale) => { throw new InvalidOperationException(); },now))
                checks["anchor_provider_exception_is_an_explicit_status"] = SpinWait.SpinUntil(() => tracker.GetOrRequest(new IntPtr(1),client,1).Status == "provider_error",1000);
        }
    }
}
