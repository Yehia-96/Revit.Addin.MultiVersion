using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2024.DB;
using Revit.Addin._2024.Services;
using Revit.Addin._2024.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;


namespace Revit.Addin._2024.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PopulateFromDbCommand : IExternalCommand
    {
        private const string ConnectionString =
            @"Server=(localdb)\MSSQLLocalDB;Database=MH_Database;Trusted_Connection=True;";

        private string SharedParamsFilePath = AddParametersToFamilies._sharedParamPath;

        private const string KeyParamName = "Bauteil";

        public Result Execute(ExternalCommandData cData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = cData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var repo = new DbRepo(ConnectionString);
                var projects = repo.GetProjects();
                if (projects.Count == 0)
                {
                    TaskDialog.Show("DB", "No projects found in database.");
                    return Result.Cancelled;
                }

                int? projectId = null;
                using (var form = new ProjectPickerForm(projects))
                {
                    var res = form.ShowDialog();
                    if (res != DialogResult.OK || form.SelectedProjectId == null)
                        return Result.Cancelled;

                    projectId = form.SelectedProjectId.Value;
                }

                var famInstances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                var targets = new List<FamilyInstance>();
                foreach (var fi in famInstances)
                {
                    var p = fi.LookupParameter(KeyParamName);
                    if (p == null) continue;

                    var val = p.AsString();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    targets.Add(fi);
                }

                if (targets.Count == 0)
                {
                    TaskDialog.Show("DB", "No FamilyInstance elements found with parameter 'Bauteil'.");
                    return Result.Cancelled;
                }

                var map = repo.GetParamMapByProject(projectId.Value);

                var perFamily = targets
                    .GroupBy(fi => fi.Symbol != null ? fi.Symbol.Family : null)
                    .Where(g => g.Key != null)
                    .ToDictionary(g => g.Key, g => g.ToList());

                using (var tg = new TransactionGroup(doc, "Populate from DB (Families)"))
                {
                    tg.Start();

                    foreach (var kv in perFamily)
                    {
                        Family fam = kv.Key;
                        List<FamilyInstance> famElems = kv.Value;

                        var neededParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var fi in famElems)
                        {
                            string objName = fi.LookupParameter(KeyParamName).AsString();
                            if (string.IsNullOrWhiteSpace(objName)) continue;

                            List<(string ParamName, string Value)> pvals;
                            if (!map.TryGetValue(objName, out pvals)) continue;

                            foreach (var pv in pvals)
                                neededParamNames.Add(pv.ParamName);
                        }

                        if (neededParamNames.Count == 0)
                            continue;

                        Document famDoc = null;

                        try
                        {
                            famDoc = doc.EditFamily(fam);

                            FamilySharedParamWriter.AddSharedParametersToFamily(
                                uiapp.Application,
                                famDoc,
                                SharedParamsFilePath,
                                neededParamNames,
                                isInstance: true);

                            if (doc.IsModifiable)
                                throw new InvalidOperationException("Project document is modifiable; close any open transaction before LoadFamily.");

                            famDoc.LoadFamily(doc, new SimpleLoad());

                        }
                        finally
                        {
                            if (famDoc != null)
                                famDoc.Close(false);
                        }
                    }

                    using (var t = new Transaction(doc, "Set parameter values"))
                    {
                        t.Start();

                        foreach (var fi in targets)
                        {
                            var keyParam = fi.LookupParameter(KeyParamName);
                            if (keyParam == null) continue;

                            string objName = keyParam.AsString();
                            if (string.IsNullOrWhiteSpace(objName)) continue;

                            List<(string ParamName, string Value)> pvals;
                            if (!map.TryGetValue(objName, out pvals)) continue;

                            foreach (var pv in pvals)
                            {
                                var p = fi.LookupParameter(pv.ParamName);
                                if (p == null || p.IsReadOnly) continue;

                                if (p.StorageType == StorageType.String) p.Set(pv.Value ?? "");
                                else if (p.StorageType == StorageType.Integer)
                                {
                                    int i;
                                    if (int.TryParse(pv.Value, out i)) p.Set(i);
                                }
                                else if (p.StorageType == StorageType.Double)
                                {
                                    double d;
                                    if (double.TryParse(pv.Value, out d)) p.Set(d);
                                }
                            }
                        }

                        t.Commit();
                    }

                    tg.Assimilate();
                }

                TaskDialog.Show("DB", "Completed.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}
