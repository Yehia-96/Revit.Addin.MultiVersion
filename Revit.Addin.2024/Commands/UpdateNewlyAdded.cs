using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Revit.Addin._2024.Commands
{

    // This class was an attempt to debug the weird behaviour of the updater class
    // where newly added Element's Ids were not being returned (more on that in the ElementUpdater.cs class)
    // to ignore the event and use a manual button that triggers this functionality
    public class UpdateNewlyAdded
    {
        public static List<ElementId> newElementIds = new List<ElementId>();
        public static void ApplyChanges(Document doc)
        {
            if (newElementIds.Count == 0) 
            {
                TaskDialog.Show("Warning", "No new elements detected");
                return;
            }
            foreach (var elementId in newElementIds) 
            {
                Element ele = doc.GetElement(elementId);
                Parameter bn = ele.LookupParameter("Bauteilnummer");
                if (bn == null)
                    continue;
                
            }
        }
    }
}
