using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// What the Check parameters dialog asks of Revit, as callbacks the command supplies: read the model's
    /// project parameters, add or replace parameters, find where those to replace are used and what Revit
    /// deletes with them, ask before replacing, and name a category. Keeps the dialog free of the Revit API,
    /// so it can be drawn and tried without Revit.
    /// </summary>
    public sealed class ParameterActions
    {
        public ParameterActions(
            Func<IReadOnlyList<ModelBinding>> scanModel,
            Func<IReadOnlyList<ParameterToAdd>, ParameterAddResult> add,
            Func<IReadOnlyList<ParameterToReplace>, ReplaceUses> findUses,
            Func<IReadOnlyList<ParameterToReplace>, ReplaceDeletions> findDeletions,
            Func<IReadOnlyList<ParameterToReplace>, IReadOnlyCollection<long>, ParameterAddResult> replace,
            Func<string, string, bool> confirm,
            Func<ReplaceUses, ReplaceChoice> askAboutUses,
            Func<string, string> categoryLabel)
        {
            ScanModel = scanModel ?? throw new ArgumentNullException(nameof(scanModel));
            Add = add ?? throw new ArgumentNullException(nameof(add));
            FindUses = findUses ?? throw new ArgumentNullException(nameof(findUses));
            FindDeletions = findDeletions ?? throw new ArgumentNullException(nameof(findDeletions));
            Replace = replace ?? throw new ArgumentNullException(nameof(replace));
            Confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            AskAboutUses = askAboutUses ?? throw new ArgumentNullException(nameof(askAboutUses));
            CategoryLabel = categoryLabel ?? throw new ArgumentNullException(nameof(categoryLabel));
        }

        /// <summary>Reads the model's project parameters again; called on Revit's thread.</summary>
        public Func<IReadOnlyList<ModelBinding>> ScanModel { get; }

        /// <summary>Binds missing parameters; called on Revit's thread.</summary>
        public Func<IReadOnlyList<ParameterToAdd>, ParameterAddResult> Add { get; }

        /// <summary>Finds the schedules and view filters that use the model's parameters Replace would delete; on Revit's thread.</summary>
        public Func<IReadOnlyList<ParameterToReplace>, ReplaceUses> FindUses { get; }

        /// <summary>Finds what else Revit deletes with the model's parameters, without changing the model; on Revit's thread.</summary>
        public Func<IReadOnlyList<ParameterToReplace>, ReplaceDeletions> FindDeletions { get; }

        /// <summary>
        /// Swaps Other GUID parameters for the database's, deleting at most the given other elements with them
        /// (the ones the user agreed to); called on Revit's thread.
        /// </summary>
        public Func<IReadOnlyList<ParameterToReplace>, IReadOnlyCollection<long>, ParameterAddResult> Replace { get; }

        /// <summary>Asks a yes/no question (title, text); true means go ahead.</summary>
        public Func<string, string, bool> Confirm { get; }

        /// <summary>Asks once more when schedules or view filters use some of the parameters to replace.</summary>
        public Func<ReplaceUses, ReplaceChoice> AskAboutUses { get; }

        /// <summary>Revit's name for a BuiltInCategory name, e.g. "Generic Models".</summary>
        public Func<string, string> CategoryLabel { get; }
    }
}
