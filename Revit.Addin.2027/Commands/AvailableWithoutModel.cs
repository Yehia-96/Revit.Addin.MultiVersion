using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Keeps a ribbon button enabled when no model is open. Revit greys out add-in buttons in that
    /// state unless the button names an availability class like this one.
    /// </summary>
    public class AvailableWithoutModel : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }
}
