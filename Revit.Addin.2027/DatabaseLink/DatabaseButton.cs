using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using MH.RevitTools.Database.Linking;
using System;
using System.Diagnostics;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Keeps the ribbon's big Database button showing the active model's link (<see cref="DatabaseButtonFace"/>):
    /// the project's number, and a dot on the icon with what the MH Database pane's last check found (green up
    /// to date, blue with the number of changes, red not reachable). It follows the active view from model to
    /// model, and every change of the link: Link project, Undo and Redo, and what synchronizing with central
    /// brings. It tells the pane which model is active (<see cref="ModelShown"/>), and the pane tells it what
    /// the check found (<see cref="ShowCondition"/>). It reads only the link saved in the model.
    /// </summary>
    internal sealed class DatabaseButton
    {
        private static DatabaseButton _current;

        private readonly PushButton _button;
        private readonly ButtonIcons _icons;
        private readonly ElementFilter _projectInformation = new ElementCategoryFilter(BuiltInCategory.OST_ProjectInformation);

        private UIControlledApplication _application;
        private DatabaseButtonFace _face = DatabaseButtonFace.NoModel;
        private Document _shown;
        private ModelLink _link;
        private LinkCondition _condition = LinkCondition.Checking;
        private int _changes;
        private int? _shownClosingId;
        private bool _waitingForIdle;

        /// <param name="button">The big Database button, as added with its plain icons.</param>
        public DatabaseButton(PushButton button)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
            _icons = new ButtonIcons(button.Image, button.LargeImage);
        }

        /// <summary>
        /// The active project model, or another one's link, is shown now: the model (null without a project
        /// model) and its link (null when it isn't linked). Raised on Revit's main thread, in API context.
        /// </summary>
        public event Action<Document, ModelLink> ModelShown;

        /// <summary>The project model whose link the button shows; null when none.</summary>
        public Document Shown => _shown != null && _shown.IsValidObject ? _shown : null;

        /// <summary>Its link; null when it isn't linked.</summary>
        public ModelLink Link => Shown == null ? null : _link;

        /// <summary>Shows the model's link right away, e.g. after Link project saved it.</summary>
        public static void Refresh(Document document)
        {
            DatabaseButton current = _current;
            if (current != null)
                current.Safely(() => current.Show(document), "refreshing");
        }

        public void Hook(UIControlledApplication application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _current = this;
            application.ViewActivated += OnViewActivated;
            application.ControlledApplication.DocumentChanged += OnDocumentChanged;
            application.ControlledApplication.DocumentSynchronizedWithCentral += OnSynchronizedWithCentral;
            application.ControlledApplication.DocumentReloadedLatest += OnReloadedLatest;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;
            application.ControlledApplication.DocumentClosed += OnDocumentClosed;
        }

        public void Unhook(UIControlledApplication application)
        {
            application.ViewActivated -= OnViewActivated;
            application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            application.ControlledApplication.DocumentSynchronizedWithCentral -= OnSynchronizedWithCentral;
            application.ControlledApplication.DocumentReloadedLatest -= OnReloadedLatest;
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            StopWaitingForIdle();
            if (ReferenceEquals(_current, this))
                _current = null;
            _projectInformation.Dispose();
        }

        /// <summary>
        /// What the pane's last check found for that model and link. Ignored when the button shows another
        /// one by now: the pane's word can arrive after a switch. Call in API context.
        /// </summary>
        public void ShowCondition(Document document, ModelLink link, LinkCondition condition, int changes) =>
            Safely(() =>
            {
                // A model closed since then is no longer valid; asking it anything more would throw.
                if (document == null || !document.IsValidObject || !IsShown(document) || !SameLink(link, _link))
                    return;
                _condition = condition;
                _changes = changes;
                Put(FaceNow());
            }, "showing the check");

        // Another model's view, or another view of the same model: show that model's link.
        private void OnViewActivated(object sender, ViewActivatedEventArgs e) =>
            Safely(() => Show(e.Document), "switching views");

        // Link project, Undo and Redo of it, and anything else that changes Project Information.
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e) =>
            Safely(() =>
            {
                Document document = e.GetDocument();
                if (IsShown(document) && e.GetModifiedElementIds(_projectInformation).Count > 0)
                    Show(document);
            }, "following a change");

        // Someone else's link arrives with their changes.
        private void OnSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e) =>
            Safely(() => ShowAgain(e.Document), "after synchronizing");

        private void OnReloadedLatest(object sender, DocumentReloadedLatestEventArgs e) =>
            Safely(() => ShowAgain(e.Document), "after Reload Latest");

        // When the model shown closes, the button goes back to plain "Database", unless another model's view
        // took over in between. Revit may make the next model active without a view activation, so once
        // Revit is idle again the button shows whatever model is active then.
        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e) =>
            Safely(() =>
            {
                if (IsShown(e.Document))
                    _shownClosingId = e.DocumentId;
            }, "closing a model");

        private void OnDocumentClosed(object sender, DocumentClosedEventArgs e) =>
            Safely(() =>
            {
                bool shownClosed = _shownClosingId == e.DocumentId;
                if (shownClosed)
                    _shownClosingId = null;
                if (e.Status != RevitAPIEventStatus.Succeeded)
                    return;
                if (shownClosed)
                    Show(null);
                ShowActiveWhenIdle();
            }, "after closing a model");

        private void ShowActiveWhenIdle()
        {
            if (_waitingForIdle || _application == null)
                return;
            _application.Idling += OnIdling;
            _waitingForIdle = true;
        }

        // Once, after a model closed: Revit's sender here is the UIApplication, which knows the active model.
        private void OnIdling(object sender, IdlingEventArgs e) =>
            Safely(() =>
            {
                StopWaitingForIdle();
                UIDocument active = (sender as UIApplication)?.ActiveUIDocument;
                Show(active?.Document);
            }, "when Revit was idle");

        private void StopWaitingForIdle()
        {
            if (!_waitingForIdle || _application == null)
                return;
            _application.Idling -= OnIdling;
            _waitingForIdle = false;
        }

        private void ShowAgain(Document document)
        {
            if (IsShown(document))
                Show(document);
        }

        // Shows the model's link; null shows that no project model is active. Another model or another link
        // starts again from "checking", and the pane hears of it.
        private void Show(Document document)
        {
            bool isProject = document != null && document.IsValidObject && !document.IsFamilyDocument;
            Document shown = isProject ? document : null;
            ModelLink link = isProject ? ProjectLinkStorage.Read(document) : null;
            bool sameModel = shown == null ? Shown == null : IsShown(shown);
            bool same = sameModel && SameLink(link, _link);

            _shown = shown;
            _link = link;
            _shownClosingId = null;
            if (!same)
            {
                _condition = LinkCondition.Checking;
                _changes = 0;
            }
            Put(FaceNow());
            if (!same)
                ModelShown?.Invoke(shown, link);
        }

        private DatabaseButtonFace FaceNow() =>
            Shown == null ? DatabaseButtonFace.NoModel : DatabaseButtonFace.For(_link, TimeZoneInfo.Local, _condition, _changes);

        private void Put(DatabaseButtonFace face)
        {
            if (face.Equals(_face))
                return;
            _button.ItemText = face.Text;
            _button.ToolTip = face.ToolTip;
            _button.Image = _icons.Small(face.Dot);
            _button.LargeImage = _icons.Large(face.Dot, face.Changes);
            _face = face;
        }

        private bool IsShown(Document document) =>
            document != null && _shown != null && _shown.IsValidObject && _shown.Equals(document);

        private static bool SameLink(ModelLink a, ModelLink b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            return a.ProjectId == b.ProjectId && a.Number == b.Number && a.SubNumber == b.SubNumber
                && string.Equals(a.Name, b.Name, StringComparison.Ordinal) && a.LinkedAtUtc == b.LinkedAtUtc
                && string.Equals(a.LinkedBy, b.LinkedBy, StringComparison.Ordinal);
        }

        // An event handler must never throw into Revit. When the link can't be read, the button makes no
        // claim: it goes back to plain "Database".
        private void Safely(Action work, string when)
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // Trace, not Debug: release builds keep it, and DebugView shows it.
                Trace.WriteLine("[Parameter Tools] The Database button failed " + when + ": " + ex);
                _shown = null;
                _link = null;
                _shownClosingId = null;
                try
                {
                    Put(DatabaseButtonFace.NoModel);
                    ModelShown?.Invoke(null, null);
                }
                catch (Exception again)
                {
                    Trace.WriteLine("[Parameter Tools] The Database button couldn't be reset: " + again.Message);
                }
            }
        }
    }
}
