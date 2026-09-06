using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitSelection = Autodesk.Revit.UI.Selection.Selection;
using Revit.Addin._2027.UI;
using System.Collections.Generic;
using System.Linq;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using RevitView = Autodesk.Revit.DB.View;
using System.Runtime.InteropServices;

namespace Revit.Addin._2027.Commands
{
    // Revit works with transactions to modify the document, so we need to wrap
    // our changes in a transaction. This command allows users to filter elements
    // by a unique ID stored in a parameter called "Bauteilnummer"
    // The user can select one or more IDs, and the command will
    // hide all elements that do not match the selected IDs
    [Transaction(TransactionMode.Manual)]
    public class ShowByUniqueID : IExternalCommand
    {
        // The class which implements the IExternalCommand interface, must implement
        // the execute method, which is the entry point for the command
        private static bool _hideSelectedPressed = false;
        private static bool _unhideAllPressed = false;
        private static bool _PermaHideSelection = false;
        private static IList<Reference> selectedElementsToHide = new List<Reference>();
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Get the current UIapplication, document, UIDocuemnt, and Application
            // (RevitApp) is an alias for Autodesk.Revit.ApplicationServices.Application
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            RevitApp app = uiApp.Application;
            Document doc = uiDoc.Document;

            // Grabs all elements in the document that are not element types
            FilteredElementCollector collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();


            DecidingParameter parametersList = new DecidingParameter();
            parametersList.ShowDialog();

            if (_unhideAllPressed)
            {
                IList<Element> elementsToUnhide = selectedElementsToHide.Select(r => doc.GetElement(r)).ToList();
                UnHideSelectedElements(doc, doc.ActiveView, elementsToUnhide);
                UnHideAllElements(doc, doc.ActiveView, collector.ToElements());

                _unhideAllPressed = false;
                selectedElementsToHide.Clear();

                return Result.Succeeded;
            }

            
            else if (parametersList.DialogResult != System.Windows.Forms.DialogResult.OK)
                return Result.Cancelled;

            if (_hideSelectedPressed)
            {
                RevitSelection selection = uiDoc.Selection;
                var TempSelectedElementsToHide = selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, "Select elements to hide");
                foreach (var reference in TempSelectedElementsToHide)
                {
                    selectedElementsToHide.Add(reference);
                }
                var elementsToHide = selectedElementsToHide.Select(r => doc.GetElement(r)).ToList();
                HideSelectedElements(doc, doc.ActiveView, elementsToHide);
                _hideSelectedPressed = false;
                return Result.Succeeded;
            }
            

            string parameterToHide = parametersList.SelectedParameter.ToString();

            var values = new HashSet<string>();

