using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PersonalUsageOverlay
{
    internal sealed class TitleAnchorObservation
    {
        public string Status;
        public Rectangle TitleBounds, HeaderBounds;
        public DateTime ObservedAtUtc;
        public long ElapsedMilliseconds;
    }

    // Read-only UIA access. Call on the single background MTA worker, never the UI thread.
    internal sealed class TitleAnchorReader
    {
        private const int MaxNodes = 600, MaxDepth = 28, SearchBudgetMilliseconds = 1500;
        private const int SearchTopDips = 240;
        private IntPtr cachedWindow;
        private Candidate cached;
        private DateTime cachedAtUtc;

        private sealed class Snapshot
        {
            public ControlType Type;
            public string Name, Id, ClassName;
            public bool Offscreen;
            public Rectangle Bounds;
            public bool HasBounds;
        }

        private sealed class Visit
        {
            public AutomationElement Element, Document, Header, Toolbar;
            public Snapshot DocumentInfo, HeaderInfo, ToolbarInfo;
            public int Depth;
        }

        private sealed class Candidate
        {
            public AutomationElement Title, Document, Header, Toolbar;
            public Rectangle TitleBounds, HeaderBounds;
        }

        private sealed class SearchLimitException : Exception { }

        public TitleAnchorObservation Query(IntPtr hwnd, Rectangle clientBounds, float scale)
        {
            var clock = Stopwatch.StartNew();
            if (hwnd != cachedWindow)
            {
                cached = null;
                cachedWindow = hwnd;
            }
            if (hwnd == IntPtr.Zero || clientBounds.Width <= 0 || clientBounds.Height <= 0
                || float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
            {
                cached = null;
                return Result("header_unavailable", null, clock);
            }
            try
            {
                var request = CreateRequest();
                // Reacquire periodically so a newly introduced second header is not cached away.
                if (cached != null && DateTime.UtcNow - cachedAtUtc < TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        if (Refresh(cached, request, clientBounds, scale, clock))
                            return Result("ok", cached, clock);
                    }
                    catch (Exception error)
                    {
                        if (!IsProviderFailure(error)) throw;
                    }
                }
                cached = null;
                bool ambiguous;
                var found = Search(hwnd, request, clientBounds, scale, clock, out ambiguous);
                if (ambiguous) return Result("ambiguous", null, clock);
                if (found == null || !Refresh(found, request, clientBounds, scale, clock))
                    return Result("header_unavailable", null, clock);
                cached = found;
                cachedAtUtc = DateTime.UtcNow;
                return Result("ok", found, clock);
            }
            catch (SearchLimitException)
            {
                cached = null;
                return Result("search_timeout", null, clock);
            }
            catch (Exception error)
            {
                cached = null;
                if (!IsProviderFailure(error)) throw;
                return Result("provider_error", null, clock);
            }
        }

        private static CacheRequest CreateRequest()
        {
            var request = new CacheRequest { TreeScope = TreeScope.Element, TreeFilter = Automation.RawViewCondition };
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            return request;
        }

        private static Snapshot Read(AutomationElement element, CacheRequest request, Stopwatch clock)
        {
            CheckBudget(clock);
            var info = element.GetUpdatedCache(request).Cached;
            var snapshot = new Snapshot
            {
                Type = info.ControlType,
                Name = (info.Name ?? "").Trim(),
                Id = info.AutomationId ?? "",
                ClassName = info.ClassName ?? "",
                Offscreen = info.IsOffscreen
            };
            Rectangle bounds;
            snapshot.HasBounds = TryRectangle(info.BoundingRectangle, out bounds);
            snapshot.Bounds = bounds;
            CheckBudget(clock);
            return snapshot;
        }

        private static Candidate Search(IntPtr hwnd, CacheRequest request, Rectangle client, float scale,
            Stopwatch clock, out bool ambiguous)
        {
            ambiguous = false;
            Candidate found = null;
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<Visit>();
            CheckBudget(clock);
            queue.Enqueue(new Visit { Element = AutomationElement.FromHandle(hwnd) });
            int scheduled = 1;
            var top = new Rectangle(client.Left, client.Top, client.Width,
                Math.Min(client.Height, (int)Math.Ceiling(SearchTopDips * scale)));
            while (queue.Count > 0)
            {
                CheckBudget(clock);
                var visit = queue.Dequeue();
                var info = Read(visit.Element, request, clock);
                if (info.Offscreen || IsSidebar(info.ClassName) || IsConversationBody(info.ClassName)
                    || (info.HasBounds && !info.Bounds.IntersectsWith(top))) continue;

                if (info.Type == ControlType.Document && info.Id == "RootWebArea")
                {
                    visit.Document = visit.Element;
                    visit.DocumentInfo = info;
                    visit.Header = null; visit.HeaderInfo = null;
                    visit.Toolbar = null; visit.ToolbarInfo = null;
                }
                if (visit.Document != null && IsHeader(info.ClassName))
                {
                    visit.Header = visit.Element;
                    visit.HeaderInfo = info;
                    visit.Toolbar = null; visit.ToolbarInfo = null;
                }
                if (visit.Header != null && info.Type == ControlType.ToolBar)
                {
                    visit.Toolbar = visit.Element;
                    visit.ToolbarInfo = info;
                }
                if (visit.DocumentInfo != null && visit.HeaderInfo != null && visit.ToolbarInfo != null
                    && Valid(info, visit.DocumentInfo, visit.HeaderInfo, visit.ToolbarInfo, client, scale))
                {
                    if (found != null && (found.TitleBounds != info.Bounds || found.HeaderBounds != visit.HeaderInfo.Bounds))
                    {
                        ambiguous = true;
                        return null;
                    }
                    found = new Candidate { Title = visit.Element, Document = visit.Document, Header = visit.Header,
                        Toolbar = visit.Toolbar, TitleBounds = info.Bounds, HeaderBounds = visit.HeaderInfo.Bounds };
                }

                CheckBudget(clock);
                var child = walker.GetFirstChild(visit.Element);
                if (child != null && visit.Depth >= MaxDepth) throw new SearchLimitException();
                while (child != null)
                {
                    CheckBudget(clock);
                    if (scheduled++ >= MaxNodes) throw new SearchLimitException();
                    queue.Enqueue(new Visit { Element = child, Depth = visit.Depth + 1, Document = visit.Document,
                        DocumentInfo = visit.DocumentInfo, Header = visit.Header, HeaderInfo = visit.HeaderInfo,
                        Toolbar = visit.Toolbar, ToolbarInfo = visit.ToolbarInfo });
                    child = walker.GetNextSibling(child);
                }
            }
            return found;
        }

        private static bool Refresh(Candidate candidate, CacheRequest request, Rectangle client, float scale, Stopwatch clock)
        {
            var document = Read(candidate.Document, request, clock);
            var header = Read(candidate.Header, request, clock);
            var toolbar = Read(candidate.Toolbar, request, clock);
            var title = Read(candidate.Title, request, clock);
            if (!Valid(title, document, header, toolbar, client, scale)) return false;
            candidate.TitleBounds = title.Bounds;
            candidate.HeaderBounds = header.Bounds;
            return true;
        }

        private static bool Valid(Snapshot title, Snapshot document, Snapshot header, Snapshot toolbar, Rectangle client, float scale)
        {
            return title.Type == ControlType.Text && document.Type == ControlType.Document && document.Id == "RootWebArea"
                && toolbar.Type == ControlType.ToolBar && IsHeader(header.ClassName)
                && !title.Offscreen && !document.Offscreen && !header.Offscreen && !toolbar.Offscreen
                && title.Name.Length > 0 && string.Equals(title.Name, document.Name, StringComparison.Ordinal)
                && title.HasBounds && header.HasBounds && toolbar.HasBounds
                && client.Contains(header.Bounds) && client.Contains(title.Bounds)
                && header.Bounds.Contains(toolbar.Bounds) && toolbar.Bounds.Contains(title.Bounds)
                && header.Bounds.Height <= Math.Ceiling(160 * scale)
                && header.Bounds.Bottom <= client.Top + Math.Ceiling(SearchTopDips * scale);
        }

        private static bool IsHeader(string classes)
        {
            return HasClassPrefix(classes, "MainTitlebar-") || HasClassPrefix(classes, "ApplicationMenuPageHeader-");
        }

        private static bool IsSidebar(string classes)
        {
            return HasClassPrefix(classes, "app-shell-left-panel") || HasClassPrefix(classes, "ConversationSidebar-");
        }

        private static bool IsConversationBody(string classes)
        {
            // In the observed Windows tree these transcript surfaces are siblings of the header.
            return HasClassPrefix(classes, "MainContentSurface-") || HasClassPrefix(classes, "MainContentViewport-");
        }

        private static bool HasClassPrefix(string classes, string prefix)
        {
            foreach (string token in (classes ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (token.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool TryRectangle(System.Windows.Rect value, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (value.IsEmpty || !Finite(value.Left) || !Finite(value.Top) || !Finite(value.Right) || !Finite(value.Bottom)
                || value.Width <= 0 || value.Height <= 0) return false;
            double left = Math.Floor(value.Left), top = Math.Floor(value.Top);
            double right = Math.Ceiling(value.Right), bottom = Math.Ceiling(value.Bottom);
            if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
                || right - left > int.MaxValue || bottom - top > int.MaxValue) return false;
            rectangle = Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
            return true;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static void CheckBudget(Stopwatch clock)
        {
            if (clock.ElapsedMilliseconds >= SearchBudgetMilliseconds) throw new SearchLimitException();
        }

        private static bool IsProviderFailure(Exception error)
        {
            return error is ElementNotAvailableException || error is InvalidOperationException || error is COMException
                || error is ArgumentException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
        }

        private static TitleAnchorObservation Result(string status, Candidate candidate, Stopwatch clock)
        {
            return new TitleAnchorObservation { Status = status,
                TitleBounds = candidate == null ? Rectangle.Empty : candidate.TitleBounds,
                HeaderBounds = candidate == null ? Rectangle.Empty : candidate.HeaderBounds,
                ObservedAtUtc = DateTime.UtcNow, ElapsedMilliseconds = clock.ElapsedMilliseconds };
        }
    }
}
