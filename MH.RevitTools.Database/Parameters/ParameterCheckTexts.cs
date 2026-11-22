using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>What the Check parameters dialog says, as in the mockup, so it's tested without any UI.</summary>
    public static class ParameterCheckTexts
    {
        public const string Footer =
            "Parameters are only added to the categories; families are not reloaded. Add touches no values; "
            + "Replace asks first, as it deletes the old parameters' values.";

        public const string OtherGuidHint =
            "Other GUID: the model has a parameter with this name from another shared parameter file, so the link "
            + "leaves it empty. Revit can't change a parameter's GUID: Replace deletes the model's parameter, with "
            + "its values in every element, and adds the database's in its place.";

        // How many names the Replace questions list before "and N more".
        private const int NamesListed = 8;

        // How many schedules, or view filters, the second question names for one parameter.
        private const int PlacesListed = 3;

        /// <summary>The sentence under the title, naming the master file and when Studio wrote it.</summary>
        public static IReadOnlyList<TextPart> Subtitle(MasterParameterFile masterFile, TimeZoneInfo timeZone)
        {
            string name = masterFile == null ? "MH_Parameter.txt" : masterFile.FileName;
            string written = masterFile == null || !masterFile.WrittenAtUtc.HasValue
                ? ""
                : " (" + TimeZoneInfo.ConvertTimeFromUtc(masterFile.WrittenAtUtc.Value, timeZone ?? TimeZoneInfo.Utc)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")";
            return new[]
            {
                TextPart.Plain("The link finds each parameter by its GUID from the master shared parameter file, "),
                TextPart.Code(name),
                TextPart.Plain(written + ". Revit shows the plain names; where two would look alike on one category, "
                    + "the set name is added."),
            };
        }

        /// <summary>E.g. "Missing · 3".</summary>
        public static string TabLabel(ParameterFilter filter, int count)
        {
            string name = filter == ParameterFilter.OtherGuid ? "Other GUID" : filter.ToString();
            return name + " · " + count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>E.g. "38 of 42 ready".</summary>
        public static string Summary(ParameterCheck check) =>
            check.ReadyCount.ToString(CultureInfo.InvariantCulture) + " of "
            + check.Rows.Count.ToString(CultureInfo.InvariantCulture) + " ready";

        public static string AddButton(ParameterCheck check)
        {
            int count = check.ToAdd.Count;
            if (count == 0)
                return "Nothing to add";
            return "Add " + count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " missing parameter" : " missing parameters");
        }

        /// <summary>E.g. "Replace 2…"; empty when there's nothing Replace can swap.</summary>
        public static string ReplaceButton(ParameterCheck check)
        {
            int count = check.ToReplace.Count;
            return count == 0 ? "" : "Replace " + count.ToString(CultureInfo.InvariantCulture) + "…";
        }

        /// <summary>The question Replace asks first: its title.</summary>
        public static string ReplaceTitle(ParameterCheck check)
        {
            int count = check.ToReplace.Count;
            return count == 1
                ? "Replace “" + check.ToReplace[0].Old.Name + "”?"
                : "Replace " + count.ToString(CultureInfo.InvariantCulture) + " parameters?";
        }

        /// <summary>The question Replace asks first: what it does, and which parameters.</summary>
        public static string ReplaceQuestion(ParameterCheck check)
        {
            var names = new List<string>();
            foreach (ParameterToReplace item in check.ToReplace)
            {
                if (names.Count == NamesListed)
                {
                    names.Add("and " + (check.ToReplace.Count - NamesListed).ToString(CultureInfo.InvariantCulture) + " more");
                    break;
                }
                names.Add("• " + item.Old.Name);
            }
            return "Revit can't change a parameter's GUID. Replacing deletes the model's parameter of the same name, "
                + "with its values in every element and on every category, and adds the database's from the master "
                + "file in its place:\n\n" + string.Join("\n", names) + "\n\n"
                + "Tags show the new parameters once their tag families use the master file's. The values come back "
                + "from the MH database when they're applied. Undo puts everything back.";
        }

        /// <summary>
        /// Replace's second question, asked when schedules or view filters use some of the model's parameters
        /// (Yehia, 2026-11-22): its title.
        /// </summary>
        public static string ReplaceUsedTitle(ReplaceUses uses)
        {
            if (uses == null) throw new ArgumentNullException(nameof(uses));
            if (uses.Problem != null)
                return "Revit couldn't check the schedules and view filters";

            IReadOnlyList<ModelBinding> used = uses.UsedParameters;
            string users = Users(uses);
            return used.Count == 1
                ? "Replace “" + used[0].Name + "” although " + users + " it?"
                : "Replace " + used.Count.ToString(CultureInfo.InvariantCulture) + " parameters that " + users + "?";
        }

        /// <summary>Replace's second question: where each parameter is used, and what replacing does there.</summary>
        public static string ReplaceUsedQuestion(ReplaceUses uses)
        {
            if (uses == null) throw new ArgumentNullException(nameof(uses));
            const string what =
                "Replacing a parameter takes it out of the schedules and view filters that use it: the schedules lose "
                + "its column, and the view filters can no longer filter by it. Add the new parameter to them by hand "
                + "afterwards, and check the views that use those filters. Undo puts everything back.";
            if (uses.Problem != null)
                return "Revit couldn't tell which schedules and view filters use these parameters (" + uses.Problem + ").\n\n" + what;

            IReadOnlyList<ModelBinding> used = uses.UsedParameters;
            var lines = new List<string>();
            foreach (ModelBinding parameter in used)
            {
                if (lines.Count == NamesListed)
                {
                    lines.Add("and " + (used.Count - NamesListed).ToString(CultureInfo.InvariantCulture) + " more");
                    break;
                }
                lines.Add("• " + parameter.Name + ": " + Places(uses.UsesOf(parameter)));
            }
            return string.Join("\n", lines) + "\n\n" + what;
        }

        /// <summary>
        /// Replace's second question: its answers, in order. Replace them too, then either replace only the
        /// others or, when all are in use, keep them. Cancel is the dialog's own.
        /// </summary>
        public static IReadOnlyList<ReplaceUsedAnswer> ReplaceUsedAnswers(ReplaceUses uses)
        {
            if (uses == null) throw new ArgumentNullException(nameof(uses));
            int used = uses.UsedParameters.Count;
            int others = uses.OtherParameters.Count;
            bool one = used == 1;
            if (others == 0)
            {
                // When Revit couldn't tell, no schedule or view filter is known to use them.
                string loses = uses.Problem != null
                    ? (one ? "Any schedules and view filters that use it lose it until you add the new one."
                           : "Any schedules and view filters that use them lose them until you add the new ones.")
                    : (one ? "Its schedules and view filters lose it until you add the new one."
                           : "Their schedules and view filters lose them until you add the new ones.");
                return new[]
                {
                    new ReplaceUsedAnswer(ReplaceChoice.ReplaceAll, one ? "Replace it anyway" : "Replace them anyway", loses),
                    new ReplaceUsedAnswer(ReplaceChoice.Cancel, one ? "Keep it" : "Keep them",
                        one ? "Leave the model's parameter as it is; the link leaves it empty."
                            : "Leave the model's parameters as they are; the link leaves them empty."),
                };
            }
            return new[]
            {
                new ReplaceUsedAnswer(ReplaceChoice.ReplaceAll,
                    "Replace all " + (used + others).ToString(CultureInfo.InvariantCulture),
                    one ? "Also the one in use: its schedules and view filters lose it until you add the new one."
                        : "Also the " + used.ToString(CultureInfo.InvariantCulture) + " in use: their schedules and view "
                          + "filters lose them until you add the new ones."),
                new ReplaceUsedAnswer(ReplaceChoice.ReplaceOthers,
                    others == 1 ? "Replace only the other one" : "Replace only the other " + others.ToString(CultureInfo.InvariantCulture),
                    one ? "Leave the one in use as it is; the link leaves it empty."
                        : "Leave the " + used.ToString(CultureInfo.InvariantCulture) + " in use as they are; the link leaves them empty."),
            };
        }

        /// <summary>
        /// The list Replace shows when Revit deletes other elements with some of the model's parameters
        /// (Yehia, 2026-11-22): its title, e.g. "Replacing “Dicke” also deletes 3 elements".
        /// </summary>
        public static string DeletionTitle(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));
            IReadOnlyList<ModelBinding> deleting = deletions.DeletingParameters;
            string what = deleting.Count == 1
                ? "“" + deleting[0].Name + "”"
                : deleting.Count.ToString(CultureInfo.InvariantCulture) + " parameters";
            return "Replacing " + what + " also deletes " + Elements(deletions.Elements.Count);
        }

        /// <summary>The list's explanation, over the elements.</summary>
        public static string DeletionIntro(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));
            bool one = deletions.DeletingParameters.Count == 1;
            string text = one
                ? "Revit can't delete this parameter on its own: it deletes the elements below with it. Check them "
                  + "before you go on; Undo puts everything back."
                : "Revit can't delete these parameters on their own: it deletes the elements below with them. Check "
                  + "them before you go on; Undo puts everything back.";
            int others = deletions.OtherParameters.Count;
            if (others == 1)
                text += " The other parameter goes on its own.";
            else if (others > 1)
                text += " The other " + others.ToString(CultureInfo.InvariantCulture) + " parameters go on their own.";
            return text;
        }

        /// <summary>The list's button that goes ahead, e.g. "Delete 3 elements and replace".</summary>
        public static string DeleteAndReplaceButton(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));
            return "Delete " + Elements(deletions.Elements.Count) + " and replace";
        }

        /// <summary>The list's button for the parameters that go on their own; empty when there are none.</summary>
        public static string DeletionOthersButton(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));
            int others = deletions.OtherParameters.Count;
            return others == 0 ? ""
                : others == 1 ? "Replace only the other one"
                : "Replace only the other " + others.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>For the banner after replacing only the others: a sentence for each parameter left.</summary>
        public static IReadOnlyList<string> LeftForDeletions(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));
            return deletions.DeletingParameters
                .Select(parameter => parameter.Name + ": left as it is, as Revit would delete other elements with it.")
                .ToList();
        }

        private static string Elements(int count) =>
            count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " element" : " elements");

        /// <summary>For the banner after "Replace only the others": a sentence for each parameter left in use.</summary>
        public static IReadOnlyList<string> LeftInUse(ReplaceUses uses)
        {
            if (uses == null) throw new ArgumentNullException(nameof(uses));
            return uses.UsedParameters
                .Select(parameter => parameter.Name + ": left as it is, as schedules or view filters use it.")
                .ToList();
        }

        // E.g. "a schedule uses", "2 schedules and a view filter use": the second question's subject and verb.
        private static string Users(ReplaceUses uses)
        {
            int schedules = PlaceCount(uses, ParameterUseKind.Schedule);
            int filters = PlaceCount(uses, ParameterUseKind.ViewFilter);
            var places = new List<string>();
            if (schedules > 0)
                places.Add(schedules == 1 ? "a schedule" : schedules.ToString(CultureInfo.InvariantCulture) + " schedules");
            if (filters > 0)
                places.Add(filters == 1 ? "a view filter" : filters.ToString(CultureInfo.InvariantCulture) + " view filters");
            return string.Join(" and ", places) + (schedules + filters == 1 ? " uses" : " use");
        }

        // Schedules (or view filters) using any of the parameters, each counted once by its name.
        private static int PlaceCount(ReplaceUses uses, ParameterUseKind kind) =>
            uses.UsedParameters.SelectMany(uses.UsesOf).Where(use => use.Kind == kind)
                .Select(use => use.Name).Distinct(StringComparer.Ordinal).Count();

        // E.g. "schedules “Wandliste” and “Mengen”; view filter “Dicke > 30”".
        private static string Places(IReadOnlyList<ParameterUse> uses)
        {
            var parts = new List<string>();
            AddPlaces(parts, uses, ParameterUseKind.Schedule, "schedule", "schedules");
            AddPlaces(parts, uses, ParameterUseKind.ViewFilter, "view filter", "view filters");
            return string.Join("; ", parts);
        }

        private static void AddPlaces(List<string> parts, IReadOnlyList<ParameterUse> uses, ParameterUseKind kind, string one, string many)
        {
            List<string> names = uses.Where(use => use.Kind == kind)
                .Select(use => "“" + use.Name + "”").Distinct(StringComparer.Ordinal).ToList();
            if (names.Count == 0)
                return;
            string listed = names.Count <= PlacesListed
                ? JoinWithAnd(names)
                : string.Join(", ", names.Take(PlacesListed)) + " and "
                  + (names.Count - PlacesListed).ToString(CultureInfo.InvariantCulture) + " more";
            parts.Add((names.Count == 1 ? one : many) + " " + listed);
        }

        // "A", "A and B", "A, B and C".
        private static string JoinWithAnd(IReadOnlyList<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];

        /// <summary>The status as the pill shows it.</summary>
        public static string State(ParameterState state)
        {
            switch (state)
            {
                case ParameterState.Missing: return "Missing";
                case ParameterState.CantAdd: return "Can't add";
                case ParameterState.OtherGuid: return "Other GUID";
                case ParameterState.Added: return "Added";
                default: return "Ready";
            }
        }
    }
}
