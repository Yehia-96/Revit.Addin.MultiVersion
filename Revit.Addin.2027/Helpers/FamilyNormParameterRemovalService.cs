using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using System.Collections.Generic;
using System.Linq;
using static Revit.Addin._2027.Helpers.CleanUp;

public class FamilyNormParameterHelper
{
    private readonly Document _doc;

    public FamilyNormParameterHelper(Document doc)
    {
        _doc = doc;
    }

    public int RemoveNormParameterFromAllFamilies()
    {
        List<Family> families = CollectAllFamilies();
        int removedCount = 0;

        foreach (Family family in families)
        {
            bool removed = ProcessFamily(family);
            if (removed) removedCount++;
        }

        return removedCount;
    }

    private List<Family> CollectAllFamilies()
    {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .ToList();
    }

    private bool ProcessFamily(Family family)
    {
        if (!family.IsEditable)
            return false; 

        Document famDoc = _doc.EditFamily(family);
        if (famDoc == null) return false;

        FamilyParameter normParam = famDoc.FamilyManager.get_Parameter("Norm");
        if (normParam == null)
        {
            famDoc.Close(false);
            return false;
        }

        DeleteParameter(famDoc, normParam);
        string path = LoadFamilies.SaveFamily(famDoc, family.Name);
        var loadopts = UIDocument.GetRevitUIFamilyLoadOptions();
        using (Transaction t = new Transaction(_doc, "Edit"))
        {
            t.Start();
            famDoc.LoadFamily(_doc, loadopts);
            t.Commit();
        }
        famDoc.Close(true);
        return true;
    }

    private void DeleteParameter(Document famDoc, FamilyParameter param)
    {
        using (Transaction t = new Transaction(famDoc, "Remove Norm Parameter"))
        {
            t.Start();
            famDoc.FamilyManager.RemoveParameter(param);
            t.Commit();
        }
    }
}

public class OverwriteLoadOptions : IFamilyLoadOptions
{
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = true;
        return true;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
    {
        source = FamilySource.Family;
        overwriteParameterValues = true;
        return true;
    }
}
public static class LoadFamilies
{
    private static readonly string SaveFolder = @"C:\Yehia\Test_Folder";

    public static void FamilyLoader(Document doc)
    {
        FilteredElementCollector collector = new FilteredElementCollector(doc);
        IList<Family> families = collector.OfClass(typeof(Family)).Cast<Family>().ToList();
        var options = UIDocument.GetRevitUIFamilyLoadOptions();
       

            foreach (Family family in families)
            {
                if (family.IsEditable)
                {
                    Document familyDoc = doc.EditFamily(family);
                    familyDoc.LoadFamily(doc, options);
                    familyDoc.Close(false);
                }
            }

    }

    public static string SaveFamily(Document famDoc, string familyName)
    {
        string path = System.IO.Path.Combine(SaveFolder, familyName + ".rfa");
        var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
        famDoc.SaveAs(path, saveOpts);
        return path;
    }

    public static Document OpenFamily(Autodesk.Revit.ApplicationServices.Application app, string path)
    {
        return app.OpenDocumentFile(path);
    }
}