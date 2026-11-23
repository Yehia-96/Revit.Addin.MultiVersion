using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using MH.RevitTools.Database.SigningIn;
using Revit.Addin._2027.Commands;
using Revit.Addin._2027.DatabaseLink.Apply;
using Revit.Addin._2027.DatabaseLink.SignIn;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Runs the MH Database pane: follows the active model through the Database button, checks its project on
    /// the <see cref="CheckLoop"/>'s thread, compares the model with the database's values inside Revit whenever
    /// either changes, shows the result in the pane and as the button's dot (step 5), and applies the changes
    /// the user asks for (step 6). The database is read only while someone is signed in to it (step 8). Only Apply
    /// changes the model; nothing ever changes the database. Everything but the check runs on Revit's main thread.
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
        private bool _isReadOnlyFile;
        private ApplyResult _applied;
        private DateTime _appliedAtUtc;
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
                settings => SignInGate.Session.PersonFor(settings) != null,
                PullCache.DefaultFolder,
                () => DateTime.UtcNow);
            // Last, so nothing above can fail with the thread already running.
            _loop = new CheckLoop(checker, OnChecked, OnCheckFailed, _settings.Interval);
            _button.ModelShown += OnModelShown;
            SignInGate.Session.Changed += OnSignInChanged;
            Current = this;
        }

        /// <summary>The one running with Revit, for the ribbon's Apply changes; null when it didn't start.</summary>
        public static LinkMonitor Current { get; private set; }

        public PaneViewModel View => _view;

        public void Hook(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentChanged += OnDocumentChanged;
            application.ControlledApplication.DocumentSynchronizedWithCentral += OnSynchronizedWithCentral;
            application.ControlledApplication.DocumentReloadedLatest += OnReloadedLatest;
        }

        public void Unhook(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            application.ControlledApplication.DocumentSynchronizedWithCentral -= OnSynchronizedWithCentral;
            application.ControlledApplication.DocumentReloadedLatest -= OnReloadedLatest;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current, this))
                Current = null;
            _button.ModelShown -= OnModelShown;
            SignInGate.Session.Changed -= OnSignInChanged;
            _settle.Stop();
            _settle.Tick -= OnModelSettled;
            _loop.Dispose();
            _revit.Dispose();
        }

        // ---- The pane's buttons (main thread, outside Revit's API context) ----

        // Asked for by hand, so the model is compared again too, even with the same values: someone may have
        // released elements since.
        public void CheckNow()
        {
            _comparedVersion = -1;
            _loop.CheckNow();
        }

        public void Link() => _revit.Run("linking the model", application => RunCommand(application, "Link project",
            (UIApplication app, ref string message) => LinkProjectCommand.Run(app, ref message)));

        public void SignIn() => _revit.Run("signing in", application => RunCommand(application, "Sign in",
            (UIApplication app, ref string message) => SignInCommand.Run(app, ref message)));

        public void CheckParameters() => _revit.Run("checking parameters", application => RunCommand(application, "Check parameters",
            (UIApplication app, ref string message) => CheckParametersCommand.Run(app, ref message)));

        public void Select(IReadOnlyList<long> elementIds, bool zoom)
        {
            List<long> ids = (elementIds ?? new long[0]).ToList();
            Document shown = _document;
            _revit.Run("selecting elements", application => SelectInModel(application, shown, ids, zoom));
        }

        public void Apply(IReadOnlyList<ChangeGroup> groups)
        {
            List<ChangeGroup> chosen = (groups ?? new ChangeGroup[0]).ToList();
            Document shown = _document;
            _revit.Run("applying changes", application => ApplyTo(shown, chosen));
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

        // ---- The ribbon's Apply changes (API context) ----

        /// <summary>Applies everything the pane lists, after asking; says why when there's nothing to apply.</summary>
        public Result ApplyFromRibbon(UIApplication application)
        {
            if (_link != null && _outcome != null && _outcome.State == CheckState.NotSignedIn)
                return SignInBeforeApplying(application);

            // As they are now: while the question is open, a check can bring others.
            Document document = _document;
            ChangeReport report = _report;
            UIDocument active = application.ActiveUIDocument;
            string why = active == null || !IsShown(active.Document)
                ? "Switch to a view of the model first."
                : ApplyTexts.WhyNothingToApply(_link != null, _outcome == null ? (CheckState?)null : _outcome.State, report)
                    ?? ApplyTexts.ApplyWaits(_outcome.State, document.IsReadOnly);
            if (why != null)
            {
                Tell("Nothing to apply", why, TaskDialogIcon.TaskDialogIconInformation);
                return Result.Cancelled;
            }

            var question = new TaskDialog(DatabasePane.Title)
            {
                MainInstruction = ApplyTexts.ConfirmTitle(report),
                MainContent = ApplyTexts.ConfirmText(report, _link.Describe()),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            question.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Apply them", "Write the database's values into the model.");
            question.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Show them first", "Open the MH Database pane, which lists each change.");
            switch (question.Show())
            {
                case TaskDialogResult.CommandLink1:
                    break;
                case TaskDialogResult.CommandLink2:
                    DatabasePane.Show(application);
                    return Result.Cancelled;
                default:
                    return Result.Cancelled;
            }

            ApplyResult result = ApplyTo(document, report.Groups);
            if (result == null)
                Tell(ApplyTexts.AppliedTitle(null), "The database or the model changed while the question was open. The MH Database pane "
                    + "shows how it is now.", TaskDialogIcon.TaskDialogIconInformation);
            else if (result.IsEmpty)
                Tell(ApplyTexts.AppliedTitle(null), "The model already has the values.", TaskDialogIcon.TaskDialogIconInformation);
            else if (!DatabasePane.IsShown(application))
                // The pane shows the result; without it, a message does.
                Tell(ApplyTexts.AppliedTitle(result),
                    ApplyTexts.AppliedDetail(result, DateTime.UtcNow, DateTime.UtcNow, TimeZoneInfo.Local),
                    result.Changes > 0 ? TaskDialogIcon.TaskDialogIconInformation : TaskDialogIcon.TaskDialogIconWarning);
            return Result.Succeeded;
        }

        // Signing in only starts the check; what there is to apply is known once it's done.
        private static Result SignInBeforeApplying(UIApplication application)
        {
            ConnectionFileResult file = ConnectionFile.Read(ConnectionFile.DefaultPath);
            if (!file.IsLoaded)
                Tell("Can't use the connection file", file.Problem, TaskDialogIcon.TaskDialogIconWarning);
            else if (SignInGate.Ensure(application, file.Settings))
                Tell("Signed in", "The MH Database pane checks for changes now. Apply them once it lists them.", TaskDialogIcon.TaskDialogIconInformation);
            return Result.Cancelled;
        }

        // ---- Revit (main thread, API context) ----

        // Someone signed in or out. The pane shows it at once, and stops offering Apply at once; the check that
        // follows can take a moment.
        private void OnSignInChanged(object sender, EventArgs e) =>
            Safely("following the sign-in", () =>
            {
                if (SignInGate.Session.Person == null)
                    _outcome = _outcome?.SignedOut(DateTime.UtcNow);
                else if (_outcome != null && _outcome.State == CheckState.NotSignedIn)
                    _outcome = null;
                _loop.CheckNow();
                Render();
            });

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
            _applied = null;
            _isReadOnlyFile = false;
            _comparedVersion = -1;
            _buttonCondition = LinkCondition.Checking;
            _buttonChanges = 0;
            _loop.Watch(link);
            if (_outcome != null && _outcome.Data != null)
                QueueCompare();
            Render();
        }

        // Any change to the shown model: compare again once it has settled. Undo and Redo count too; undoing an
        // Apply also takes back what the pane says about it.
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e) =>
            Safely("following a change", () =>
            {
                if (!IsShown(e.GetDocument()))
                    return;
                if (_applied != null && e.Operation == UndoOperation.TransactionUndone
                    && e.GetTransactionNames().Any(name => name != null && name.StartsWith(ApplyTexts.UndoPrefix, StringComparison.Ordinal)))
                {
                    _applied = null;
                    Render();
                }
                CompareWhenSettled();
            });

        // Other people's changes, and who has borrowed what, arrive with these.
        private void OnSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e) =>
            Safely("following a synchronization", () =>
            {
                if (IsShown(e.Document))
                    CompareWhenSettled();
            });

        private void OnReloadedLatest(object sender, DocumentReloadedLatestEventArgs e) =>
            Safely("following Reload Latest", () =>
            {
                if (IsShown(e.Document))
                    CompareWhenSettled();
            });

        private void CompareWhenSettled()
        {
            if (_outcome == null || _outcome.Data == null)
                return;
            _settle.Stop();
            _settle.Start();
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
                _isReadOnlyFile = document.IsReadOnlyFile;
                var values = new RevitValues(document);
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

        // Writes what the pane listed, then compares again. Nothing happens, and the answer is null, when the
        // pane has moved on to another model or state since the click; it shows that already.
        private ApplyResult ApplyTo(Document document, IReadOnlyList<ChangeGroup> groups)
        {
            CheckOutcome outcome = _outcome;
            if (groups.Count == 0 || !IsShown(document) || outcome == null || outcome.State != CheckState.Answered || outcome.Data == null)
                return null;

            ApplyResult result;
            try
            {
                result = DatabaseApplier.Apply(document, outcome.Data, groups);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The MH Database pane couldn't apply the changes: " + ex);
                result = ApplyResult.Stop("Something went wrong in the add-in: " + ex.Message);
            }

            // Nothing left to do, as on a double-click: what the first click did stays on show.
            if (!result.IsEmpty)
            {
                _applied = result;
                _appliedAtUtc = DateTime.UtcNow;
            }
            Compare();
            return result;
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
            // Someone signed out while it was being read; the check that says so is on its way.
            if (outcome.State == CheckState.Answered && SignInGate.Session.Person == null)
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
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Safely(what, action)));
        }

        // ---- Showing it ----

        private void Render()
        {
            SignedInPerson person = SignInGate.Session.Person;
            var snapshot = new PaneSnapshot(_document != null, _link, ConnectionFile.DefaultPath, person == null ? null : person.Name, _outcome,
                _compareFault ?? _checkFault, _report, _isReadOnlyFile, _applied, _appliedAtUtc, _settings.Interval, DateTime.UtcNow, TimeZoneInfo.Local);
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

        private bool IsShown(Document document) =>
            document != null && document.IsValidObject && _document != null && _document.IsValidObject && _document.Equals(document);

        // An event handler must never throw into Revit.
        private static void Safely(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The MH Database pane failed " + what + ": " + ex);
            }
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
                TaskDialog.Show(DatabasePane.Title, "The elements are selected, but Revit couldn't show them: " + ex.Message);
            }
        }

        private static void Tell(string headline, string content, TaskDialogIcon icon)
        {
            var dialog = new TaskDialog(DatabasePane.Title)
            {
                MainInstruction = headline,
                MainContent = content,
                MainIcon = icon,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
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
