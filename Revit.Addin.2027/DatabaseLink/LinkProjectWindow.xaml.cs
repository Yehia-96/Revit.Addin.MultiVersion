using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// The Link dialog: which MH database project this model is. It reads the projects in the
    /// background, so Revit never waits on the database, and shows what <see cref="ProjectPicker"/>
    /// decides. It never touches the model: the command saves the pick (<see cref="ChosenProject"/>).
    /// </summary>
    public partial class LinkProjectWindow : Window
    {
        private static readonly Brush InfoBackground = Frozen(0xF5, 0xF9, 0xFF);
        private static readonly Brush InfoBorder = Frozen(0xA9, 0xC5, 0xF7);
        private static readonly Brush InfoIcon = Frozen(0x1F, 0x6F, 0xEB);
        private static readonly Brush WarningBackground = Frozen(0xFD, 0xF3, 0xE1);
        private static readonly Brush WarningBorder = Frozen(0xEB, 0xCB, 0x8B);
        private static readonly Brush WarningText = Frozen(0x8F, 0x5B, 0x0A);
        private static readonly Brush MutedText = Frozen(0x5B, 0x65, 0x78);
        private static readonly Brush CodeText = Frozen(0x2A, 0x34, 0x47);

        private readonly string _modelName;
        private readonly string _projectNumber;
        private readonly ModelLink _currentLink;
        private readonly ILinkDatabase _database;

        private ProjectPicker _picker;
        private LinkProject _pick;
        private bool _started;
        private bool _refreshingList;
        private bool _closed;

        /// <param name="modelName">The model's file name, shown in the subtitle.</param>
        /// <param name="projectNumber">Project Information's Project Number, for the suggestion.</param>
        /// <param name="currentLink">The link saved in the model; null when it has none.</param>
        /// <param name="database">Where the projects come from; read on a background thread.</param>
        public LinkProjectWindow(string modelName, string projectNumber, ModelLink currentLink, ILinkDatabase database)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            InitializeComponent();
            _modelName = modelName;
            _projectNumber = projectNumber;
            _currentLink = currentLink;
            _database = database;

            ShowText(Subtitle, ProjectPicker.SubtitleBeforeCheck(modelName, currentLink));
            ShowBanner(new[] { TextPart.Plain("Reading the projects from the MH database…") }, false);
            UpdateLinkButton();

            Loaded += OnLoaded;
            Closed += (sender, e) => _closed = true;
        }

        /// <summary>The project to link to; set only when the dialog closes with Link.</summary>
        public LinkProject ChosenProject { get; private set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started)
                return;
            _started = true;

            Search.Focus();
            Task.Run(() => _database.GetProjects())
                .ContinueWith(
                    read => Dispatcher.BeginInvoke(new Action(() => OnProjectsRead(read))),
                    TaskScheduler.Default);
        }

        private void OnProjectsRead(Task<IReadOnlyList<LinkProject>> read)
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
                ShowProjects(read.Result);
            }
            catch (Exception ex)
            {
                _picker = null;
                ShowProblem(ex);
                UpdateLinkButton();
            }
        }

        private void ShowProjects(IReadOnlyList<LinkProject> projects)
        {
            _picker = new ProjectPicker(_modelName, _projectNumber, _currentLink, projects, TimeZoneInfo.Local);
            ShowText(Subtitle, _picker.Subtitle);
            Subtitle.Foreground = _picker.SubtitleIsWarning ? WarningText : MutedText;
            ShowBanner(_picker.Banner, _picker.BannerIsWarning);
            _pick = _picker.InitialPick;
            RefreshList();
            UpdateLinkButton();
        }

        private void ShowProblem(Exception error)
        {
            string problem = error is LinkDatabaseException
                ? error.Message
                : "Something went wrong while reading them: " + error.Message;
            ShowBanner(new[] { TextPart.Strong("Can't read the projects. "), TextPart.Plain(problem) }, true);
            Projects.ItemsSource = null;
            ListMessage.Text = "Close this window and try again. Test connection shows more about the connection.";
            ListMessage.Visibility = Visibility.Visible;
        }

        private void RefreshList()
        {
            if (_picker == null)
                return;

            string query = Search.Text.Trim();
            IReadOnlyList<ProjectRow> rows = _picker.Rows(query);
            _refreshingList = true;
            try
            {
                Projects.ItemsSource = rows;
                ProjectRow picked = _pick == null ? null : rows.FirstOrDefault(r => r.Project.ProjectId == _pick.ProjectId);
                Projects.SelectedItem = picked;
                if (picked != null)
                    Projects.ScrollIntoView(picked);
            }
            finally
            {
                _refreshingList = false;
            }

            ListMessage.Text = rows.Count > 0 ? ""
                : query.Length > 0 ? "No project matches “" + query + "”."
                : "The database has no projects yet.";
            ListMessage.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateLinkButton()
        {
            LinkButton.Content = _picker == null ? "Pick a project" : _picker.LinkButtonText(_pick);
            LinkButton.IsEnabled = _picker != null && _picker.CanLink(_pick);
        }

        private void OnProjectSelected(object sender, SelectionChangedEventArgs e)
        {
            // A search that hides the picked project clears the selection; the pick stays.
            var row = Projects.SelectedItem as ProjectRow;
            if (_refreshingList || row == null)
                return;
            _pick = row.Project;
            UpdateLinkButton();
        }

        private void OnProjectDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(Projects, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null && item.DataContext is ProjectRow)
                OnLink(sender, e);
        }

        private void OnLink(object sender, RoutedEventArgs e)
        {
            if (_picker == null || !_picker.CanLink(_pick))
                return;
            ChosenProject = _pick;
            DialogResult = true;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshList();
        }

        // Down arrow from the search box goes to the list, as in Windows' own pickers.
        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down || Projects.Items.Count == 0)
                return;
            if (Projects.SelectedItem == null)
                Projects.SelectedIndex = 0;
            var item = Projects.ItemContainerGenerator.ContainerFromItem(Projects.SelectedItem) as ListBoxItem;
            if (item != null)
            {
                item.Focus();
                e.Handled = true;
            }
        }

        private void ShowBanner(IEnumerable<TextPart> parts, bool isWarning)
        {
            List<TextPart> text = parts.ToList();
            Banner.Visibility = text.Any(p => p.Text.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
            Banner.Background = isWarning ? WarningBackground : InfoBackground;
            Banner.BorderBrush = isWarning ? WarningBorder : InfoBorder;
            BannerIcon.Stroke = isWarning ? WarningText : InfoIcon;
            ShowText(BannerText, text);
        }

        private void ShowText(TextBlock target, IEnumerable<TextPart> parts)
        {
            target.Inlines.Clear();
            foreach (TextPart part in parts)
            {
                var run = new Run(part.Text);
                if (part.Style == TextStyle.Strong)
                {
                    run.FontWeight = FontWeights.SemiBold;
                }
                else if (part.Style == TextStyle.Code)
                {
                    run.FontFamily = (FontFamily)FindResource("CodeFont");
                    run.FontSize = 12;
                    run.Foreground = CodeText;
                }
                target.Inlines.Add(run);
            }
        }

        private static Brush Frozen(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
