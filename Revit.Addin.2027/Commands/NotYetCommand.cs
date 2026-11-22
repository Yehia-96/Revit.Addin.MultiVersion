using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit.Addin._2027.Commands
{
    /// <summary>The command behind the buttons <see cref="NotYetAvailable"/> greys out; never runs while it does.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class NotYetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("MH database", "This comes with a later step of the database link.");
            return Result.Cancelled;
        }
    }
}
