using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>What a model's Project Number says about its project.</summary>
    public enum SuggestionKind
    {
        /// <summary>Exactly one project fits the Project Number.</summary>
        Suggested,

        /// <summary>Project Information has no Project Number.</summary>
        NoProjectNumber,

        /// <summary>The Project Number isn't written like 1761 or 1761-01, e.g. "Ulm" or "1761.01".</summary>
        NotAProjectNumber,

        /// <summary>No project fits the Project Number.</summary>
        NoMatch,

        /// <summary>Several projects fit, e.g. "1761" without a sub-number when there are 1761-01 and 1761-02.</summary>
        SeveralMatch,
    }

    /// <summary>
    /// Which project a model's Project Number suggests (decided on 2026-11-19). With a sub-number, the
    /// project with that number and sub-number. Without one, the project with that number, if it's the
    /// only one. It's only a suggestion: the user confirms it in the Link dialog.
    /// </summary>
    public sealed class ProjectSuggestion
    {
        private static readonly IReadOnlyList<LinkProject> None = new LinkProject[0];

        private ProjectSuggestion(
            SuggestionKind kind,
            ProjectNumberText modelNumber,
            IReadOnlyList<LinkProject> matches,
            IReadOnlyList<LinkProject> sameNumber)
        {
            Kind = kind;
            ModelNumber = modelNumber;
            Matches = matches;
            SameNumber = sameNumber;
        }

        public SuggestionKind Kind { get; }

        /// <summary>The model's Project Number, read.</summary>
        public ProjectNumberText ModelNumber { get; }

        /// <summary>The suggested project; null unless <see cref="Kind"/> is <see cref="SuggestionKind.Suggested"/>.</summary>
        public LinkProject Project => Kind == SuggestionKind.Suggested ? Matches[0] : null;

        /// <summary>Every project that fits the Project Number.</summary>
        public IReadOnlyList<LinkProject> Matches { get; }

        /// <summary>Every project with the model's project number, whatever its sub-number.</summary>
        public IReadOnlyList<LinkProject> SameNumber { get; }

        public static ProjectSuggestion For(string projectNumber, IEnumerable<LinkProject> projects)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            ProjectNumberText modelNumber = ProjectNumberText.Parse(projectNumber);
            if (modelNumber.IsEmpty)
                return new ProjectSuggestion(SuggestionKind.NoProjectNumber, modelNumber, None, None);
            if (!modelNumber.IsReadable)
                return new ProjectSuggestion(SuggestionKind.NotAProjectNumber, modelNumber, None, None);

            List<LinkProject> sameNumber = projects.Where(p => p.Number == modelNumber.Number).ToList();
            List<LinkProject> matches = modelNumber.SubNumber.HasValue
                ? sameNumber.Where(p => p.SubNumber == modelNumber.SubNumber.Value).ToList()
                : sameNumber;

            SuggestionKind kind = matches.Count == 1 ? SuggestionKind.Suggested
                : matches.Count == 0 ? SuggestionKind.NoMatch
                : SuggestionKind.SeveralMatch;
            return new ProjectSuggestion(kind, modelNumber, matches.AsReadOnly(), sameNumber.AsReadOnly());
        }
    }
}
