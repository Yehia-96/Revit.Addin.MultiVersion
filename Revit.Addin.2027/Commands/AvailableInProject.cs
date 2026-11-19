using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Keeps a ribbon button enabled only while a project model is active: greyed out with no model
    /// open and in the family editor.
    /// </summary>
    public class AvailableInProject : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            UIDocument active = applicationData.ActiveUIDocument;
            return active != null && !active.Document.IsFamilyDocument;
        }
    }
}
