using System;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>What a check found, for the pane's header and the Database button's dot.</summary>
    public enum CheckState
    {
        /// <summary>The PC's connection file is missing or broken.</summary>
        NotSetUp,

        /// <summary>The database didn't answer: the VPN is off, the server is down, the login is refused...</summary>
        NotReachable,

        /// <summary>The database has no project with the model's id: it was deleted, or it's another database.</summary>
        ProjectMissing,

        /// <summary>The model's id is another project now (another number), so its values aren't this model's.</summary>
        ProjectChanged,

        /// <summary>The database answered; <see cref="CheckOutcome.Data"/> is the project as it is there.</summary>
        Answered,
    }

    /// <summary>The answer of one <see cref="LinkChecker.Check"/>. Immutable.</summary>
    public sealed class CheckOutcome
    {
        private CheckOutcome(
            CheckState state,
            int projectId,
            string where,
            string problem,
            DateTime checkedAtUtc,
            DateTime? lastAnswerUtc,
            LinkProjectData data,
            DateTime? dataReadAtUtc,
            bool dataFromCache,
            long dataVersion,
            LinkProject otherProject,
            string cacheProblem)
        {
            State = state;
            ProjectId = projectId;
            Where = where ?? "";
            Problem = problem;
            CheckedAtUtc = checkedAtUtc;
            LastAnswerUtc = lastAnswerUtc;
            Data = data;
            DataReadAtUtc = dataReadAtUtc;
            DataFromCache = dataFromCache;
            DataVersion = dataVersion;
            OtherProject = otherProject;
            CacheProblem = cacheProblem;
        }

        public CheckState State { get; }

        /// <summary>The project the check was for: the id saved in the model.</summary>
        public int ProjectId { get; }

        /// <summary>The server and database, e.g. "CADSQL · MH_Database"; empty when not set up.</summary>
        public string Where { get; }

        /// <summary>Why it's not set up or not reachable, or what's wrong with the project, in plain words; null when answered.</summary>
        public string Problem { get; }

        public DateTime CheckedAtUtc { get; }

        /// <summary>When the database last answered for this project: this session, or the kept pull's time.</summary>
        public DateTime? LastAnswerUtc { get; }

        /// <summary>
        /// The project's values: as read now or earlier this session, or the last pull kept on this PC
        /// (<see cref="DataFromCache"/>). Null when there are none, and whenever the project is missing or changed.
        /// </summary>
        public LinkProjectData Data { get; }

        /// <summary>When <see cref="Data"/> was read from the database.</summary>
        public DateTime? DataReadAtUtc { get; }

        /// <summary>True when <see cref="Data"/> is the pull kept from an earlier session, as the database can't be reached.</summary>
        public bool DataFromCache { get; }

        /// <summary>
        /// Goes up whenever <see cref="Data"/> is different data, so the model is compared again only then.
        /// Never repeats within a checker, whichever database or project the data came from.
        /// </summary>
        public long DataVersion { get; }

        /// <summary>With <see cref="CheckState.ProjectChanged"/>: the project the model's id means now.</summary>
        public LinkProject OtherProject { get; }

        /// <summary>
        /// Why the pull couldn't be kept on this PC, or a gone project's pull couldn't be removed; null when
        /// all went well, or there was nothing to keep.
        /// </summary>
        public string CacheProblem { get; }

        internal static CheckOutcome NotSetUp(int projectId, string problem, DateTime nowUtc) =>
            new CheckOutcome(CheckState.NotSetUp, projectId, "", problem, nowUtc, null, null, null, false, 0, null, null);

        internal static CheckOutcome Answered(int projectId, string where, DateTime nowUtc, ProjectCheckState project, string cacheProblem) =>
            new CheckOutcome(CheckState.Answered, projectId, where, null, nowUtc, project.LastAnswerUtc,
                project.Data, project.DataReadAtUtc, project.DataFromCache, project.Version, null, cacheProblem);

        internal static CheckOutcome NotReachable(int projectId, string where, string problem, DateTime nowUtc, ProjectCheckState project) =>
            new CheckOutcome(CheckState.NotReachable, projectId, where, problem, nowUtc,
                project.LastAnswerUtc ?? (project.DataFromCache ? project.DataReadAtUtc : null),
                project.Data, project.DataReadAtUtc, project.DataFromCache, project.Version, null, null);

        internal static CheckOutcome ProjectMissing(int projectId, string where, string problem, DateTime nowUtc,
            ProjectCheckState project, string cacheProblem) =>
            new CheckOutcome(CheckState.ProjectMissing, projectId, where, problem, nowUtc, project.LastAnswerUtc,
                null, null, false, project.Version, null, cacheProblem);

        internal static CheckOutcome ProjectChanged(int projectId, string where, string problem, DateTime nowUtc,
            ProjectCheckState project, LinkProject other, string cacheProblem) =>
            new CheckOutcome(CheckState.ProjectChanged, projectId, where, problem, nowUtc, project.LastAnswerUtc,
                null, null, false, project.Version, other, cacheProblem);
    }

    /// <summary>
    /// What <see cref="LinkChecker"/> keeps between checks for one project. Its versions come from the
    /// checker's one counter, so a version never comes back, not even for another database's data.
    /// </summary>
    internal sealed class ProjectCheckState
    {
        public LinkProjectData Data;
        public DateTime? DataReadAtUtc;
        public bool DataFromCache;
        public bool ReadThisSession;
        public DateTime? LastAnswerUtc;
        public long Version;

        public void Take(LinkProjectData data, DateTime readAtUtc, bool fromCache, long version)
        {
            Data = data;
            DataReadAtUtc = readAtUtc;
            DataFromCache = fromCache;
            Version = version;
        }

        public void Forget(long version)
        {
            if (Data == null && !DataFromCache)
                return;
            Data = null;
            DataReadAtUtc = null;
            DataFromCache = false;
            Version = version;
        }
    }
}
