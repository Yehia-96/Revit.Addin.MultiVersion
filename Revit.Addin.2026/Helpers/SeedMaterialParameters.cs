using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Revit.Addin._2026.Services;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Document = Autodesk.Revit.DB.Document;

namespace Revit.Addin._2026.Helpers
{
    public class SeedMaterialParameters
    {
        public static int BindSharedParametersToMaterials(
        Document doc,
        Application app,
        string sharedParamFilePath,
        Dictionary<string, ParamMetaData> MaterialParameterDefinitions,
        string sharedParamFileGroupName = "Materials")
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (MaterialParameterDefinitions == null || MaterialParameterDefinitions.Count == 0) return 0;

            var log = new StringBuilder();

            // Open shared parameter file once
            app.SharedParametersFilename = sharedParamFilePath;
            DefinitionFile defFile = app.OpenSharedParameterFile()
                ?? throw new InvalidOperationException("Cannot open shared parameter file.");

            // Ensure a group exists in the shared parameter file for creating missing defs
            DefinitionGroup spGroup =
                defFile.Groups.get_Item(sharedParamFileGroupName) ?? defFile.Groups.Create(sharedParamFileGroupName);

            // Cache all external definitions by name for fast lookup
            var defsByName = defFile.Groups
                .Cast<DefinitionGroup>()
                .SelectMany(g => g.Definitions.Cast<Definition>())
                .OfType<ExternalDefinition>()
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Prepare Materials binding once
            Category matCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Materials);
            CategorySet cs = app.Create.NewCategorySet();
            cs.Insert(matCat);

            InstanceBinding binding = app.Create.NewInstanceBinding(cs);
            BindingMap map = doc.ParameterBindings;

            int bound = 0;

            using (Transaction tx = new Transaction(doc, "Bind shared parameters to Materials"))
            {
                tx.Start();

                foreach (var kvp in MaterialParameterDefinitions)
                {
                    // Dictionary key can be trusted as parameter name; also keep meta.Name in mind if you prefer that
                    string paramName = kvp.Key?.Trim();
                    ParamMetaData meta = kvp.Value;

                    if (string.IsNullOrWhiteSpace(paramName) || meta == null)
                        continue;

                    // Optional: only bind material-target parameters
                    if (meta.Target != ParamMetaData.ParamTarget.Material)
                        continue;

                    

                    // 1) Get or create the ExternalDefinition from the shared parameter file
                    if (!defsByName.TryGetValue(paramName, out ExternalDefinition fileDef))
                    {
                        // Create missing definition using metadata datatype
                        var opt = new ExternalDefinitionCreationOptions(paramName, meta.DataTypeRevit);
                        fileDef = (ExternalDefinition)spGroup.Definitions.Create(opt);
                        defsByName[paramName] = fileDef;
                    }

                    // 2) If project already has SharedParameterElement with same GUID, bind that Definition instead
                    SharedParameterElement spe = SharedParameterElement.Lookup(doc, fileDef.GUID);
                    Definition defToBind;
                    if (spe != null)
                    {
                        defToBind = spe.GetDefinition();
                    }
                    else
                    {
                        defToBind = fileDef;
                    }

                    // 3) Use the metadata's Revit group (fallback to Materials)
                    ForgeTypeId groupId = meta.GroupNameRevit ?? GroupTypeId.Materials;

                    // 4) Insert or update binding
                    bool ok = map.Insert(defToBind, binding, groupId);
                    if (!ok)
                        ok = map.ReInsert(defToBind, binding, groupId);

                    if (ok) bound++;
                    else log.AppendLine($"Failed to bind '{paramName}'.");
                }

                tx.Commit();
            }

