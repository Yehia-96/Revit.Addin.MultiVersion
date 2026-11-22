using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Linking;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>What the pane's buttons ask for. The pane's controller does it, in Revit where needed.</summary>
    public interface IPaneActions
    {
        void CheckNow();

        /// <summary>Opens the Link dialog: "Link the model…" and "Change project…".</summary>
        void Link();

        void CheckParameters();

        /// <summary>Selects the elements in the active model; with zoom, also shows them in the view.</summary>
        void Select(IReadOnlyList<long> elementIds, bool zoom);

        void ChooseInterval(CheckInterval interval);
    }

    /// <summary>
    /// The MH Database pane's state, in the mockup's words (<see cref="PaneTexts"/>). <see cref="Show"/> takes a
    /// new <see cref="PaneSnapshot"/> and the view reads everything again. Main thread only.
    /// </summary>
    public sealed class PaneViewModel : INotifyPropertyChanged
    {
        // A first look at a model can list every value; the pane shows this many, then says how many more.
        private const int MaxRows = 300;

        private const string Grey = "#8C95A5";
        private const string Green = "#1F8A4C";
        private const string Blue = "#1F6FEB";
        private const string Red = "#C03A2B";

        private readonly IPaneActions _actions;
        private PaneSnapshot _snapshot;
        private CheckInterval _selectedInterval;

        public PaneViewModel(IPaneActions actions)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            CheckNowCommand = new RelayCommand(() => _actions.CheckNow());
            LinkCommand = new RelayCommand(() => _actions.Link());
            CheckParametersCommand = new RelayCommand(() => _actions.CheckParameters());
            SelectLinkedCommand = new RelayCommand(() => SelectCounted(counts => counts.Linked));
            SelectWithoutIdCommand = new RelayCommand(() => SelectCounted(counts => counts.WithoutId));
            SelectNotInProjectCommand = new RelayCommand(() => SelectCounted(counts => counts.IdNotInProject));
            Show(new PaneSnapshot(false, null, null, null, null, null, null, DateTime.UtcNow, TimeZoneInfo.Local));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // ---- Header ----
        public string DotColor { get; private set; }
        public string Title { get; private set; }
        public string Where { get; private set; }
        public string When { get; private set; }
        public bool CanCheckNow { get; private set; }

        // ---- Cards ----
        public bool ShowNoModel { get; private set; }
        public bool ShowNoFile { get; private set; }
        public string NoFilePath { get; private set; }
        public string NoFileProblem { get; private set; }
        public bool ShowSetup { get; private set; }

        // ---- This model ----
        public bool ShowModel { get; private set; }
        public string ProjectCode { get; private set; }
        public string ProjectName { get; private set; }
        public string LinkedLine { get; private set; }
        public bool ShowCounts { get; private set; }
        public string LinkedCount { get; private set; }
        public string LinkedLabel { get; private set; }
        public string WithoutIdCount { get; private set; }
        public string NotInProjectCount { get; private set; }
        public string WithoutIdColor { get; private set; }
        public string NotInProjectColor { get; private set; }
        public string WithoutIdLabel => PaneTexts.WithoutIdLabel;
        public string NotInProjectLabel => PaneTexts.NotInProjectLabel;

        // ---- Banner: not reachable, project gone, or the check's own fault ----
        public bool ShowBanner { get; private set; }
        public string BannerTitle { get; private set; }
        public string BannerText { get; private set; }
        public bool BannerCanRetry { get; private set; }
        public bool BannerOffersLink { get; private set; }

        // ---- Up to date, or the changes ----
        public bool ShowComparing { get; private set; }
        public bool ShowUpToDate { get; private set; }
        public string UpToDateText { get; private set; }
        public bool ShowChanges { get; private set; }
        public string ChangesTitle { get; private set; }
        public IReadOnlyList<ChangeGroupView> Groups { get; private set; }
        public bool ShowMore { get; private set; }
        public string MoreText { get; private set; }

        // ---- Notes ----
        public bool ShowMissing { get; private set; }
        public string MissingText { get; private set; }
        public bool ShowProblems { get; private set; }
        public string ProblemsText { get; private set; }
        public bool ShowLastPull { get; private set; }
        public string LastPullText { get; private set; }

        // ---- Footer ----
        public bool ShowFooter { get; private set; }
        public IReadOnlyList<CheckInterval> Intervals => CheckInterval.All;

        public CheckInterval SelectedInterval
        {
            get => _selectedInterval;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedInterval))
                    return;
                _selectedInterval = value;
                _actions.ChooseInterval(value);
            }
        }

        public ICommand CheckNowCommand { get; }
        public ICommand LinkCommand { get; }
        public ICommand CheckParametersCommand { get; }
        public ICommand SelectLinkedCommand { get; }
        public ICommand SelectWithoutIdCommand { get; }
        public ICommand SelectNotInProjectCommand { get; }

        /// <summary>The snapshot shown now.</summary>
        public PaneSnapshot Snapshot => _snapshot;

        public void Show(PaneSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _selectedInterval = CheckInterval.All.FirstOrDefault(choice => choice.Seconds == snapshot.Interval.Seconds);
            Clear();

            if (!snapshot.HasProjectModel)
                ShowWithoutModel();
            else if (snapshot.Link == null)
                ShowUnlinked();
            else
                ShowLinked(snapshot);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private void Clear()
        {
            CanCheckNow = ShowNoModel = ShowNoFile = ShowSetup = ShowModel = ShowCounts = false;
            ShowBanner = BannerCanRetry = BannerOffersLink = ShowComparing = ShowUpToDate = ShowChanges = ShowMore = false;
            ShowMissing = ShowProblems = ShowLastPull = ShowFooter = false;
            Where = When = NoFilePath = NoFileProblem = ProjectCode = ProjectName = LinkedLine = "";
            LinkedCount = LinkedLabel = WithoutIdCount = NotInProjectCount = "";
            BannerTitle = BannerText = UpToDateText = ChangesTitle = MoreText = MissingText = ProblemsText = LastPullText = "";
            WithoutIdColor = NotInProjectColor = "#0F1B2D";
            Groups = new ChangeGroupView[0];
        }

        private void ShowWithoutModel()
        {
            DotColor = Grey;
            Title = "No project model";
            When = "Open a project model to see its link.";
            ShowNoModel = true;
        }

        private void ShowUnlinked()
        {
            DotColor = Grey;
            Title = "Not linked";
            Where = "MH database";
            When = "This model is not linked yet.";
            ShowSetup = true;
        }

        private void ShowLinked(PaneSnapshot snapshot)
        {
            ModelLink link = snapshot.Link;
            CheckOutcome outcome = snapshot.Outcome;
            ShowFooter = true;
            ShowModel = true;
            ProjectCode = MH.RevitTools.Database.Linking.ProjectCode.Format(link.Number, link.SubNumber);
            ProjectName = link.Name;
            LinkedLine = PaneTexts.LinkedLine(link.Linked(snapshot.TimeZone));

            if (outcome == null)
            {
                DotColor = Grey;
                Title = "Checking…";
                When = "Looking at the database…";
                ShowFault(snapshot);
                return;
            }

            Where = outcome.Where.Length == 0 ? "" : outcome.Where + " · read-only";
            CanCheckNow = true;
            switch (outcome.State)
            {
                case CheckState.NotSetUp:
                    DotColor = Grey;
                    Title = "Not set up";
                    Where = "This computer";
                    When = "No connection file found.";
                    ShowModel = false;
                    ShowNoFile = true;
                    NoFilePath = snapshot.ConnectionFilePath;
                    // A missing file is what the card already says; anything else (can't read it, a mistake
                    // in it) is worth its own line.
                    NoFileProblem = outcome.Problem == null || outcome.Problem.StartsWith("No connection file at", StringComparison.Ordinal)
                        ? ""
                        : outcome.Problem;
                    ShowFooter = false;
                    break;
                case CheckState.NotReachable:
                    DotColor = Red;
                    Title = "Not reachable";
                    When = PaneTexts.NoAnswer(outcome.LastAnswerUtc, snapshot.NowUtc, snapshot.TimeZone, snapshot.Interval);
                    Banner("The database is not reachable", PaneTexts.NotReachableDetail(outcome.Problem, snapshot.Interval), true, false);
                    break;
                case CheckState.ProjectMissing:
                    DotColor = Red;
                    Title = "Project not found";
                    When = PaneTexts.Checked(outcome.CheckedAtUtc, snapshot.NowUtc, snapshot.TimeZone, snapshot.Interval);
                    Banner("The database doesn't have this project", outcome.Problem + " Link the model again to go on.", false, true);
                    break;
                case CheckState.ProjectChanged:
                    DotColor = Red;
                    Title = "Another project";
                    When = PaneTexts.Checked(outcome.CheckedAtUtc, snapshot.NowUtc, snapshot.TimeZone, snapshot.Interval);
                    Banner("The model's project is another one now", outcome.Problem
                        + " Its values aren't shown. Link the model to the right project to go on.", false, true);
                    break;
                default:
                    DotColor = snapshot.ChangeCount > 0 ? Blue : Green;
                    Title = "Connected";
                    When = PaneTexts.Checked(outcome.CheckedAtUtc, snapshot.NowUtc, snapshot.TimeZone, snapshot.Interval);
                    break;
            }

            ShowFault(snapshot);
            if (outcome.Data == null)
                return;

            if (outcome.DataFromCache && outcome.DataReadAtUtc.HasValue)
            {
                ShowLastPull = true;
                LastPullText = PaneTexts.FromLastPull(outcome.DataReadAtUtc.Value, snapshot.NowUtc, snapshot.TimeZone);
            }
            ShowReport(snapshot);
        }

        private void ShowReport(PaneSnapshot snapshot)
        {
            ChangeReport report = snapshot.Report;
            if (report == null)
            {
                ShowComparing = true;
                return;
            }

            ShowCounts = true;
            LinkedCount = Number(report.Counts.Linked.Count);
            LinkedLabel = PaneTexts.LinkedLabel(report.Counts.Linked.Count);
            WithoutIdCount = Number(report.Counts.WithoutId.Count);
            NotInProjectCount = Number(report.Counts.IdNotInProject.Count);
            WithoutIdColor = report.Counts.WithoutId.Count > 0 ? "#8F5B0A" : "#0F1B2D";
            NotInProjectColor = report.Counts.IdNotInProject.Count > 0 ? "#8F5B0A" : "#0F1B2D";

            if (report.IsUpToDate)
            {
                ShowUpToDate = true;
                UpToDateText = PaneTexts.UpToDate(report, snapshot.NowUtc, snapshot.TimeZone);
            }
            else
            {
                ShowChanges = true;
                ChangesTitle = PaneTexts.ChangesTitle(report.ChangeCount).ToUpperInvariant();
                Groups = GroupsUpTo(report, snapshot, out int listed);
                int more = report.ChangeCount - listed;
                ShowMore = more > 0;
                MoreText = ShowMore ? "… and " + PaneTexts.Count(more, "more change", "more changes") + ", not listed here." : "";
            }

            MissingText = PaneTexts.Missing(report.MissingParameters);
            ShowMissing = MissingText.Length > 0;
            ProblemsText = PaneTexts.Problems(report.Problems);
            ShowProblems = ProblemsText.Length > 0;
        }

        private IReadOnlyList<ChangeGroupView> GroupsUpTo(ChangeReport report, PaneSnapshot snapshot, out int listed)
        {
            var groups = new List<ChangeGroupView>();
            listed = 0;
            foreach (ChangeGroup group in report.Groups)
            {
                if (listed >= MaxRows)
                    break;
                List<ChangeRow> rows = group.Rows.Take(MaxRows - listed).ToList();
                listed += rows.Count;
                groups.Add(new ChangeGroupView(group, rows, snapshot, _actions));
            }
            return groups;
        }

        private void Banner(string title, string text, bool canRetry, bool offersLink)
        {
            ShowBanner = true;
            BannerTitle = title;
            BannerText = text;
            BannerCanRetry = canRetry;
            BannerOffersLink = offersLink;
        }

        // A check that failed by the add-in's own fault: said plainly, with Try again.
        private void ShowFault(PaneSnapshot snapshot)
        {
            if (snapshot.Fault == null || ShowBanner)
                return;
            Banner("The check stopped", "Something went wrong in the add-in: " + snapshot.Fault, true, false);
        }

        private void SelectCounted(Func<ModelCounts, IReadOnlyList<long>> which)
        {
            ChangeReport report = _snapshot == null ? null : _snapshot.Report;
            if (report != null)
                _actions.Select(which(report.Counts), false);
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One object's or material's changes in the pane's list.</summary>
    public sealed class ChangeGroupView
    {
        public ChangeGroupView(ChangeGroup group, IEnumerable<ChangeRow> rows, PaneSnapshot snapshot, IPaneActions actions)
        {
            Key = group.Key;
            Name = group.Name;
            Size = PaneTexts.GroupSize(group);
            Rows = rows.Select(row => new ChangeRowView(row, snapshot)).ToList();
            // A material isn't something a view can show; elements are.
            CanShow = !group.IsMaterial && group.ElementIds.Count > 0;
            IReadOnlyList<long> ids = group.ElementIds;
            ShowCommand = new RelayCommand(() => actions.Select(ids, true));
        }

        public string Key { get; }
        public string Name { get; }
        public string Size { get; }
        public IReadOnlyList<ChangeRowView> Rows { get; }
        public bool CanShow { get; }
        public ICommand ShowCommand { get; }
    }

    /// <summary>One value in the pane's list: the parameter, who changed it and when, the model's value and the database's.</summary>
    public sealed class ChangeRowView
    {
        public ChangeRowView(ChangeRow row, PaneSnapshot snapshot)
        {
            Parameter = row.Parameter;
            Who = PaneTexts.Who(row, snapshot.NowUtc, snapshot.TimeZone);
            From = row.From;
            To = row.To;
        }

        public string Parameter { get; }
        public string Who { get; }
        public string From { get; }
        public string To { get; }
    }
}
