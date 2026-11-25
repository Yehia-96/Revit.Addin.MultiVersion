using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Changes;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// What the pane and the ribbon say about applying the database's changes, about what's left out, and about
    /// creating missing materials, in the mockup's words. Like <see cref="PaneTexts"/>, so the wording sits under tests.
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

        /// <summary>The section's title, e.g. "Not in this model · 38 materials".</summary>
        public static string MissingTitle(int count) => "Not in this model · " + PaneTexts.Count(count, "material", "materials");

        /// <summary>What the section says about them.</summary>
        public static string MissingDetail => "The project has them, this model doesn't. Create… makes them; Apply all doesn't.";

        /// <summary>The step in Revit's Undo list, e.g. "MH database: 38 materials".</summary>
        public static string CreateUndoName(int count) => UndoPrefix + PaneTexts.Count(count, "material", "materials");

        /// <summary>Create's question, e.g. "Create 38 materials in this model?".</summary>
        public static string CreateQuestion(int count) => "Create " + PaneTexts.Count(count, "material", "materials") + " in this model?";

        /// <summary>What the question says about them.</summary>
        /// <param name="project">E.g. "1761-01 Ulm".</param>
        public static string CreateText(int count, string project) =>
            (count == 1 ? "It's" : "They're") + " in " + project + ", but not in this model. " + (count == 1 ? "It" : "Each")
            + " gets its material class, a look like one of its kind that's in the model, its fill patterns where the model has them, and its values. "
            + "One undo step (Ctrl+Z).";

        /// <summary>The green banner's title, e.g. "3 changes applied to 16 elements", or "38 materials created".</summary>
        public static string AppliedTitle(ApplyResult result)
        {
            if (result != null && result.Created.HasValue)
                return result.Created.Value == 0 ? "No material was created" : PaneTexts.Count(result.Created.Value, "material", "materials") + " created";
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
            else if (result.DidSomething)
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
            bool creates = result.Created.HasValue;
            if (result.Outdated > 0)
            {
                sentences.Add((creates ? PaneTexts.Count(result.Outdated, "material", "materials") : PaneTexts.Count(result.Outdated, "value", "values"))
                    + " had changed in the database by then: check the list and " + (creates ? "create" : "apply") + " again.");
            }
            if (result.Refused.Count > 0)
                sentences.Add((creates ? "Revit refused some: " : "Revit refused some values: ") + string.Join("; ", result.Refused) + ".");
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
                case NotAppliedReason.MissingMaterial:
                    return "The material “" + item.Name + "” is not in this model";
                case NotAppliedReason.MissingPattern:
                    return "The fill pattern “" + item.Name + "” is not in this model";
                case NotAppliedReason.NoMaterialParameter:
                    return PaneTexts.Count(elements, "element has", "elements have") + " no parameter for " + (elements == 1 ? "its" : "their") + " material";
                case NotAppliedReason.SameMaterialName:
                    return "Two materials are called “" + item.Name + "”";
                case NotAppliedReason.NotCreatable:
                    return item.Name + " has no " + item.Text;
                case NotAppliedReason.OfElements:
                    return Names(item.Parameters) + (item.Parameters.Count == 1 ? " goes" : " go") + " on elements, not on materials";
                default:
                    return PaneTexts.Count(item.ValueCount, "value is ", "values are ") + item.Name;
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
                case NotAppliedReason.MissingMaterial:
                    return "It's the material of " + PaneTexts.Count(item.ElementIds.Count, "element", "elements") + ". " + WhoMakes(item);
                case NotAppliedReason.NoMaterialParameter:
                    return "The database says what " + (one ? "it's" : "they're") + " made of. " + (one ? "Its family needs" : "Their families need")
                        + " an instance parameter called " + ObjectMaterial.ParameterName + " that takes a material, as for the Excel import.";
                case NotAppliedReason.MissingPattern:
                    return "It's " + string.Join(" and ", item.Parameters.Select(name => "the " + name.ToLowerInvariant())) + " of "
                        + PaneTexts.Count(item.ElementIds.Count, "material", "materials") + ". Add it to the model (Manage, Additional Settings, Fill "
                        + "Patterns), or copy something that uses it from another model; then it can be applied.";
                case NotAppliedReason.SameMaterialName:
                    return "Revit tells materials apart by name, so " + item.Text + " is left out. Give it another name in Studio.";
                case NotAppliedReason.NotCreatable:
                    return "So it can't be created in Revit. Give it one in Studio.";
                case NotAppliedReason.OfElements:
                    return "The database has values of " + (item.Parameters.Count == 1 ? "it" : "them") + " for materials too; Revit has no place for those.";
                default:
                    return PlaceholderDetail(item);
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
                    return "The connection file can't be used, so there's nothing to apply. The MH Database pane says why.";
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
            if (report.HasBlockedDifferences)
                return BlockedDetail.Replace("See Not applied below.", "The MH Database pane lists them under Not applied.");
            return report.MissingMaterials.Count == 0
                ? "The model matches the database."
                : "What the model has matches the database. The MH Database pane lists the materials it doesn't have.";
        }

        /// <summary>The pane's line where Apply is when it can't be used; null when it can.</summary>
        public static string ApplyWaits(CheckState state, bool isReadOnly) => Waits("Apply", "applied", state, isReadOnly);

        /// <summary>The pane's line where Create is when it can't be used; null when it can.</summary>
        public static string CreateWaits(CheckState state, bool isReadOnly) => Waits("Create", "created", state, isReadOnly);

        private static string Waits(string what, string done, CheckState state, bool isReadOnly)
        {
            if (isReadOnly)
                return "The model is open read-only, so nothing can be " + done + ".";
            if (state == CheckState.NotSignedIn)
                return what + " waits until you sign in, so that nothing outdated goes in.";
            return state == CheckState.Answered
                ? null
                : what + " waits until the database answers again, so that nothing outdated goes in.";
        }

        // How a missing material gets into the model.
        private static string WhoMakes(NotAppliedItem item)
        {
            if (item.Text == null)
                return "Create… makes it (Not in this model); then it can be applied.";
            return (item.Text == NotAppliedItem.Global ? "It's a global one, and Create… makes the project's own only." : "Create… can't make it.")
                + " Once the model has a material of that name, it can be applied.";
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
