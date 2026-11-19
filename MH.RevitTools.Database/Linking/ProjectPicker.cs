using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// What the Link dialog says and allows, without any UI: what the model is linked to, which project
    /// its Project Number suggests, the project list in order, and the Link button. The dialog only
    /// draws it.
    /// </summary>
    public sealed class ProjectPicker
    {
        private const int MaxButtonProjectLength = 40;

        private const string NotLinkedText =
            " is not linked yet. The link fills its elements by Bauteil_ID from this project's objects.";

        private readonly string _modelName;
        private readonly TimeZoneInfo _timeZone;
        private readonly IReadOnlyList<LinkProject> _ordered;
        private readonly HashSet<int> _sameNumber;

        /// <param name="modelName">The model's file name, e.g. "1761_Ulm_Tunnel.rvt".</param>
        /// <param name="projectNumber">Project Information's Project Number, as written.</param>
        /// <param name="currentLink">The link saved in the model; null when it has none.</param>
        /// <param name="projects">The database's projects.</param>
        /// <param name="timeZone">For showing when the model was linked; usually <see cref="TimeZoneInfo.Local"/>.</param>
        public ProjectPicker(
            string modelName,
            string projectNumber,
            ModelLink currentLink,
            IEnumerable<LinkProject> projects,
            TimeZoneInfo timeZone)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            List<LinkProject> all = projects.ToList();
            _modelName = ModelNameOrDefault(modelName);
            _timeZone = timeZone ?? TimeZoneInfo.Utc;
            CurrentLink = currentLink;
            Suggestion = ProjectSuggestion.For(projectNumber, all);
            LinkCheck = currentLink == null ? null : currentLink.Check(all);
            LinkedProject = LinkCheck != null && LinkCheck.Status == ModelLinkStatus.Matches ? LinkCheck.Project : null;
            _sameNumber = new HashSet<int>(Suggestion.SameNumber.Select(p => p.ProjectId));
            _ordered = Order(all);
        }

        /// <summary>The link saved in the model; null when it has none.</summary>
        public ModelLink CurrentLink { get; }

        /// <summary>Whether the saved link still holds; null when the model has no link.</summary>
        public ModelLinkCheck LinkCheck { get; }

        /// <summary>The database's project the model is linked to, while the link holds; else null.</summary>
        public LinkProject LinkedProject { get; }

        public ProjectSuggestion Suggestion { get; }

        /// <summary>The project picked when the dialog opens: the linked one, else the suggested one, else none.</summary>
        public LinkProject InitialPick => LinkedProject ?? Suggestion.Project;

        /// <summary>The sentence under the title: the model and what it's linked to.</summary>
        public IReadOnlyList<TextPart> Subtitle => BuildSubtitle();

        /// <summary>True when the saved link no longer holds, so the subtitle is a warning.</summary>
        public bool SubtitleIsWarning => LinkCheck != null && LinkCheck.Status != ModelLinkStatus.Matches;

        /// <summary>What the Project Number suggests; empty when there's nothing to say.</summary>
        public IReadOnlyList<TextPart> Banner => BuildBanner();

        /// <summary>True when the banner asks the user to look, rather than confirming a suggestion.</summary>
        public bool BannerIsWarning => LinkedProject != null || Suggestion.Kind != SuggestionKind.Suggested;

        /// <summary>The subtitle while the projects are still being read, before the link can be checked.</summary>
        public static IReadOnlyList<TextPart> SubtitleBeforeCheck(string modelName, ModelLink currentLink)
        {
            var parts = new List<TextPart> { TextPart.Code(ModelNameOrDefault(modelName)) };
            if (currentLink == null)
            {
                parts.Add(TextPart.Plain(NotLinkedText));
                return parts;
            }
            parts.Add(TextPart.Plain(" is linked to "));
            parts.Add(TextPart.Strong(currentLink.Describe()));
            parts.Add(TextPart.Plain("."));
            return parts;
        }

        /// <summary>
        /// The projects to list. A search keeps those whose number and name hold every word of it, in any
        /// case, so "1761 ulm" finds 1761-01 Ulm. The linked project comes first, then those with the
        /// model's project number, then the rest in the database's order.
        /// </summary>
        public IReadOnlyList<ProjectRow> Rows(string query)
        {
            string[] words = (query ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return _ordered
                .Where(p => HasAllWords(ProjectCode.Describe(p), words))
                .Select(ToRow)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>Whether Link may save this pick: a project, and not the one the model is already linked to.</summary>
        public bool CanLink(LinkProject pick) =>
            pick != null && (LinkedProject == null || pick.ProjectId != LinkedProject.ProjectId);

        public string LinkButtonText(LinkProject pick)
        {
            if (pick == null)
                return "Pick a project";
            if (!CanLink(pick))
                return "Already linked";
            return "Link to " + Shorten(ProjectCode.Describe(pick), MaxButtonProjectLength);
        }

        private IReadOnlyList<TextPart> BuildSubtitle()
        {
            if (CurrentLink == null)
                return SubtitleBeforeCheck(_modelName, null);

            var parts = new List<TextPart> { TextPart.Code(_modelName), TextPart.Plain(" is linked to ") };
            switch (LinkCheck.Status)
            {
                case ModelLinkStatus.Matches:
                    parts.Add(TextPart.Strong(ProjectCode.Describe(LinkedProject)));
                    parts.Add(TextPart.Plain("." + LinkedByText()));
                    break;
                case ModelLinkStatus.ProjectGone:
                    parts.Add(TextPart.Strong(CurrentLink.Describe()));
                    parts.Add(TextPart.Plain(", but the database has no such project any more. Pick the project again."));
                    break;
                default:
                    parts.Add(TextPart.Strong(CurrentLink.Describe()));
                    parts.Add(TextPart.Plain(", but in this database that project is now "));
                    parts.Add(TextPart.Strong(ProjectCode.Describe(LinkCheck.Project)));
                    parts.Add(TextPart.Plain(". Pick the project again."));
                    break;
            }
            return parts;
        }

        private string LinkedByText()
        {
            string when = CurrentLink.LinkedAtUtc.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(CurrentLink.LinkedAtUtc.Value, _timeZone)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : null;
            if (CurrentLink.LinkedBy == null)
                return when == null ? "" : " Linked " + when + ".";
            return when == null
                ? " Linked by " + CurrentLink.LinkedBy + "."
                : " Linked by " + CurrentLink.LinkedBy + ", " + when + ".";
        }

        private IReadOnlyList<TextPart> BuildBanner()
        {
            string number = Suggestion.ModelNumber.Text;
            LinkProject suggested = Suggestion.Project;

            // While the link holds, only a Project Number that points elsewhere is worth a word.
            if (LinkedProject != null)
            {
                if (suggested == null || suggested.ProjectId == LinkedProject.ProjectId)
                    return new TextPart[0];
                return Parts(
                    TextPart.Plain("Project Number "), TextPart.Strong(number), TextPart.Plain(" matches "),
                    TextPart.Strong(ProjectCode.Describe(suggested)), TextPart.Plain(", not the linked project."));
            }

            switch (Suggestion.Kind)
            {
                case SuggestionKind.Suggested:
                    return Parts(
                        TextPart.Plain("Suggested from Project Information: Project Number "), TextPart.Strong(number),
                        TextPart.Plain(" matches "), TextPart.Strong(ProjectCode.Describe(suggested)), TextPart.Plain("."));
                case SuggestionKind.NoProjectNumber:
                    return Parts(TextPart.Plain("Project Information has no Project Number. Pick the project."));
                case SuggestionKind.NotAProjectNumber:
                    return Parts(
                        TextPart.Plain("Project Number "), TextPart.Strong(number),
                        TextPart.Plain(" isn't written like 1761 or 1761-01. Pick the project."));
                case SuggestionKind.NoMatch:
                    return Parts(
                        TextPart.Plain("Project Number "), TextPart.Strong(number),
                        TextPart.Plain(" matches no project in the database. Pick the project."));
                default:
                    return Parts(
                        TextPart.Plain("Project Number "), TextPart.Strong(number),
                        TextPart.Plain(Suggestion.ModelNumber.SubNumber.HasValue ? " fits " : " has no sub-number and fits "),
                        TextPart.Strong(Suggestion.Matches.Count + " projects"), TextPart.Plain(". Pick the right one."));
            }
        }

        private List<LinkProject> Order(List<LinkProject> all)
        {
            var first = new List<LinkProject>();
            if (LinkedProject != null)
                first.Add(LinkedProject);
            first.AddRange(Suggestion.SameNumber.Where(p => LinkedProject == null || p.ProjectId != LinkedProject.ProjectId));

            var firstIds = new HashSet<int>(first.Select(p => p.ProjectId));
            return first.Concat(all.Where(p => !firstIds.Contains(p.ProjectId))).ToList();
        }

        private ProjectRow ToRow(LinkProject project)
        {
            bool isLinked = LinkedProject != null && project.ProjectId == LinkedProject.ProjectId;
            bool isSuggested = Suggestion.Project != null && project.ProjectId == Suggestion.Project.ProjectId;

            var notes = new List<string>();
            if (isLinked)
                notes.Add("Linked");
            if (isSuggested)
                notes.Add("Suggested");
            if (!isLinked && !isSuggested && _sameNumber.Contains(project.ProjectId))
                notes.Add("Same number");

            return new ProjectRow(project, ProjectCode.Format(project), project.Name, string.Join(" · ", notes));
        }

        private static bool HasAllWords(string text, string[] words) =>
            words.All(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);

        private static IReadOnlyList<TextPart> Parts(params TextPart[] parts) => parts;

        private static string ModelNameOrDefault(string modelName) =>
            string.IsNullOrWhiteSpace(modelName) ? "This model" : modelName.Trim();

        private static string Shorten(string text, int maxLength) =>
            text.Length <= maxLength ? text : text.Substring(0, maxLength - 1).TrimEnd() + "…";
    }
}
