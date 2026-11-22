using System;
using System.Collections.Generic;
using System.Globalization;
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

        // How many names the Replace question lists before "and N more".
        private const int NamesListed = 8;

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
                + "A parameter that a schedule or a view filter uses is left as it is, so they don't lose it. "
                + "The values come back from the MH database when they're applied. Undo puts everything back.";
        }

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
