using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2027.DatabaseLink.Pane;
using System;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Database → Apply changes: everything the MH Database pane lists, after asking, in one undo step. Says why
    /// when there's nothing to apply.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ApplyChangesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                LinkMonitor monitor = LinkMonitor.Current;
                if (monitor == null)
                {
                    TaskDialog.Show(DatabasePane.Title, "The MH Database pane didn't start with Revit"
                        + (DatabasePane.StartProblem == null ? "." : ": " + DatabasePane.StartProblem) + " Restart Revit.");
                    return Result.Cancelled;
                }
                return monitor.ApplyFromRibbon(commandData.Application);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
