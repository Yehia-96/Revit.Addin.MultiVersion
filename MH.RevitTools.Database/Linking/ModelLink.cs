using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// The project a model is linked to, as saved in the model. It keeps the project's number, sub-number
    /// and name next to the database's id: the model can say what it's linked to without the database,
    /// and the add-in can tell when the id has come to mean another project (<see cref="Check"/>).
    /// Immutable.
    /// </summary>
    public sealed class ModelLink
    {
        public ModelLink(int projectId, int number, int subNumber, string name, DateTime? linkedAtUtc, string linkedBy)
        {
            ProjectId = projectId;
            Number = number;
            SubNumber = subNumber;
            Name = (name ?? "").Trim();
            LinkedAtUtc = AsUtc(linkedAtUtc);
            LinkedBy = string.IsNullOrWhiteSpace(linkedBy) ? null : linkedBy.Trim();
        }

        /// <summary>The database's id for the project.</summary>
        public int ProjectId { get; }

        /// <summary>E.g. 1761.</summary>
        public int Number { get; }

        /// <summary>E.g. 1 for 1761-01.</summary>
        public int SubNumber { get; }

        /// <summary>The project's name when the model was linked.</summary>
        public string Name { get; }

        /// <summary>When the model was linked, in UTC; null if unknown.</summary>
        public DateTime? LinkedAtUtc { get; }

        /// <summary>Who linked it (Revit's user name); null if unknown.</summary>
        public string LinkedBy { get; }

        public static ModelLink To(LinkProject project, DateTime linkedAtUtc, string linkedBy)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            return new ModelLink(project.ProjectId, project.Number, project.SubNumber, project.Name, linkedAtUtc, linkedBy);
        }

        /// <summary>E.g. "1761-01 Ulm".</summary>
        public string Describe() => ProjectCode.Describe(Number, SubNumber, Name);

        /// <summary>
        /// Whether the database's project with the saved id is still the project the model was linked to:
        /// the same number and sub-number. A new name doesn't matter.
        /// </summary>
        public ModelLinkCheck Check(IEnumerable<LinkProject> projects)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            LinkProject project = projects.FirstOrDefault(p => p.ProjectId == ProjectId);
            if (project == null)
                return new ModelLinkCheck(ModelLinkStatus.ProjectGone, null);

            bool same = project.Number == Number && project.SubNumber == SubNumber;
            return new ModelLinkCheck(same ? ModelLinkStatus.Matches : ModelLinkStatus.ProjectChanged, project);
        }

        public override string ToString() => Describe();

        private static DateTime? AsUtc(DateTime? time)
        {
            if (!time.HasValue)
                return null;
            DateTime value = time.Value;
            return value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    /// <summary>Whether a model's link still holds in the database it reads.</summary>
    public enum ModelLinkStatus
    {
        /// <summary>The database still has the project, with the same number and sub-number.</summary>
        Matches,

        /// <summary>The database has no project with the saved id: it was deleted, or it's another database.</summary>
        ProjectGone,

        /// <summary>The saved id is another project now: another database, or the project was renumbered in Studio.</summary>
        ProjectChanged,
    }

    /// <summary>The answer of <see cref="ModelLink.Check"/>.</summary>
    public sealed class ModelLinkCheck
    {
        internal ModelLinkCheck(ModelLinkStatus status, LinkProject project)
        {
            Status = status;
            Project = project;
        }

        public ModelLinkStatus Status { get; }

        /// <summary>The database's project with the saved id; null when it's gone.</summary>
        public LinkProject Project { get; }
    }
}
