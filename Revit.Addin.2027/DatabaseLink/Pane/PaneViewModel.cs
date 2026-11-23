using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Linking;
using MH.RevitTools.Database.SigningIn;
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

        /// <summary>Asks for the Studio account.</summary>
        void SignIn();

        /// <summary>Opens the Link dialog: "Link the model…" and "Change project…".</summary>
        void Link();

        void CheckParameters();

        /// <summary>Selects the elements in the active model; with zoom, also shows them in the view.</summary>
        void Select(IReadOnlyList<long> elementIds, bool zoom);

        /// <summary>Writes the groups' values into the model, as the pane lists them.</summary>
        void Apply(IReadOnlyList<ChangeGroup> groups);

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
        private bool _notAppliedOpen = true;

        // What the lists were made from. A check every minute mustn't rebuild them, or they'd flicker and lose
        // the keyboard's place.
        private ChangeReport _listedReport;
        private bool _listedCanApply;

        public PaneViewModel(IPaneActions actions)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            CheckNowCommand = new RelayCommand(() => _actions.CheckNow());
            SignInCommand = new RelayCommand(() => _actions.SignIn());
            LinkCommand = new RelayCommand(() => _actions.Link());
            CheckParametersCommand = new RelayCommand(() => _actions.CheckParameters());
            SelectLinkedCommand = new RelayCommand(() => SelectCounted(counts => counts.Linked));
            SelectWithoutIdCommand = new RelayCommand(() => SelectCounted(counts => counts.WithoutId));
            SelectNotInProjectCommand = new RelayCommand(() => SelectCounted(counts => counts.IdNotInProject));
            ApplyAllCommand = new RelayCommand(ApplyAll);
            ToggleNotAppliedCommand = new RelayCommand(ToggleNotApplied);
            Groups = new ChangeGroupView[0];
            NotAppliedItems = new NotAppliedView[0];
            Show(new PaneSnapshot(false, null, null, null, null, null, null, false, null, DateTime.MinValue, null, DateTime.UtcNow, TimeZoneInfo.Local));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // ---- Header ----
        public string DotColor { get; private set; }
        public string Title { get; private set; }
        public string Where { get; private set; }
        public string When { get; private set; }
        public bool CanCheckNow { get; private set; }
        public bool ShowSignedIn { get; private set; }
        public string SignedInText { get; private set; }

        // ---- Cards ----
        public bool ShowSignIn { get; private set; }
        public string SignInCardTitle => SignInTexts.CardTitle;
        public string SignInCardText => SignInTexts.CardText;
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
        public string UpToDateTitle { get; private set; }
        public string UpToDateText { get; private set; }
        public bool ShowChanges { get; private set; }
        public string ChangesTitle { get; private set; }
        public IReadOnlyList<ChangeGroupView> Groups { get; private set; }
        public bool ShowMore { get; private set; }
        public string MoreText { get; private set; }

        // ---- Apply ----
        public bool CanApply { get; private set; }
        public bool ShowApplyWaits { get; private set; }
        public string ApplyWaitsText { get; private set; }
        public bool ShowApplied { get; private set; }
        public bool AppliedSomething { get; private set; }
        public bool AppliedNothing => ShowApplied && !AppliedSomething;
        public string AppliedTitle { get; private set; }
        public string AppliedDetail { get; private set; }
        public string AppliedBack => AppliedSomething ? "#E7F5EC" : "#FDF3E1";
        public string AppliedLine => AppliedSomething ? "#BFE3CC" : "#F0D6A8";
        public string AppliedText => AppliedSomething ? "#145C33" : "#8F5B0A";

        // ---- Not applied ----
        public bool ShowNotApplied { get; private set; }
        public string NotAppliedTitle { get; private set; }
        public bool NotAppliedOpen => _notAppliedOpen;
        public double NotAppliedTurn => _notAppliedOpen ? 180 : 0;
        public IReadOnlyList<NotAppliedView> NotAppliedItems { get; private set; }

        // ---- Notes ----
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
        public ICommand SignInCommand { get; }
        public ICommand LinkCommand { get; }
        public ICommand CheckParametersCommand { get; }
        public ICommand SelectLinkedCommand { get; }
        public ICommand SelectWithoutIdCommand { get; }
        public ICommand SelectNotInProjectCommand { get; }
        public ICommand ApplyAllCommand { get; }
        public ICommand ToggleNotAppliedCommand { get; }

        /// <summary>The snapshot shown now.</summary>
        public PaneSnapshot Snapshot => _snapshot;

        public void Show(PaneSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _selectedInterval = CheckInterval.All.FirstOrDefault(choice => choice.Seconds == snapshot.Interval.Seconds);
            Clear();
            ShowSignedIn = snapshot.SignedInAs != null;
            SignedInText = ShowSignedIn ? SignInTexts.SignedIn(snapshot.SignedInAs) : "";

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
            CanCheckNow = ShowNoModel = ShowNoFile = ShowSignIn = ShowSetup = ShowModel = ShowCounts = false;
            ShowBanner = BannerCanRetry = BannerOffersLink = ShowComparing = ShowUpToDate = ShowChanges = ShowMore = false;
            ShowLastPull = ShowFooter = false;
            CanApply = ShowApplyWaits = ShowApplied = AppliedSomething = ShowNotApplied = false;
            Where = When = NoFilePath = NoFileProblem = ProjectCode = ProjectName = LinkedLine = "";
            LinkedCount = LinkedLabel = WithoutIdCount = NotInProjectCount = "";
            BannerTitle = BannerText = UpToDateTitle = UpToDateText = ChangesTitle = MoreText = LastPullText = "";
            ApplyWaitsText = AppliedTitle = AppliedDetail = NotAppliedTitle = "";
            WithoutIdColor = NotInProjectColor = "#0F1B2D";
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
                case CheckState.NotSignedIn:
                    DotColor = Grey;
                    Title = "Not signed in";
                    Where = outcome.Where;
                    When = PaneTexts.NotSignedIn(outcome.DataReadAtUtc, snapshot.NowUtc, snapshot.TimeZone);
                    ShowSignIn = true;
                    CanCheckNow = false;
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
            if (outcome.State == CheckState.NotSignedIn)
            {
                // What the model is, by the last pull; nothing about changes until someone signs in.
                if (snapshot.Report != null)
                    ShowCountsOf(snapshot.Report);
                return;
            }

            ShowAppliedBanner(snapshot);
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

            string waits = ApplyTexts.ApplyWaits(snapshot.Outcome.State, snapshot.IsReadOnly);
            CanApply = waits == null;
            bool relist = !ReferenceEquals(report, _listedReport) || CanApply != _listedCanApply;
            _listedReport = report;
            _listedCanApply = CanApply;

            ShowCountsOf(report);
            if (report.IsUpToDate)
            {
                ShowUpToDate = true;
                UpToDateTitle = ApplyTexts.UpToDateTitle(report);
                UpToDateText = report.HasBlockedDifferences
                    ? ApplyTexts.BlockedDetail
                    : PaneTexts.UpToDate(report, snapshot.NowUtc, snapshot.TimeZone);
            }
            else
            {
                ShowChanges = true;
                ChangesTitle = PaneTexts.ChangesTitle(report.ChangeCount).ToUpperInvariant();
                ShowApplyWaits = !CanApply;
                ApplyWaitsText = waits ?? "";
                if (relist)
                    Groups = GroupsUpTo(report, snapshot, CanApply);
                int more = report.ChangeCount - Groups.Sum(group => group.Rows.Count);
                ShowMore = more > 0;
                MoreText = ShowMore
                    ? "… and " + PaneTexts.Count(more, "more change", "more changes") + ", not listed here. Apply all applies them too."
                    : "";
            }

            ShowNotApplied = report.NotApplied.Count > 0;
            NotAppliedTitle = ApplyTexts.NotAppliedTitle(report.NotApplied.Count).ToUpperInvariant();
            if (relist)
                NotAppliedItems = report.NotApplied.Select(item => new NotAppliedView(item, CheckParametersCommand)).ToList();
        }

        private void ShowCountsOf(ChangeReport report)
        {
            ShowCounts = true;
            LinkedCount = Number(report.Counts.Linked.Count);
            LinkedLabel = PaneTexts.LinkedLabel(report.Counts.Linked.Count);
            WithoutIdCount = Number(report.Counts.WithoutId.Count);
            NotInProjectCount = Number(report.Counts.IdNotInProject.Count);
            WithoutIdColor = report.Counts.WithoutId.Count > 0 ? "#8F5B0A" : "#0F1B2D";
            NotInProjectColor = report.Counts.IdNotInProject.Count > 0 ? "#8F5B0A" : "#0F1B2D";
        }

        // Whole cards only: a card's Apply writes all it has, so it must also show all of it.
        private IReadOnlyList<ChangeGroupView> GroupsUpTo(ChangeReport report, PaneSnapshot snapshot, bool canApply)
        {
            var groups = new List<ChangeGroupView>();
            int listed = 0;
            foreach (ChangeGroup group in report.Groups)
            {
                if (listed >= MaxRows)
                    break;
                listed += group.Rows.Count;
                groups.Add(new ChangeGroupView(group, snapshot, canApply, _actions));
            }
            return groups;
        }

        private void ShowAppliedBanner(PaneSnapshot snapshot)
        {
            if (snapshot.Applied == null)
                return;
            ShowApplied = true;
            AppliedSomething = snapshot.Applied.Changes > 0;
            AppliedTitle = ApplyTexts.AppliedTitle(snapshot.Applied);
            AppliedDetail = ApplyTexts.AppliedDetail(snapshot.Applied, snapshot.AppliedAtUtc, snapshot.NowUtc, snapshot.TimeZone);
        }

        private void ApplyAll()
        {
            ChangeReport report = _snapshot == null ? null : _snapshot.Report;
            if (report != null && CanApply && report.Groups.Count > 0)
                _actions.Apply(report.Groups);
        }

        private void ToggleNotApplied()
        {
            _notAppliedOpen = !_notAppliedOpen;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
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
        public ChangeGroupView(ChangeGroup group, PaneSnapshot snapshot, bool canApply, IPaneActions actions)
        {
            Key = group.Key;
            Name = group.Name;
            Size = PaneTexts.GroupSize(group);
            Rows = group.Rows.Select(row => new ChangeRowView(row, snapshot)).ToList();
            // A material isn't something a view can show; elements are.
            CanShow = !group.IsMaterial && group.ElementIds.Count > 0;
            IReadOnlyList<long> ids = group.ElementIds;
            ShowCommand = new RelayCommand(() => actions.Select(ids, true));
            CanApply = canApply;
            ApplyLabel = ApplyTexts.ApplyLabel(group.Rows.Count);
            ApplyCommand = new RelayCommand(() => actions.Apply(new[] { group }));
        }

        public string Key { get; }
        public string Name { get; }
        public string Size { get; }
        public IReadOnlyList<ChangeRowView> Rows { get; }
        public bool CanShow { get; }
        public ICommand ShowCommand { get; }
        public bool CanApply { get; }
        public string ApplyLabel { get; }
        public ICommand ApplyCommand { get; }
    }

    /// <summary>One entry of the pane's "Not applied" list.</summary>
    public sealed class NotAppliedView
    {
        private const string Lock = "M7,11 H17 A2,2 0 0 1 19,13 V19 A2,2 0 0 1 17,21 H7 A2,2 0 0 1 5,19 V13 A2,2 0 0 1 7,11 Z M8,11 V8 A4,4 0 0 1 16,8 V11";
        private const string Pencil = "M4,20 H8 L19,9 L15,5 L4,16 Z";
        private const string Warning = "M12,3 L2,20 H22 Z M12,10 V14 M12,17.5 V17.51";

        public NotAppliedView(NotAppliedItem item, ICommand checkParameters)
        {
            Title = ApplyTexts.Title(item);
            Detail = ApplyTexts.Detail(item);
            OffersCheckParameters = item.Reason == NotAppliedReason.MissingParameter;
            CheckParametersCommand = checkParameters;
            switch (item.Reason)
            {
                case NotAppliedReason.Borrowed:
                case NotAppliedReason.OutOfDate:
                case NotAppliedReason.ReadOnly:
                    Icon = Lock;
                    IconColor = "#B7791F";
                    break;
                case NotAppliedReason.Placeholder:
                case NotAppliedReason.MaterialReference:
                    Icon = Pencil;
                    IconColor = "#5B6578";
                    break;
                default:
                    Icon = Warning;
                    IconColor = "#B7791F";
                    break;
            }
        }

        public string Title { get; }
        public string Detail { get; }
        public string Icon { get; }
        public string IconColor { get; }
        public bool OffersCheckParameters { get; }
        public ICommand CheckParametersCommand { get; }
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
