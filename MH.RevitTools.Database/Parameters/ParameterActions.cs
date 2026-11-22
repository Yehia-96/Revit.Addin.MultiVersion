using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// What the Check parameters dialog asks of Revit, as callbacks the command supplies: read the model's
    /// project parameters, add or replace parameters, ask before replacing, and name a category. Keeps the
    /// dialog free of the Revit API, so it can be drawn and tried without Revit.
    /// </summary>
    public sealed class ParameterActions
    {
        public ParameterActions(
            Func<IReadOnlyList<ModelBinding>> scanModel,
            Func<IReadOnlyList<ParameterToAdd>, ParameterAddResult> add,
            Func<IReadOnlyList<ParameterToReplace>, ParameterAddResult> replace,
            Func<string, string, bool> confirm,
            Func<string, string> categoryLabel)
        {
            ScanModel = scanModel ?? throw new ArgumentNullException(nameof(scanModel));
            Add = add ?? throw new ArgumentNullException(nameof(add));
            Replace = replace ?? throw new ArgumentNullException(nameof(replace));
            Confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            CategoryLabel = categoryLabel ?? throw new ArgumentNullException(nameof(categoryLabel));
        }

        /// <summary>Reads the model's project parameters again; called on Revit's thread.</summary>
        public Func<IReadOnlyList<ModelBinding>> ScanModel { get; }

        /// <summary>Binds missing parameters; called on Revit's thread.</summary>
        public Func<IReadOnlyList<ParameterToAdd>, ParameterAddResult> Add { get; }

        /// <summary>Swaps Other GUID parameters for the database's; called on Revit's thread.</summary>
        public Func<IReadOnlyList<ParameterToReplace>, ParameterAddResult> Replace { get; }

        /// <summary>Asks a yes/no question (title, text); true means go ahead.</summary>
        public Func<string, string, bool> Confirm { get; }

        /// <summary>Revit's name for a BuiltInCategory name, e.g. "Generic Models".</summary>
        public Func<string, string> CategoryLabel { get; }
    }
}
