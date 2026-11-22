using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using Revit.Addin._2027.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Runs the MH Database pane (step 5): follows the active model through the Database button, checks its
    /// project on the <see cref="CheckLoop"/>'s thread, compares the model with the database's values inside
    /// Revit whenever either changes, and shows the result in the pane and as the button's dot. It never
    /// changes the model and only reads the database. Everything but the check runs on Revit's main thread.
    /// </summary>
    internal sealed class LinkMonitor : IPaneActions, IDisposable
    {
        // How long the model must stay unchanged before it's compared again, so dragging an element doesn't
        // compare after every step.
        private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1.5);

        private readonly DatabaseButton _button;
        private readonly Dispatcher _dispatcher;
        private readonly RevitQueue _revit;
        private readonly PaneViewModel _view;
        private readonly CheckLoop _loop;
        private readonly DispatcherTimer _settle;

        private CheckSettings _settings;
        private Document _document;
        private ModelLink _link;
        private CheckOutcome _outcome;
        private string _checkFault;
        private string _compareFault;
        private ChangeReport _report;
        private long _comparedVersion = -1;
        private bool _compareQueued;
        private LinkCondition _buttonCondition = LinkCondition.Checking;
        private int _buttonChanges;

        /// <summary>Create on Revit's main thread, in OnStartup: it makes the pane's external event.</summary>
        public LinkMonitor(DatabaseButton button, Dispatcher dispatcher)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _settings = CheckSettings.Load(CheckSettings.DefaultPath);
            _view = new PaneViewModel(this);
            _settle = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = SettleTime };
            _settle.Tick += OnModelSettled;
            _revit = new RevitQueue(dispatcher);
            var checker = new LinkChecker(
                () => ConnectionFile.Read(ConnectionFile.DefaultPath),
                settings => new SqlLinkDatabase(settings),
                PullCache.DefaultFolder,
                () => DateTime.UtcNow);
            // Last, so nothing above can fail with the thread already running.
            _loop = new CheckLoop(checker, OnChecked, OnCheckFailed, _settings.Interval);
            _button.ModelShown += OnModelShown;
        }

        public PaneViewModel View => _view;

        public void Hook(UIControlledApplication application) =>
            application.ControlledApplication.DocumentChanged += OnDocumentChanged;

        public void Unhook(UIControlledApplication application) =>
            application.ControlledApplication.DocumentChanged -= OnDocumentChanged;

        public void Dispose()
        {
            _button.ModelShown -= OnModelShown;
            _settle.Stop();
            _settle.Tick -= OnModelSettled;
            _loop.Dispose();
            _revit.Dispose();
        }

        // ---- The pane's buttons (main thread, outside Revit's API context) ----

        public void CheckNow() => _loop.CheckNow();

        public void Link() => _revit.Run("linking the model", application => RunCommand(application, "Link project",
            (UIApplication app, ref string message) => LinkProjectCommand.Run(app, ref message)));

        public void CheckParameters() => _revit.Run("checking parameters", application => RunCommand(application, "Check parameters",
            (UIApplication app, ref string message) => CheckParametersCommand.Run(app, ref message)));

        public void Select(IReadOnlyList<long> elementIds, bool zoom)
        {
            List<long> ids = (elementIds ?? new long[0]).ToList();
            Document shown = _document;
            _revit.Run("selecting elements", application => SelectInModel(application, shown, ids, zoom));
        }

        public void ChooseInterval(CheckInterval interval)
        {
            if (interval == null || interval.Seconds == _settings.Interval.Seconds)
                return;
            _settings = new CheckSettings(interval.Seconds);
            string problem;
            if (!_settings.TrySave(CheckSettings.DefaultPath, out problem))
                Trace.WriteLine("[Parameter Tools] " + problem);
            _loop.SetInterval(_settings.Interval);
            Render();
        }

        // ---- Revit (main thread, API context) ----

        // Another model, or another link: check its project at once, and compare when its values are in.
        private void OnModelShown(Document document, ModelLink link)
        {
            bool sameProject = link != null && _link != null && link.ProjectId == _link.ProjectId;
            _document = document;
            _link = link;
            if (!sameProject)
            {
                _outcome = null;
                _checkFault = null;
            }
            _compareFault = null;
            _report = null;
            _comparedVersion = -1;
            _buttonCondition = LinkCondition.Checking;
            _buttonChanges = 0;
            _loop.Watch(link);
            if (_outcome != null && _outcome.Data != null)
                QueueCompare();
            Render();
        }

        // Any change to the shown model: compare again once it has settled. Undo and Redo count too.
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (_document == null || !_document.IsValidObject || _outcome == null || _outcome.Data == null)
                    return;
                if (!_document.Equals(e.GetDocument()))
                    return;
                _settle.Stop();
                _settle.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The MH Database pane missed a change: " + ex);
            }
        }

        private void OnModelSettled(object sender, EventArgs e)
        {
            _settle.Stop();
            QueueCompare();
        }

        private void QueueCompare()
        {
            if (_compareQueued)
                return;
            _compareQueued = true;
            _revit.Run("comparing the model with the database", application =>
            {
                _compareQueued = false;
                Compare();
            });
        }

        private void Compare()
        {
            Document document = _document;
            CheckOutcome outcome = _outcome;
            if (document == null || !document.IsValidObject || outcome == null || outcome.Data == null)
                return;

            LinkProjectData data = outcome.Data;
            try
            {
                var values = new RevitValueCompare(document);
                _report = ChangeFinder.Find(data, ModelReader.Elements(document, data), ModelReader.Materials(document), values.Compare);
                _comparedVersion = outcome.DataVersion;
                _compareFault = null;
            }
            catch (Exception ex)
            {
                // Said in the pane, with Try again; the next check or change compares once more.
                Trace.WriteLine("[Parameter Tools] The MH Database pane couldn't compare the model: " + ex);
                _report = null;
                _compareFault = "Comparing the model with the database failed: " + ex.Message;
            }
            Render();
        }

        // ---- The check (from its thread, moved to the main thread) ----

        private void OnChecked(CheckOutcome outcome) => Post("taking a check", () => Take(outcome));

        private void OnCheckFailed(int projectId, Exception fault)
        {
            Trace.WriteLine("[Parameter Tools] The MH Database check failed: " + fault);
            Post("taking a failed check", () =>
            {
                if (_link == null || _link.ProjectId != projectId)
                    return;
                _checkFault = fault.Message;
                Render();
            });
        }

        private void Take(CheckOutcome outcome)
        {
            if (_link == null || outcome.ProjectId != _link.ProjectId)
                return;
            _outcome = outcome;
            _checkFault = null;
            if (outcome.CacheProblem != null)
                Trace.WriteLine("[Parameter Tools] " + outcome.CacheProblem);

            if (outcome.Data == null)
            {
                _report = null;
                _comparedVersion = -1;
            }
            else if (outcome.DataVersion != _comparedVersion || _report == null)
            {
                QueueCompare();
            }
            Render();
        }

        private void Post(string what, Action action)
        {
            if (_dispatcher.HasShutdownStarted)
                return;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[Parameter Tools] The MH Database pane failed " + what + ": " + ex);
                }
            }));
        }

        // ---- Showing it ----

        private void Render()
        {
            var snapshot = new PaneSnapshot(_document != null, _link, ConnectionFile.DefaultPath, _outcome, _compareFault ?? _checkFault, _report,
                _settings.Interval, DateTime.UtcNow, TimeZoneInfo.Local);
            _view.Show(snapshot);

            LinkCondition condition = snapshot.Condition;
            int changes = snapshot.ChangeCount;
            if (condition == _buttonCondition && changes == _buttonChanges)
                return;
            _buttonCondition = condition;
            _buttonChanges = changes;
            Document document = _document;
            ModelLink link = _link;
            _revit.Run("showing the check on the Database button", application => _button.ShowCondition(document, link, condition, changes));
        }

        private static void SelectInModel(UIApplication application, Document shown, List<long> ids, bool zoom)
        {
            UIDocument active = application.ActiveUIDocument;
            if (active == null || shown == null || !shown.IsValidObject || !active.Document.Equals(shown))
                return;

            List<ElementId> elementIds = ids.Select(id => new ElementId(id)).Where(id => active.Document.GetElement(id) != null).ToList();
            active.Selection.SetElementIds(elementIds);
            if (!zoom || elementIds.Count == 0)
                return;
            try
            {
                active.ShowElements(elementIds);
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                TaskDialog.Show("MH Database", "The elements are selected, but Revit couldn't show them: " + ex.Message);
            }
        }

        private delegate Result PaneCommand(UIApplication application, ref string message);

        private static void RunCommand(UIApplication application, string title, PaneCommand command)
        {
            string message = null;
            if (command(application, ref message) == Result.Failed && !string.IsNullOrWhiteSpace(message))
                TaskDialog.Show(title, title + " stopped: " + message);
        }
    }
}
