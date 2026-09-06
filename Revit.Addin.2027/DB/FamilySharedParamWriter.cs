using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace Revit.Addin._2027.DB
{
    public static class FamilySharedParamWriter
    {
        public static void AddSharedParametersToFamily(
            Application app,
            Document familyDoc,
            string sharedParamsFilePath,
            IEnumerable<string> parameterNames,
            bool isInstance)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (familyDoc == null) throw new ArgumentNullException(nameof(familyDoc));
            if (!familyDoc.IsFamilyDocument) throw new InvalidOperationException("Not a family document.");

            app.SharedParametersFilename = sharedParamsFilePath;
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null) throw new InvalidOperationException("Shared parameter file cannot be opened.");

            var defs = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (DefinitionGroup g in defFile.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    var ed = d as ExternalDefinition;
                    if (ed != null && !defs.ContainsKey(ed.Name))
                        defs[ed.Name] = ed;
                }
            }

            FamilyManager fm = familyDoc.FamilyManager;

            var existing = new HashSet<string>(
                fm.Parameters.Cast<FamilyParameter>().Select(fp => fp.Definition.Name),
                StringComparer.OrdinalIgnoreCase);

            var toAdd = parameterNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !existing.Contains(n))
                .ToList();

            if (toAdd.Count == 0) return;

            using (var t = new Transaction(familyDoc, "Add shared parameters"))
            {
                t.Start();

                foreach (string name in toAdd)
                {
                    ExternalDefinition extDef;
                    if (!defs.TryGetValue(name, out extDef))
                        continue; // not in shared param txt
                    
                    ForgeTypeId groupTypeId = extDef.GetGroupTypeId();
                    if (groupTypeId == null)
                        groupTypeId = GroupTypeId.Data; // default group

                    FamilyParameter fp = fm.AddParameter(extDef, groupTypeId, isInstance);
                }

                t.Commit();
            }
        }
    }
}
