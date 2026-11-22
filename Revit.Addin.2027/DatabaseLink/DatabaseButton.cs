using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using MH.RevitTools.Database.Linking;
using System;
using System.Diagnostics;
using System.Windows.Media;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Keeps the ribbon's big Database button showing the active model's link (<see cref="DatabaseButtonFace"/>):
    /// the project's number and the icon with the green dot once it's linked (Yehia, 2026-11-22). It follows
    /// the active view from model to model, and every change of the link: Link project, Undo and Redo, and
    /// what synchronizing with central brings. It reads only the link saved in the model, never the database.
    /// </summary>
    internal sealed class DatabaseButton
    {
        private static DatabaseButton _current;

        private readonly PushButton _button;
        private readonly ImageSource _image;
        private readonly ImageSource _largeImage;
        private readonly ImageSource _linkedImage;
        private readonly ImageSource _linkedLargeImage;
        private readonly ElementFilter _projectInformation = new ElementCategoryFilter(BuiltInCategory.OST_ProjectInformation);

        private UIControlledApplication _application;
        private DatabaseButtonFace _face = DatabaseButtonFace.NoModel;
        private Document _shown;
        private int? _shownClosingId;
        private bool _waitingForIdle;

        /// <param name="button">The big Database button, as added with its plain icons.</param>
        /// <param name="linkedImage">The small icon with the green dot.</param>
        /// <param name="linkedLargeImage">The large icon with the green dot.</param>
        public DatabaseButton(PushButton button, ImageSource linkedImage, ImageSource linkedLargeImage)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
            _image = button.Image;
            _largeImage = button.LargeImage;
            _linkedImage = linkedImage ?? _image;
            _linkedLargeImage = linkedLargeImage ?? _largeImage;
        }

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

        // Shows the model's link; null shows that no project model is active.
        private void Show(Document document)
        {
            bool isProject = document != null && document.IsValidObject && !document.IsFamilyDocument;
            _shown = isProject ? document : null;
            _shownClosingId = null;
            Put(isProject ? DatabaseButtonFace.For(ProjectLinkStorage.Read(document), TimeZoneInfo.Local) : DatabaseButtonFace.NoModel);
        }

        private void Put(DatabaseButtonFace face)
        {
            if (face.Equals(_face))
                return;
            _button.ItemText = face.Text;
            _button.ToolTip = face.ToolTip;
            _button.Image = face.IsLinked ? _linkedImage : _image;
            _button.LargeImage = face.IsLinked ? _linkedLargeImage : _largeImage;
            _face = face;
        }

        private bool IsShown(Document document) =>
            document != null && _shown != null && _shown.IsValidObject && _shown.Equals(document);

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
                _shownClosingId = null;
                try
                {
                    Put(DatabaseButtonFace.NoModel);
                }
                catch (Exception again)
                {
                    Trace.WriteLine("[Parameter Tools] The Database button couldn't be reset: " + again.Message);
                }
            }
        }
    }
}
