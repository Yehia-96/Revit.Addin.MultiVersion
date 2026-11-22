using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Linking;
using System;
using System.Diagnostics;
using System.Threading;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// The pane's change check, on a thread of its own, so Revit never waits for the database. It checks the
    /// active model's project at the chosen interval, at once when the model or its link changes, and when
    /// asked (Check now, Try again). Nothing is checked while no linked model is active. It reports each
    /// outcome, or a fault of the add-in's own, from its thread; the caller moves it to Revit's.
    /// </summary>
    internal sealed class CheckLoop : IDisposable
    {
        private readonly LinkChecker _checker;
        private readonly Action<CheckOutcome> _checked;
        private readonly Action<int, Exception> _failed;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _gate = new object();
        private readonly Thread _thread;

        private ModelLink _link;
        private CheckInterval _interval;
        private bool _due;
        private bool _stopping;
        private DateTime? _lastCheckUtc;

        /// <param name="checker">Used on the loop's thread only.</param>
        /// <param name="checkedProject">Each outcome, called on the loop's thread.</param>
        /// <param name="failed">A check that threw: the project id and the fault, called on the loop's thread.</param>
        /// <param name="interval">How often to check.</param>
        public CheckLoop(LinkChecker checker, Action<CheckOutcome> checkedProject, Action<int, Exception> failed, CheckInterval interval)
        {
            _checker = checker ?? throw new ArgumentNullException(nameof(checker));
            _checked = checkedProject ?? throw new ArgumentNullException(nameof(checkedProject));
            _failed = failed ?? throw new ArgumentNullException(nameof(failed));
            _interval = interval ?? CheckInterval.For(CheckSettings.DefaultSeconds);
            _thread = new Thread(Run) { IsBackground = true, Name = "MH Database check" };
            _thread.Start();
        }

        /// <summary>Checks this link's project from now on, right away; null stops checking.</summary>
        public void Watch(ModelLink link)
        {
            lock (_gate)
            {
                _link = link;
                _due = link != null;
            }
            _wake.Set();
        }

        public void CheckNow()
        {
            lock (_gate)
                _due = true;
            _wake.Set();
        }

        public void SetInterval(CheckInterval interval)
        {
            lock (_gate)
                _interval = interval ?? CheckInterval.For(CheckSettings.DefaultSeconds);
            _wake.Set();
        }

        /// <summary>Stops the thread. A database read in progress may outlast this; the thread then ends with Revit.</summary>
        public void Dispose()
        {
            lock (_gate)
                _stopping = true;
            _wake.Set();
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        private void Run()
        {
            while (true)
            {
                ModelLink link;
                TimeSpan wait;
                bool check;
                lock (_gate)
                {
                    if (_stopping)
                        return;
                    DateTime now = DateTime.UtcNow;
                    bool intervalPassed = _interval.ChecksByItself
                        && (!_lastCheckUtc.HasValue || now - _lastCheckUtc.Value >= TimeSpan.FromSeconds(_interval.Seconds));
                    link = _link;
                    check = link != null && (_due || intervalPassed);
                    _due = false;
                    wait = WaitAfter(link, check, now);
                }

                if (check)
                    CheckOnce(link);
                else
                    _wake.WaitOne(wait);
            }
        }

        // How long to sleep when there's nothing to do now; Watch, CheckNow and Dispose wake it early.
        private TimeSpan WaitAfter(ModelLink link, bool checksNow, DateTime now)
        {
            if (checksNow)
                return TimeSpan.Zero;
            if (link == null || !_interval.ChecksByItself || !_lastCheckUtc.HasValue)
                return Timeout.InfiniteTimeSpan;
            TimeSpan left = _lastCheckUtc.Value + TimeSpan.FromSeconds(_interval.Seconds) - now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        private void CheckOnce(ModelLink link)
        {
            try
            {
                CheckOutcome outcome = _checker.Check(link);
                Report(() => _checked(outcome));
            }
            catch (Exception ex)
            {
                Report(() => _failed(link.ProjectId, ex));
            }
            finally
            {
                lock (_gate)
                    _lastCheckUtc = DateTime.UtcNow;
            }
        }

        private static void Report(Action report)
        {
            try
            {
                report();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The MH Database check couldn't report: " + ex);
            }
        }
    }
}
