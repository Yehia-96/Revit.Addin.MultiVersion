using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Linking;
using System;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Everything the MH Database pane shows at one moment: the active model, its link, the last check and
    /// the last comparison. Made on Revit's main thread; the pane's view model only turns it into words.
    /// Immutable.
    /// </summary>
    public sealed class PaneSnapshot
    {
        public PaneSnapshot(
            bool hasProjectModel,
            ModelLink link,
            string connectionFilePath,
            string signedInAs,
            CheckOutcome outcome,
            string fault,
            ChangeReport report,
            bool isReadOnly,
            ApplyResult applied,
            DateTime appliedAtUtc,
            CheckInterval interval,
            DateTime nowUtc,
            TimeZoneInfo timeZone)
        {
            HasProjectModel = hasProjectModel;
            Link = hasProjectModel ? link : null;
            ConnectionFilePath = connectionFilePath ?? "";
            Outcome = Link == null ? null : outcome;
            // Signed in to another database than the connection file names now is not signed in.
            bool isSignedOut = Outcome != null && (Outcome.State == CheckState.NotSignedIn || Outcome.State == CheckState.NotSetUp);
            SignedInAs = isSignedOut || string.IsNullOrWhiteSpace(signedInAs) ? null : signedInAs;
            Fault = string.IsNullOrWhiteSpace(fault) ? null : fault;
            Report = Outcome == null || Outcome.Data == null ? null : report;
            IsReadOnly = isReadOnly;
            Applied = Link == null ? null : applied;
            AppliedAtUtc = appliedAtUtc;
            Interval = interval ?? CheckInterval.For(CheckSettings.DefaultSeconds);
            NowUtc = nowUtc;
            TimeZone = timeZone ?? TimeZoneInfo.Local;
        }

        /// <summary>False with no model open, or the family editor active.</summary>
        public bool HasProjectModel { get; }

        /// <summary>The project the active model is linked to; null when it isn't.</summary>
        public ModelLink Link { get; }

        /// <summary>Where the connection file is expected, for the "not set up" card.</summary>
        public string ConnectionFilePath { get; }

        /// <summary>The name of who is signed in; null when nobody is.</summary>
        public string SignedInAs { get; }

        /// <summary>The last check for the model's project; null before the first one.</summary>
        public CheckOutcome Outcome { get; }

        /// <summary>When the last check failed in a way it shouldn't: the add-in's fault, not the database's.</summary>
        public string Fault { get; }

        /// <summary>The model compared with <see cref="CheckOutcome.Data"/>; null until it has been.</summary>
        public ChangeReport Report { get; }

        /// <summary>The model was opened read-only, so nothing can be applied.</summary>
        public bool IsReadOnly { get; }

        /// <summary>What the last Apply on this model did; null when there was none, or it was undone.</summary>
        public ApplyResult Applied { get; }

        public DateTime AppliedAtUtc { get; }

        public CheckInterval Interval { get; }

        public DateTime NowUtc { get; }

        public TimeZoneInfo TimeZone { get; }

        /// <summary>What the Database button's dot says for this moment.</summary>
        public LinkCondition Condition
        {
            get
            {
                if (Outcome == null)
                    return LinkCondition.Checking;
                switch (Outcome.State)
                {
                    case CheckState.NotSetUp:
                        return LinkCondition.NotSetUp;
                    case CheckState.NotSignedIn:
                        return LinkCondition.NotSignedIn;
                    case CheckState.NotReachable:
                        return LinkCondition.NotReachable;
                    case CheckState.ProjectMissing:
                    case CheckState.ProjectChanged:
                        return LinkCondition.ProjectProblem;
                    default:
                        if (Report == null)
                            return LinkCondition.Checking;
                        return Report.IsUpToDate ? LinkCondition.UpToDate : LinkCondition.Changes;
                }
            }
        }

        /// <summary>How many changes wait; 0 until the model has been compared.</summary>
        public int ChangeCount => Report == null ? 0 : Report.ChangeCount;
    }
}
