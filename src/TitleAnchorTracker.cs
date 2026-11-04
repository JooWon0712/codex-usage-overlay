using System;
using System.Drawing;
using System.Threading;

namespace PersonalUsageOverlay
{
    internal sealed class TitleAnchorTracker : IDisposable
    {
        internal static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(2);
        private sealed class Request
        {
            public IntPtr Window;
            public Rectangle Client;
            public float Scale;
            public long Generation;
        }

        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Func<IntPtr, Rectangle, float, TitleAnchorObservation> query;
        private readonly Func<DateTime> clock;
        private Request pending, currentKey;
        private TitleAnchorObservation observation;
        private DateTime nextQuery = DateTime.MinValue, startedAt;
        private bool stopped, busy;
        private long generation;

        public TitleAnchorTracker() : this(null, null) { }

        internal TitleAnchorTracker(Func<IntPtr, Rectangle, float, TitleAnchorObservation> query, Func<DateTime> clock)
        {
            this.query = query;
            this.clock = clock ?? (() => DateTime.UtcNow);
            var thread = new Thread(ReadLoop) { IsBackground = true, Name = "Usage Overlay title position" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public TitleAnchorObservation GetOrRequest(IntPtr window, Rectangle client, float scale)
        {
            lock (gate)
            {
                DateTime now = clock();
                if (stopped)
                    return Status("header_unavailable");
                if (currentKey == null || currentKey.Window != window || currentKey.Client != client || currentKey.Scale != scale)
                {
                    currentKey = new Request { Window = window, Client = client, Scale = scale, Generation = ++generation };
                    observation = null;
                    nextQuery = DateTime.MinValue;
                }
                if (now >= nextQuery)
                {
                    // One worker and one replaceable pending request; a slow provider cannot create more threads.
                    pending = currentKey;
                    nextQuery = now.AddMilliseconds(500);
                    wake.Set();
                }
                if (observation != null && IsFresh(observation.ObservedAtUtc, now))
                    return observation;
                return Status(busy && now - startedAt >= FreshFor ? "search_timeout" : "pending");
            }
        }

        internal static bool IsFresh(DateTime observed, DateTime now)
        {
            return observed <= now && now - observed < FreshFor;
        }

        private TitleAnchorObservation Status(string status)
        {
            return new TitleAnchorObservation { Status = status, ObservedAtUtc = clock() };
        }

        private void ReadLoop()
        {
            var reader = query == null ? new TitleAnchorReader() : null;
            try
            {
                while (true)
                {
                    wake.WaitOne();
                    Request request;
                    lock (gate)
                    {
                        if (stopped)
                            return;
                        request = pending;
                        pending = null;
                        if (request == null)
                            continue;
                        busy = true;
                        startedAt = clock();
                    }
                    TitleAnchorObservation result;
                    try { result = query == null ? reader.Query(request.Window, request.Client, request.Scale) : query(request.Window, request.Client, request.Scale); }
                    catch { result = Status("provider_error"); }
                    lock (gate)
                    {
                        busy = false;
                        if (!stopped && request.Generation == generation)
                        {
                            observation = result;
                            if (result.Status != "ok") nextQuery = clock().AddSeconds(2);
                        }
                    }
                }
            }
            finally { wake.Dispose(); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (stopped)
                    return;
                stopped = true;
                pending = null;
                observation = null;
                wake.Set();
            }
            // UIA provider RPC is not cancellable here. Never block X waiting for it.
            // This background MTA thread cannot keep the application process alive.
        }
    }
}
