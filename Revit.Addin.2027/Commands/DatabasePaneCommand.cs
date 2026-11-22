using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2027.DatabaseLink.Pane;
using System;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// The big Database button: shows the MH Database pane, or hides it when it's showing. Linking the model
    /// and the database's changes are in the pane (step 5).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DatabasePaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!DatabasePane.IsRegistered)
                {
                    TaskDialog.Show(DatabasePane.Title, "The MH Database pane didn't start with Revit"
                        + (DatabasePane.StartProblem == null ? "." : ": " + DatabasePane.StartProblem)
                        + " Restart Revit; if it stays away, tell whoever looks after the add-in.");
                    return Result.Cancelled;
                }
                DatabasePane.Toggle(commandData.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