            // If you want, show log in a TaskDialog here (kept out to avoid UI spam).
            return bound;
        }



        public static int ApplyValuesByMaterialName(
    Document doc,
    IDictionary<string, Dictionary<string, string>> valuesByMaterialName,
    bool createMissingMaterials = false,
    Func<Parameter, double, double> toInternalDouble = null)
{
    if (doc == null) throw new ArgumentNullException(nameof(doc));
    if (valuesByMaterialName == null || valuesByMaterialName.Count == 0) return 0;
    
    
    // Cache Revit materials by name (case-insensitive)
    var materialsByName = new FilteredElementCollector(doc)
        .OfClass(typeof(Material))
        .Cast<Material>()
        .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int written = 0;

    using (Transaction tx = new Transaction(doc, "Apply Material Parameter Values"))
    {
        tx.Start();

        foreach (var entry in valuesByMaterialName)
        {
            string materialName = entry.Key?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(materialName))
                    continue;

            // Find or create material
            if (!materialsByName.TryGetValue(materialName, out Material mat))
            {
                if (!createMissingMaterials) continue;

                ElementId newId = Material.Create(doc, materialName);
                mat = (Material)doc.GetElement(newId);
                materialsByName[materialName] = mat;
            }

            Dictionary<string, string> paramValues = entry.Value;
            if (paramValues == null || paramValues.Count == 0)
                continue;

            foreach (var pv in paramValues)
            {
                string paramName = pv.Key?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(paramName))
                    continue;

                Parameter p = mat.LookupParameter(paramName);
                if (p == null || p.IsReadOnly)
                    continue;

                string raw = pv.Value?.Trim() ?? "";

                if (TrySetParameterFromString(p, raw, toInternalDouble))
                    written++;
            }
        }

        tx.Commit();
    }

    return written;
}

private static bool TrySetParameterFromString(
    Parameter p,
    string raw,
    Func<Parameter, double, double> toInternalDouble)
{
    // Treat empty strings as "skip" for numeric types; for string, write empty.
    switch (p.StorageType)
    {
        case StorageType.String:
            return p.Set(raw ?? "");

        case StorageType.Integer:
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                // Common Yes/No patterns (also handles Excel "TRUE/FALSE")
                if (TryParseBoolLike(raw, out bool b))
                    return p.Set(b ? 1 : 0);

                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    return p.Set(i);

                // Some locales use comma/space; last resort:
                if (int.TryParse(raw, out i))
                    return p.Set(i);

                return false;
            }

        case StorageType.Double:
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                // Accept both "12.3" and "12,3"
                if (!TryParseDoubleFlexible(raw, out double d))
                    return false;

                // Optional conversion hook (e.g., Excel values in mm -> internal ft)
                if (toInternalDouble != null)
                    d = toInternalDouble(p, d);

                return p.Set(d);
            }

        case StorageType.ElementId:
            // Case 1 typically shouldn't use ElementId-typed shared parameters.
            // If you ever do, you need a resolver (e.g., map string -> ElementId).
            return false;

        default:
            return false;
    }
}

private static bool TryParseBoolLike(string s, out bool value)
{
    value = false;
    if (s == null) return false;

    string t = s.Trim().ToLowerInvariant();
    if (t == "1" || t == "true" || t == "yes" || t == "y" || t == "ja" || t == "j" || t == "wahr")
    {
        value = true;
        return true;
    }
    if (t == "0" || t == "false" || t == "no" || t == "n" || t == "nein" || t == "falsch")
    {
        value = false;
        return true;
    }
    return false;
}

private static bool TryParseDoubleFlexible(string s, out double d)
{
    // First try invariant (dot decimal)
    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d))
        return true;

    // Then try current culture (comma decimal in many locales)
    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out d))
        return true;

    // Finally: swap comma/dot as a last resort
    string swapped = s.Contains(',') && !s.Contains('.')
        ? s.Replace(',', '.')
        : s.Contains('.') && !s.Contains(',')
            ? s.Replace('.', ',')
            : s;

    return double.TryParse(swapped, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d)
        || double.TryParse(swapped, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out d);
}


}
}

