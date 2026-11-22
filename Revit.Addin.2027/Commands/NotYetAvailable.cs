using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Keeps a ribbon button greyed out: for the mockup's buttons whose step isn't built yet (Apply
    /// changes, Sign in), so the Database panel already has its final layout.
    /// </summary>
    public class NotYetAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => false;
    }
}