            // Iterate through all elements in the collector 
            // and ensure that the parameter "Bauteilnummer" exists, adds its value to a HashSet
            foreach (Element element in collector)
            {
                Parameter p = element.LookupParameter(parameterToHide);
                Parameter name = element.LookupParameter("Bauteil");

                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Integer)
                    {
                        int ValueAsInt = p.AsInteger();
                        values.Add(ValueAsInt.ToString());
                    }
                    else
                        values.Add(p.AsString());
                }
                else if (name != null && name.HasValue)
                {
                    string nameValue = name.AsString();
                    values.Add(nameValue);
                }
              
            }

            // Create a dialog to select the Bauteilnummer values from the UI classes
            BauteilnummerSelection dialog = new BauteilnummerSelection(values);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return Result.Cancelled;

            // If UnHide is pressed, we will unhide all elements in the active view
            if (dialog.SelectedValues.Count == 0)
            {
                UnHideAllElements(doc, doc.ActiveView, collector.ToElements());
            }
            else
            {
                if(_PermaHideSelection)
                {
                    var toPermaHide = GetElementsMatchingSelection(collector, dialog.SelectedValues, parameterToHide);
                    HideSelectedElements(doc, doc.ActiveView, toPermaHide);
                    _PermaHideSelection = false;

                    return Result.Succeeded;
                }
                var toHide = GetElementsNotMatchingSelection(collector, dialog.SelectedValues, parameterToHide);
                HideInsideList(toHide, doc, doc.ActiveView);
            }

            return Result.Succeeded;
        }
        // This method retrieves all elements in the active view that do not match the selected values
        private static IList<Element> GetElementsNotMatchingSelection(FilteredElementCollector collector, List<string> selectedValues, string parameterDecider)
        {
            var elementsToHide = new List<Element>();

            foreach (var element in collector)
            {
                var param = element.LookupParameter(parameterDecider);
                var paramName = element.LookupParameter("Bauteil");
                if (param == null || !param.HasValue)
                {
                    if (paramName != null)
                    {
                        if (!selectedValues.Contains(paramName.AsString()))
                        {
                            elementsToHide.Add(element);
                            continue;
                        }
                    }
                    else
                        elementsToHide.Add(element);
                    continue;

                }

                if (StorageType.Integer == param.StorageType)
                {
                    // If the parameter is an integer, convert it to string for comparison
                    if (!selectedValues.Contains(param.AsInteger().ToString()))
                        elementsToHide.Add(element);

                }
                else
                {
                    if (!selectedValues.Contains(param.AsString()))
                        elementsToHide.Add(element);
                }
            }

            return elementsToHide;
        }

        private static IList<Element> GetElementsMatchingSelection(FilteredElementCollector collector, List<string> selectedValues, string parameterDecider)
        {
            var elementsToPermahide = new List<Element>();

            foreach (var element in collector)
            {
                var param = element.LookupParameter(parameterDecider);
                var paramName = element.LookupParameter("Bauteil");
                if (param == null || !param.HasValue)
                {
                    if (paramName != null)
                    {
                        if (selectedValues.Contains(paramName.AsString()))
                        {
                            elementsToPermahide.Add(element);
                            continue;
                        }
                    }
                    continue;
                }
                if (StorageType.Integer == param.StorageType)
                {
                    // If the parameter is an integer, convert it to string for comparison
                    if (selectedValues.Contains(param.AsInteger().ToString()))
                        elementsToPermahide.Add(element);
                }
                else
                {
                    if (selectedValues.Contains(param.AsString()))
                        elementsToPermahide.Add(element);
                }

            }
            foreach (var element in elementsToPermahide)
            {
                selectedElementsToHide.Add(new Reference(element));
            }

            return elementsToPermahide;
        }
        // This method hides the elements in the active view that do not match the selected values
        // The elements are hidden temporarily, inside revit that a temp view mode
        private static void HideInsideList(IList<Element> elementsToHide, Document doc, RevitView view)
        {
            if (view == null || elementsToHide.Count == 0) return;

            var ids = elementsToHide.Select(e => e.Id).ToList();

            using (var tx = new Transaction(doc, "Hide Elements")) // Requires a transaction to modify 
            {
                tx.Start();
                view.HideElementsTemporary(ids);
                tx.Commit();
            }
        }
        // This method unhides all elements in the active view, removing any temporary hide/isolate mode
        private static void UnHideAllElements(Document doc, RevitView view, IList<Element> elementsToUnhide)
        {
            if (view == null) return;
            
            using (var tx = new Transaction(doc, "Unhide all elements"))
            {
                tx.Start();
                if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
                tx.Commit();
            }
        }

        public static void PickElementsToHide()
        {
            _hideSelectedPressed = true;
        }
        public static void PickElementsToUnhide()
        {
            _unhideAllPressed = true;
        }
        private static void HideSelectedElements(Document doc, RevitView view, IList<Element> elements)
        {
            if (view == null || elements.Count == 0) return;
            var ids = elements.Select(e => e.Id).ToList();
            using (var tx = new Transaction(doc, "Hide Elements"))
            {
                tx.Start();
                view.HideElements(ids);
                tx.Commit();
            }
        }
        private static void UnHideSelectedElements(Document doc, RevitView view, IList<Element> elements)
        {
            if (view == null || elements.Count == 0) return;
            var ids = elements.Select(e => e.Id).ToList();
            using (var tx = new Transaction(doc, "Unhide Elements"))
            {
                tx.Start();
                view.UnhideElements(ids);
                tx.Commit();
            }

        }

        public static void PermanentHideElements() 
        {
            _PermaHideSelection = true;
        }
    }
}
