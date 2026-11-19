using System;
using System.Collections.Generic;
using System.Linq;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// An MH database in memory, for tests of everything built on <see cref="ILinkDatabase"/>.
    /// <see cref="Update"/> plays someone changing the project in Studio, so its stamp moves on.
    /// Safe to use from several threads, like the real one.
    /// </summary>
    public sealed class FakeLinkDatabase : ILinkDatabase
    {
        private readonly object _gate = new object();
        private readonly IReadOnlyList<LinkProject> _projects;
        private readonly Dictionary<int, LinkProjectData> _data = new Dictionary<int, LinkProjectData>();

        public FakeLinkDatabase(IEnumerable<LinkProject> projects, IEnumerable<LinkProjectData> projectData)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));
            if (projectData == null)
                throw new ArgumentNullException(nameof(projectData));

            _projects = projects.ToList().AsReadOnly();
            foreach (LinkProjectData data in projectData)
            {
                RequireListed(data.ProjectId, nameof(projectData));
                _data[data.ProjectId] = data;
            }
        }

        public IReadOnlyList<LinkProject> GetProjects() => _projects;

        /// <summary>The project's stamp; null for an unknown project, and for a listed one without data.</summary>
        public long? GetStamp(int projectId) => GetProjectData(projectId)?.Stamp;

        /// <summary>The project's data; null for an unknown project, and for a listed one without data.</summary>
        public LinkProjectData GetProjectData(int projectId)
        {
            lock (_gate)
            {
                LinkProjectData data;
                return _data.TryGetValue(projectId, out data) ? data : null;
            }
        }

        /// <summary>Replaces a project's data as if it had changed in the database: its stamp moves on by one.</summary>
        public void Update(LinkProjectData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            RequireListed(data.ProjectId, nameof(data));

            lock (_gate)
            {
                LinkProjectData current;
                long stamp = _data.TryGetValue(data.ProjectId, out current) ? current.Stamp + 1 : data.Stamp;
                _data[data.ProjectId] = data.WithStamp(stamp);
            }
        }

        private void RequireListed(int projectId, string parameterName)
        {
            if (!_projects.Any(p => p.ProjectId == projectId))
                throw new ArgumentException("Project " + projectId + " isn't in the project list.", parameterName);
        }
    }
}
