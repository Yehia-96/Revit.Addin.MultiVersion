using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Runs the pane's work inside Revit: a pane's buttons and the check's thread can't call Revit's API
    /// themselves, so the work is queued here, and Revit runs it through one external event as soon as it's
    /// idle. Revit runs nothing of it while a command or edit mode is active; when it turns the event down,
    /// it's asked again a second later, so queued work never waits for something else to come along.
    /// Create it and call <see cref="Run"/> on Revit's main thread; create it in API context (OnStartup).
    /// </summary>
    internal sealed class RevitQueue : IExternalEventHandler, IDisposable
    {
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);

        private readonly ConcurrentQueue<Work> _work = new ConcurrentQueue<Work>();
        private readonly ExternalEvent _event;
        private readonly DispatcherTimer _retry;

        public RevitQueue(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            _retry = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = RetryAfter };
            _retry.Tick += OnRetry;
            _event = ExternalEvent.Create(this);
        }

        public void Run(string what, Action<UIApplication> work)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));
            _work.Enqueue(new Work(what, work));
            Raise();
        }

        public void Execute(UIApplication application)
        {
            Work work;
            while (_work.TryDequeue(out work))
            {
                try
                {
                    work.Action(application);
                }
                catch (Exception ex)
                {
                    // Never into Revit: the pane's own code says what went wrong where it matters.
                    Trace.WriteLine("[Parameter Tools] The MH Database pane failed " + work.What + ": " + ex);
                }
            }
        }

        public string GetName() => "MH Database pane";

        public void Dispose()
        {
            _retry.Stop();
            _retry.Tick -= OnRetry;
            _event.Dispose();
        }

        // Accepted: Revit runs it when idle. Pending: already on its way, and it takes everything queued by
        // then. Denied or TimedOut: ask again shortly.
        private void Raise()
        {
            ExternalEventRequest raised = _event.Raise();
            if (raised == ExternalEventRequest.Denied || raised == ExternalEventRequest.TimedOut)
                _retry.Start();
        }

        private void OnRetry(object sender, EventArgs e)
        {
            _retry.Stop();
            if (!_work.IsEmpty)
                Raise();
        }

        private sealed class Work
        {
            public Work(string what, Action<UIApplication> action)
            {
                What = what ?? "";
                Action = action;
            }

            public string What { get; }

            public Action<UIApplication> Action { get; }
        }
    }
}
