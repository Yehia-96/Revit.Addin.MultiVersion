using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Changes;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// What the pane and the ribbon say about applying the database's changes, and about what's left out, in
    /// the mockup's words. Like <see cref="PaneTexts"/>, so the wording sits under tests.
    /// </summary>
    public static class ApplyTexts
    {
        /// <summary>Names the add-in's changes in Revit's Undo list, so it can tell its own from others.</summary>
        public const string UndoPrefix = "MH database: ";

        // Longer lists of parameters are counted instead.
        private const int MaxNames = 5;

        /// <summary>A card's button: "Apply", or "Apply 3" for several changes.</summary>
        public static string ApplyLabel(int changes) =>
            changes > 1 ? "Apply " + changes.ToString(CultureInfo.InvariantCulture) : "Apply";

        /// <summary>The step in Revit's Undo list, e.g. "MH database: BT-1101-01" or "MH database: 17 changes".</summary>
        public static string UndoName(IReadOnlyList<ChangeGroup> groups)
        {
            if (groups == null || groups.Count == 0)
                return UndoPrefix + "changes";
            return UndoPrefix + (groups.Count == 1
                ? groups[0].Key
                : PaneTexts.Count(groups.Sum(group => group.Rows.Count), "change", "changes"));
        }

        /// <summary>The green banner's title, e.g. "3 changes applied to 16 elements".</summary>
        public static string AppliedTitle(ApplyResult result)
        {
            if (result == null || result.Changes == 0)
                return "Nothing was applied";
            return PaneTexts.Count(result.Changes, "change", "changes") + " applied to " + Targets(result.Elements, result.Materials);
        }

        /// <summary>The banner's line: when, how to undo it, and what was left out and why.</summary>
        public static string AppliedDetail(ApplyResult result, DateTime appliedAtUtc, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            if (result == null)
                return "";
            var sentences = new List<string>();
            if (result.Stopped != null)
                sentences.Add(Sentence(result.Stopped));
            else if (result.Changes > 0)
                sentences.Add(Capital(PaneTexts.WhenInSentence(appliedAtUtc, nowUtc, timeZone)) + ", one undo step in Revit (Ctrl+Z).");

            foreach (BorrowedElements borrowed in result.Borrowed)
            {
                sentences.Add("Not applied to " + PaneTexts.Count(borrowed.Count, "element", "elements") + " that " + Owner(borrowed.Owner)
                    + " has borrowed: apply again once " + (borrowed.Count == 1 ? "it's" : "they're") + " released.");
            }
            if (result.OutOfDate > 0)
            {
                sentences.Add("Not applied to " + PaneTexts.Count(result.OutOfDate, "element", "elements")
                    + " with newer changes in the central model: Reload Latest, then apply again.");
            }
            if (result.Outdated > 0)
            {
                sentences.Add(PaneTexts.Count(result.Outdated, "value", "values")
                    + " had changed in the database by then: check the list and apply again.");
            }
            if (result.Refused.Count > 0)
                sentences.Add("Revit refused some values: " + string.Join("; ", result.Refused) + ".");
            return string.Join(" ", sentences);
        }

        /// <summary>The section's title, e.g. "Not applied · 3".</summary>
        public static string NotAppliedTitle(int count) => "Not applied · " + count.ToString(CultureInfo.InvariantCulture);

        /// <summary>An entry's first line, e.g. "2 elements are borrowed by M. Keller".</summary>
        public static string Title(NotAppliedItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            int elements = item.ElementIds.Count;
            switch (item.Reason)
            {
                case NotAppliedReason.Borrowed:
                    return PaneTexts.Count(elements, "element is", "elements are") + " borrowed by " + Owner(item.Name);
                case NotAppliedReason.OutOfDate:
                    return PaneTexts.Count(elements, "element has", "elements have") + " newer changes in the central model";
                case NotAppliedReason.ReadOnly:
                    return Names(item.Parameters) + Are(item.Parameters) + " read-only in " + PaneTexts.Count(elements, "element", "elements");
                case NotAppliedReason.OnType:
                    return Names(item.Parameters) + (item.Parameters.Count == 1 ? " sits" : " sit") + " on the type of "
                        + PaneTexts.Count(elements, "element", "elements");
                case NotAppliedReason.DoesntFit:
                    return item.Text ?? "";
                case NotAppliedReason.ConflictingValues:
                    return item.Name + " has different values for " + Names(item.Parameters);
                case NotAppliedReason.MissingParameter:
                    return Names(item.Parameters) + Are(item.Parameters) + " not in this model";
                case NotAppliedReason.SameMaterialName:
                    return "Two materials are called “" + item.Name + "”";
                case NotAppliedReason.Placeholder:
                    return PaneTexts.Count(item.ValueCount, "value is ", "values are ") + item.Name;
                default:
                    return Names(item.Parameters) + (item.Parameters.Count == 1 ? " says" : " say") + " which material an object has";
            }
        }

        /// <summary>An entry's second line: why, and what can be done about it.</summary>
        public static string Detail(NotAppliedItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            bool one = item.ElementIds.Count == 1;
            switch (item.Reason)
            {
                case NotAppliedReason.Borrowed:
                    return (one ? "It gets" : "They get") + " the new values when you apply again after " + Owner(item.Name) + " releases "
                        + (one ? "it." : "them.");
                case NotAppliedReason.OutOfDate:
                    return "Reload Latest (Collaborate tab), then apply.";
                case NotAppliedReason.ReadOnly:
                    return "Revit lets nobody change it there, for example in a group, or where a formula sets it.";
                case NotAppliedReason.OnType:
                    return "The link fills parameters of the elements themselves; a type is shared by other elements.";
                case NotAppliedReason.DoesntFit:
                    return "The value in the database doesn't fit the parameter in Revit.";
                case NotAppliedReason.ConflictingValues:
                    return item.Text == null
                        ? "Objects in the database share this Bauteil_ID, so none of their values goes in. Give one another Bauteilunternummer in Studio."
                        : "Materials in the database share this Material-ID, so none of their values goes in.";
                case NotAppliedReason.MissingParameter:
                    return "Check parameters adds " + (item.Parameters.Count == 1 ? "it" : "them") + " from the master file.";
                case NotAppliedReason.SameMaterialName:
                    return "Revit tells materials apart by name, so " + item.Text + " is left out. Give it another name in Studio.";
                case NotAppliedReason.Placeholder:
                    return PlaceholderDetail(item);
                default:
                    return "Revit has " + (item.Parameters.Count == 1 ? "it" : "them")
                        + " on the material, not on the element. Assigning materials comes later.";
            }
        }

        /// <summary>The up-to-date card's title: "Up to date", or that the rest can't go in now.</summary>
        public static string UpToDateTitle(ChangeReport report) =>
            report != null && report.HasBlockedDifferences ? "Nothing to apply now" : "Up to date";

        /// <summary>The up-to-date card's line when values differ that can't go in now.</summary>
        public static string BlockedDetail => "Some values differ but can't go into the model now. See Not applied below.";

        /// <summary>The ribbon button's question, e.g. "Apply 17 changes from the database?".</summary>
        public static string ConfirmTitle(ChangeReport report) =>
            "Apply " + PaneTexts.ChangesTitle(report == null ? 0 : report.ChangeCount) + "?";

        /// <summary>What the question says about them.</summary>
        /// <param name="project">E.g. "1761-01 Ulm".</param>
        public static string ConfirmText(ChangeReport report, string project)
        {
            if (report == null)
                return "";
            int elements = report.Groups.Where(group => !group.IsMaterial).SelectMany(group => group.Rows).SelectMany(row => row.ElementIds).Distinct().Count();
            int materials = report.Groups.Where(group => group.IsMaterial).SelectMany(group => group.Rows).SelectMany(row => row.ElementIds).Distinct().Count();
            return "They go into " + Targets(elements, materials) + " of this model, from " + project
                + ", in one undo step (Ctrl+Z). The MH Database pane lists each of them.";
        }

        /// <summary>
        /// Why the ribbon button has nothing to apply, or null when it has: the model isn't linked, the
        /// database didn't answer, the model isn't compared yet, or it's up to date.
        /// </summary>
        public static string WhyNothingToApply(bool isLinked, CheckState? state, ChangeReport report)
        {
            if (!isLinked)
                return "This model isn't linked to a project in the MH database yet. Link it in the MH Database pane.";
            if (state == null)
                return "The database is still being checked. Try again in a moment.";
            switch (state.Value)
            {
                case CheckState.NotSetUp:
                    return "This computer has no connection file, so there's nothing to apply.";
                case CheckState.NotSignedIn:
                    return "Nobody is signed in to the MH database. Sign in first (Sign in… in the Database panel); the MH Database pane then checks for changes.";
                case CheckState.NotReachable:
                    return "The database isn't reachable now. Apply waits until it answers again, so that nothing outdated goes in.";
                case CheckState.ProjectMissing:
                case CheckState.ProjectChanged:
                    return "The database doesn't have this model's project any more. The MH Database pane says more.";
            }
            if (report == null)
                return "The model is still being compared with the database. Try again in a moment.";
            if (report.ChangeCount > 0)
                return null;
            return report.HasBlockedDifferences
                ? BlockedDetail.Replace("See Not applied below.", "The MH Database pane lists them under Not applied.")
                : "The model matches the database.";
        }

        /// <summary>The pane's line where Apply is when it can't be used; null when it can.</summary>
        public static string ApplyWaits(CheckState state, bool isReadOnly)
        {
            if (isReadOnly)
                return "The model is open read-only, so nothing can be applied.";
            if (state == CheckState.NotSignedIn)
                return "Apply waits until you sign in, so that nothing outdated goes in.";
            return state == CheckState.Answered
                ? null
                : "Apply waits until the database answers again, so that nothing outdated goes in.";
        }

        private static string PlaceholderDetail(NotAppliedItem item)
        {
            string names = Names(item.Parameters);
            bool one = item.Parameters.Count == 1;
            switch (item.Name)
            {
                case Placeholders.Modeller:
                    return names + (one ? " is" : " are") + " entered in the model and never overwritten.";
                case Placeholders.NotApplicable:
                    return names + (one ? " doesn't" : " don't") + " apply to these objects.";
                case Placeholders.LeftEmpty:
                    return names + (one ? " is" : " are") + " left empty on purpose.";
                default:
                    return names + (one ? " is" : " are") + " still to be determined.";
            }
        }

        private static string Targets(int elements, int materials)
        {
            string elementText = PaneTexts.Count(elements, "element", "elements");
            string materialText = PaneTexts.Count(materials, "material", "materials");
            if (materials == 0)
                return elementText;
            return elements == 0 ? materialText : elementText + " and " + materialText;
        }

        // "Dicke", "Dicke and Ort", "Dicke, Ort and Breite"; more than a few are counted.
        private static string Names(IReadOnlyList<string> names)
        {
            if (names.Count == 0)
                return "A parameter";
            if (names.Count > MaxNames)
                return names.Count.ToString(CultureInfo.InvariantCulture) + " parameters";
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }

        private static string Are(IReadOnlyList<string> names) => names.Count <= 1 ? " is" : " are";

        private static string Owner(string owner) => string.IsNullOrWhiteSpace(owner) ? "someone else" : owner;

        private static string Capital(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string Sentence(string text)
        {
            string trimmed = text.Trim();
            char last = trimmed.Length == 0 ? '.' : trimmed[trimmed.Length - 1];
            return last == '.' || last == '!' || last == '?' ? trimmed : trimmed + ".";
        }
    }
}
