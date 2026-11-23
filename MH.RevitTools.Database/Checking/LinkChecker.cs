using System;
using System.Collections.Generic;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// The pane's change check, one project at a time. The first check of a project in each Revit session
    /// reads everything, whatever the stamp says: a Studio migration can change data without moving it.
    /// After that, each check reads only the cheap change stamp, and everything again only when the stamp
    /// has moved. Every full read is kept on the PC (<see cref="PullCache"/>), so a later session that can't
    /// reach the database still has the values and knows when they were read. It reads the connection file
    /// each time, so a file put right, or taken away, counts at the next check. It only ever reads the
    /// database, and only while someone is signed in to it. Not thread-safe: one check at a time, which the
    /// pane's own check thread guarantees.
    /// </summary>
    public sealed class LinkChecker
    {
        private readonly Func<ConnectionFileResult> _readConnectionFile;
        private readonly Func<ConnectionSettings, ILinkDatabase> _openDatabase;
        private readonly Func<ConnectionSettings, bool> _isSignedIn;
        private readonly string _cacheFolder;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<int, ProjectCheckState> _projects = new Dictionary<int, ProjectCheckState>();
        private string _databaseKey;

        // One counter for every project and database, so a data version never comes back: the pane compares
        // again whenever the version it compared is no longer the one shown.
        private long _lastVersion;

        /// <param name="readConnectionFile">Reads the PC's connection file, e.g. <c>() =&gt; ConnectionFile.Read(ConnectionFile.DefaultPath)</c>.</param>
        /// <param name="openDatabase">The database for those settings, e.g. <c>s =&gt; new SqlLinkDatabase(s)</c>.</param>
        /// <param name="isSignedIn">Whether someone is signed in to that database; asked at every check.</param>
        /// <param name="cacheFolder">Where the last pulls are kept; null keeps none.</param>
        /// <param name="utcNow">The clock.</param>
        public LinkChecker(
            Func<ConnectionFileResult> readConnectionFile,
            Func<ConnectionSettings, ILinkDatabase> openDatabase,
            Func<ConnectionSettings, bool> isSignedIn,
            string cacheFolder,
            Func<DateTime> utcNow)
        {
            _readConnectionFile = readConnectionFile ?? throw new ArgumentNullException(nameof(readConnectionFile));
            _openDatabase = openDatabase ?? throw new ArgumentNullException(nameof(openDatabase));
            _isSignedIn = isSignedIn ?? throw new ArgumentNullException(nameof(isSignedIn));
            _cacheFolder = string.IsNullOrWhiteSpace(cacheFolder) ? null : cacheFolder;
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <summary>
        /// Checks the project the model is linked to. A database that can't be read gives
        /// <see cref="CheckState.NotReachable"/>; other exceptions are faults and are thrown.
        /// </summary>
        public CheckOutcome Check(ModelLink link)
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));

            DateTime now = _utcNow();
            ConnectionFileResult file = _readConnectionFile();
            if (file == null || !file.IsLoaded)
                return CheckOutcome.NotSetUp(link.ProjectId, file == null ? "There's no connection file." : file.Problem, now);

            // Another database (another connection file): what was read from the first one doesn't count.
            string databaseKey = PullCache.KeyFor(file.Settings);
            if (!string.Equals(databaseKey, _databaseKey, StringComparison.Ordinal))
            {
                _projects.Clear();
                _databaseKey = databaseKey;
            }

            ProjectCheckState project = StateFor(link.ProjectId);
            PullCache cache = _cacheFolder == null ? null : new PullCache(_cacheFolder, file.Settings);
            string where = DatabaseLabel.For(file.Settings);
            if (!_isSignedIn(file.Settings))
            {
                ShowLastPull(link, project, cache);
                return CheckOutcome.NotSignedIn(link.ProjectId, where, now, project);
            }

            try
            {
                ILinkDatabase database = _openDatabase(file.Settings);
                if (project.ReadThisSession && project.Data != null)
                {
                    long? stamp = database.GetStamp(link.ProjectId);
                    if (stamp == null)
                        return Missing(link, where, now, project, cache);
                    if (stamp.Value == project.Data.Stamp)
                    {
                        project.LastAnswerUtc = now;
                        return CheckOutcome.Answered(link.ProjectId, where, now, project, null);
                    }
                }
                return ReadEverything(database, link, where, now, project, cache);
            }
            catch (LinkDatabaseException ex)
            {
                ShowLastPull(link, project, cache);
                return CheckOutcome.NotReachable(link.ProjectId, where, ex.Message, now, project);
            }
        }

        // Nothing read yet this session: the pull kept from an earlier one still shows the values, if it was kept
        // for this same project.
        private void ShowLastPull(ModelLink link, ProjectCheckState project, PullCache cache)
        {
            if (project.Data != null || cache == null)
                return;
            CachedPull kept = cache.Load(link);
            if (kept != null)
                project.Take(kept.Data, kept.ReadAtUtc, true, ++_lastVersion);
        }

        private CheckOutcome ReadEverything(
            ILinkDatabase database, ModelLink link, string where, DateTime now, ProjectCheckState project, PullCache cache)
        {
            // The saved id must still mean the model's project; another database or a renumbering would
            // otherwise show another project's values as this model's.
            ModelLinkCheck stillThere = link.Check(database.GetProjects());
            project.LastAnswerUtc = now;
            if (stillThere.Status == ModelLinkStatus.ProjectGone)
                return Missing(link, where, now, project, cache);
            if (stillThere.Status == ModelLinkStatus.ProjectChanged)
            {
                string removeProblem = Forget(link, project, cache);
                return CheckOutcome.ProjectChanged(link.ProjectId, where,
                    "In this database the model's project id means " + ProjectCode.Describe(stillThere.Project.Number,
                        stillThere.Project.SubNumber, stillThere.Project.Name) + ", not " + link.Describe() + ".",
                    now, project, stillThere.Project, removeProblem);
            }

            LinkProjectData data = database.GetProjectData(link.ProjectId);
            if (data == null)
                return Missing(link, where, now, project, cache);

            project.Take(data, now, false, ++_lastVersion);
            project.ReadThisSession = true;
            string cacheProblem = null;
            if (cache != null)
                cache.TrySave(data, link, now, out cacheProblem);
            return CheckOutcome.Answered(link.ProjectId, where, now, project, cacheProblem);
        }

        private CheckOutcome Missing(ModelLink link, string where, DateTime now, ProjectCheckState project, PullCache cache)
        {
            project.LastAnswerUtc = now;
            string removeProblem = Forget(link, project, cache);
            return CheckOutcome.ProjectMissing(link.ProjectId, where,
                "This database has no project " + link.Describe() + " (id " + link.ProjectId + "). It was deleted, or the "
                + "model was linked in another database.", now, project, removeProblem);
        }

        // The project's values no longer belong to the model: none are shown, and the kept pull goes, so it
        // can't come back while the database is out of reach.
        private string Forget(ModelLink link, ProjectCheckState project, PullCache cache)
        {
            project.Forget(++_lastVersion);
            project.ReadThisSession = false;
            string problem = null;
            if (cache != null)
                cache.TryRemove(link.ProjectId, out problem);
            return problem;
        }

        private ProjectCheckState StateFor(int projectId)
        {
            ProjectCheckState state;
            if (!_projects.TryGetValue(projectId, out state))
            {
                state = new ProjectCheckState();
                _projects.Add(projectId, state);
            }
            return state;
        }
    }
}
