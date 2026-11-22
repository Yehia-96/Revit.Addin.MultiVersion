using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using MH.RevitTools.Database.Parameters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Check parameters: the database's parameters set against the model's project parameters
    /// (<see cref="ParameterCheck"/>), Add for the missing ones and Replace for those of the same name
    /// with another GUID. The database and the master file are read in the background, so Revit never
    /// waits on them. Add and Replace call back into the command on Revit's thread: the dialog is modal
    /// inside it, so the model may be changed there.
    /// </summary>
    public partial class CheckParametersWindow : Window
    {
        private const int ProblemsShown = 3;

        private readonly ILinkDatabase _database;
        private readonly string _masterFilePath;
        private readonly string _localCopy;
        private readonly IReadOnlyList<ModelBinding> _model;
        private readonly ParameterActions _actions;

        private IReadOnlyList<LinkParameter> _parameters;
        private MasterParameterFile _masterFile;
        private ParameterCheck _check;
        private ParameterFilter _filter = ParameterFilter.All;
        private bool _started;
        private bool _closed;
        private bool _addDone;
        private bool _busy;

        /// <param name="database">Where the parameters come from; read on a background thread.</param>
        /// <param name="masterFilePath">Studio's master file, as the connection file names it.</param>
        /// <param name="localCopy">Where to copy it, for reading and for Revit.</param>
        /// <param name="model">The model's project parameters when the dialog opens.</param>
        /// <param name="actions">What the dialog asks of Revit: scan, add, replace, confirm, name a category.</param>
        public CheckParametersWindow(
            ILinkDatabase database,
            string masterFilePath,
            string localCopy,
            IReadOnlyList<ModelBinding> model,
            ParameterActions actions)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (actions == null) throw new ArgumentNullException(nameof(actions));

            InitializeComponent();
            _database = database;
            _masterFilePath = masterFilePath;
            _localCopy = localCopy;
            _model = model;
            _actions = actions;

            DialogText.Show(Subtitle, ParameterCheckTexts.Subtitle(null, TimeZoneInfo.Local));
            FooterText.Text = ParameterCheckTexts.Footer;
            OtherGuidHintText.Text = ParameterCheckTexts.OtherGuidHint;
            ShowBanner(new[] { TextPart.Plain("Reading the parameters from the MH database and the master file…") }, false);

            Loaded += OnLoaded;
            Closed += (sender, e) => _closed = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started)
                return;
            _started = true;

            string path = _masterFilePath;
            string copy = _localCopy;
            Task.Run(() => new ReadResult(_database.GetParameters(), MasterParameterFile.CopyAndRead(path, copy)))
                .ContinueWith(
                    read => Dispatcher.BeginInvoke(new Action(() => OnRead(read))),
                    TaskScheduler.Default);
        }

        private void OnRead(Task<ReadResult> read)
        {
            // Look at the error even when the window is gone, so it never goes unobserved.
            Exception error = read.IsFaulted ? read.Exception.InnerException ?? read.Exception : null;
            if (_closed)
                return;
            if (error != null || read.IsCanceled)
            {
                ShowProblem(error ?? new OperationCanceledException("The read was stopped."));
                return;
            }

            // This runs on Revit's thread outside the command's try/catch: a fault here must end up in
            // the dialog, not in Revit.
            try
            {
                _parameters = read.Result.Parameters;
                _masterFile = read.Result.MasterFile;
                ShowCheck(ParameterCheck.Run(_parameters, _masterFile, _model), null);
            }
            catch (Exception ex)
            {
                _check = null;
                ShowProblem(ex);
                UpdateAddButton();
            }
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (_addDone)
            {
                Close();
                return;
            }
            if (_check == null || _check.ToAdd.Count == 0)
                return;

            WhileBusy(() =>
            {
                ParameterAddResult result = Changing(() => _actions.Add(_check.ToAdd));
                // Done once all went in; after a partial or failed Add, the button offers what's still missing.
                _addDone = result.Added.Count > 0 && result.Problems.Count == 0;
                ShowCheck(ParameterCheck.Run(_parameters, _masterFile, _actions.ScanModel(), result.Added), result, "Added");
            }, "Adding failed. ");
        }

        // Replace deletes the model's parameters and their values, so it asks first.
        private void OnReplace(object sender, RoutedEventArgs e)
        {
            if (_check == null || _check.ToReplace.Count == 0)
                return;

            WhileBusy(() =>
            {
                if (!_actions.Confirm(ParameterCheckTexts.ReplaceTitle(_check), ParameterCheckTexts.ReplaceQuestion(_check)))
                    return;
                ParameterAddResult result = Changing(() => _actions.Replace(_check.ToReplace));
                ShowCheck(ParameterCheck.Run(_parameters, _masterFile, _actions.ScanModel(), result.Added), result, "Replaced");
            }, "Replacing failed. ");
        }

        // While Add or Replace runs, or asks its question, nothing else in the dialog can be clicked: Revit's
        // question box doesn't block this window, and a second click would act on a stale check.
        private void WhileBusy(Action work, string failure)
        {
            if (_busy)
                return;
            _busy = true;
            var content = (UIElement)Content;
            content.IsEnabled = false;
            try
            {
                work();
            }
            catch (Exception ex)
            {
                ShowBanner(new[] { TextPart.Strong(failure), TextPart.Plain(ex.Message) }, true);
            }
            finally
            {
                content.IsEnabled = true;
                _busy = false;
                UpdateAddButton();
            }
        }

        private ParameterAddResult Changing(Func<ParameterAddResult> change)
        {
            Cursor = Cursors.Wait;
            try
            {
                return change();
            }
            finally
            {
                Cursor = null;
            }
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            _filter = ReferenceEquals(sender, MissingTab) ? ParameterFilter.Missing
                : ReferenceEquals(sender, OtherGuidTab) ? ParameterFilter.OtherGuid
                : ReferenceEquals(sender, ReadyTab) ? ParameterFilter.Ready
                : ParameterFilter.All;
            if (_check != null)
                ShowRows();
        }

        // Keeps the column headings over their columns while the list shows a scroll bar.
        private void OnRowsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double bar = RowsScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? SystemParameters.VerticalScrollBarWidth
                : 0;
            HeaderColumns.Margin = new Thickness(0, 0, bar, 0);
        }

        /// <param name="result">What Add or Replace just did; null when nothing was done yet.</param>
        /// <param name="done">"Added" or "Replaced", for the banner.</param>
        private void ShowCheck(ParameterCheck check, ParameterAddResult result, string done = null)
        {
            _check = check;
            DialogText.Show(Subtitle, ParameterCheckTexts.Subtitle(check.MasterFile, TimeZoneInfo.Local));
            if (result != null)
                ShowResult(result, done);
            else if (check.FileProblem != null)
                ShowBanner(new[] { TextPart.Strong("Can't add parameters. "), TextPart.Plain(check.FileProblem) }, true);
            else
                Banner.Visibility = Visibility.Collapsed;

            AllTab.Content = ParameterCheckTexts.TabLabel(ParameterFilter.All, check.Count(ParameterFilter.All));
            MissingTab.Content = ParameterCheckTexts.TabLabel(ParameterFilter.Missing, check.Count(ParameterFilter.Missing));
            OtherGuidTab.Content = ParameterCheckTexts.TabLabel(ParameterFilter.OtherGuid, check.Count(ParameterFilter.OtherGuid));
            ReadyTab.Content = ParameterCheckTexts.TabLabel(ParameterFilter.Ready, check.Count(ParameterFilter.Ready));
            Summary.Text = ParameterCheckTexts.Summary(check);
            OtherGuidHint.Visibility = check.Count(ParameterFilter.OtherGuid) > 0 ? Visibility.Visible : Visibility.Collapsed;
            ReplaceButton.Content = ParameterCheckTexts.ReplaceButton(check);
            ReplaceButton.Visibility = check.ToReplace.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowRows();
            UpdateAddButton();
        }

        private void ShowRows()
        {
            IReadOnlyList<ParameterCheckRow> rows = _check.Filter(_filter);
            Rows.ItemsSource = rows.Select(r => new ParameterRowView(r, _actions.CategoryLabel(r.Category))).ToList();
            ListMessage.Text = rows.Count > 0 ? ""
                : _check.Rows.Count == 0 ? "The database has no parameters for Revit."
                : "No parameters here.";
            ListMessage.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowResult(ParameterAddResult result, string done)
        {
            string count = result.Added.Count.ToString(CultureInfo.InvariantCulture)
                + (result.Added.Count == 1 ? " parameter" : " parameters");
            if (result.Problems.Count == 0)
            {
                string keep = result.Added.Count == 1 ? "Save the model to keep it." : "Save the model to keep them.";
                ShowBanner(new[] { TextPart.Strong(done + " " + count + ". "), TextPart.Plain(keep) }, false);
                return;
            }

            string problems = string.Join(" ", result.Problems.Take(ProblemsShown));
            if (result.Problems.Count > ProblemsShown)
                problems += " And " + (result.Problems.Count - ProblemsShown).ToString(CultureInfo.InvariantCulture) + " more.";
            ShowBanner(new[]
            {
                TextPart.Strong(result.Added.Count > 0 ? done + " " + count + ", not all. " : "Nothing was changed. "),
                TextPart.Plain(problems),
            }, true);
        }

        private void ShowProblem(Exception error)
        {
            string problem = error is LinkDatabaseException
                ? error.Message
                : "Something went wrong while reading them: " + error.Message;
            ShowBanner(new[] { TextPart.Strong("Can't read the parameters. "), TextPart.Plain(problem) }, true);
            Rows.ItemsSource = null;
            ListMessage.Text = "Close this window and try again. Test connection shows more about the connection.";
            ListMessage.Visibility = Visibility.Visible;
        }

        private void UpdateAddButton()
        {
            if (_addDone)
            {
                AddButton.Content = "Done";
                AddButton.IsEnabled = true;
                return;
            }
            AddButton.Content = _check == null ? "Nothing to add" : ParameterCheckTexts.AddButton(_check);
            AddButton.IsEnabled = _check != null && _check.ToAdd.Count > 0;
        }

        private void ShowBanner(IEnumerable<TextPart> parts, bool isWarning)
        {
            Banner.Visibility = Visibility.Visible;
            Banner.Background = isWarning ? DialogText.WarningBackground : DialogText.InfoBackground;
            Banner.BorderBrush = isWarning ? DialogText.WarningBorder : DialogText.InfoBorder;
            DialogText.Show(BannerText, parts);
        }

        // What the background read brings back.
        private sealed class ReadResult
        {
            public ReadResult(IReadOnlyList<LinkParameter> parameters, MasterParameterFile masterFile)
            {
                Parameters = parameters;
                MasterFile = masterFile;
            }

            public IReadOnlyList<LinkParameter> Parameters { get; }

            public MasterParameterFile MasterFile { get; }
        }
    }
}
