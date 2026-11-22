using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using MH.RevitTools.Database.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Check parameters' Add and Replace: binds parameters from the local copy of Studio's master shared
    /// parameter file, as instance parameters on their categories, in one undo step. Revit opens only
    /// the file set in its options, so the add-in points it at the copy for the moment it reads it,
    /// then puts the user's own setting back. Families aren't reloaded. Add touches no values; Replace
    /// deletes the old parameter's, which is why the dialog asks first.
    /// </summary>
    internal static class ParameterAdder
    {
        public static ParameterAddResult Add(
            Application application, Document document, string masterFileCopy, IReadOnlyList<ParameterToAdd> toAdd)
        {
            if (toAdd == null || toAdd.Count == 0)
                return new ParameterAddResult(null, null);
            return WithMasterFile(application, document, masterFileCopy,
                definitions => Bind(application, document, definitions, toAdd));
        }

        /// <summary>
        /// Swaps each Other GUID parameter for the database's: deletes the model's parameter (and its values)
        /// and binds the database's in its place. Each swap is all or nothing: when the new one can't be
        /// bound, the old one stays.
        /// </summary>
        public static ParameterAddResult Replace(
            Application application, Document document, string masterFileCopy, IReadOnlyList<ParameterToReplace> toReplace)
        {
            if (toReplace == null || toReplace.Count == 0)
                return new ParameterAddResult(null, null);
            return WithMasterFile(application, document, masterFileCopy,
                definitions => ReplaceAll(application, document, definitions, toReplace));
        }

        // Opens the copy of the master file as Revit's shared parameter file for the time of the change.
        private static ParameterAddResult WithMasterFile(
            Application application, Document document, string masterFileCopy,
            Func<Dictionary<Guid, ExternalDefinition>, ParameterAddResult> change)
        {
            if (document.IsReadOnly)
                return ParameterAddResult.Failed("The model is open read-only, so nothing was changed.");

            string usersFile = application.SharedParametersFilename;
            ParameterAddResult result;
            string restoreProblem;
            try
            {
                application.SharedParametersFilename = masterFileCopy;
                DefinitionFile file = application.OpenSharedParameterFile();
                result = file == null
                    ? ParameterAddResult.Failed("Revit couldn't open the copy of the master shared parameter file, "
                        + masterFileCopy + ". Nothing was changed.")
                    : change(DefinitionsByGuid(file));
            }
            finally
            {
                restoreProblem = RestoreUsersFile(application, usersFile);
            }
            return restoreProblem == null ? result : result.WithProblem(restoreProblem);
        }

        // Puts the user's shared parameter file setting back; a failure must not hide what Add did.
        private static string RestoreUsersFile(Application application, string usersFile)
        {
            try
            {
                application.SharedParametersFilename = usersFile ?? "";
                return null;
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                return "Revit didn't take your own shared parameter file back (" + usersFile + "): " + ex.Message
                    + " Set it again under Manage > Shared Parameters.";
            }
        }

        private static Dictionary<Guid, ExternalDefinition> DefinitionsByGuid(DefinitionFile file)
        {
            var byGuid = new Dictionary<Guid, ExternalDefinition>();
            foreach (DefinitionGroup group in file.Groups)
            {
                foreach (Definition definition in group.Definitions)
                {
                    var external = definition as ExternalDefinition;
                    if (external != null && !byGuid.ContainsKey(external.GUID))
                        byGuid.Add(external.GUID, external);
                }
            }
            return byGuid;
        }

        private static ParameterAddResult Bind(
            Application application, Document document, Dictionary<Guid, ExternalDefinition> definitions, IReadOnlyList<ParameterToAdd> toAdd)
        {
            var added = new List<Guid>();
            var problems = new List<string>();
            using (var transaction = new Transaction(document, "Add MH parameters"))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    return ParameterAddResult.Failed("Revit couldn't start the change. Nothing was added.");

                foreach (ParameterToAdd item in toAdd)
                {
                    string problem = BindOne(application, document, definitions, item);
                    if (problem == null)
                        added.Add(item.Parameter.Guid);
                    else
                        problems.Add(item.Parameter.RevitName + ": " + problem + ".");
                }

                if (added.Count == 0)
                {
                    transaction.RollBack();
                    return new ParameterAddResult(added, problems);
                }
                if (transaction.Commit() != TransactionStatus.Committed)
                    return ParameterAddResult.Failed("Revit undid the change; its own message said why. Nothing was added.");
            }
            return new ParameterAddResult(added, problems);
        }

        private static ParameterAddResult ReplaceAll(
            Application application, Document document, Dictionary<Guid, ExternalDefinition> definitions, IReadOnlyList<ParameterToReplace> toReplace)
        {
            var replaced = new List<Guid>();
            var problems = new List<string>();
            var deleted = new HashSet<long>();
            using (var transaction = new Transaction(document, "Replace MH parameters"))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    return ParameterAddResult.Failed("Revit couldn't start the change. Nothing was replaced.");

                foreach (ParameterToReplace item in toReplace)
                {
                    string problem = ReplaceOne(application, document, definitions, item, deleted);
                    if (problem == null)
                        replaced.Add(item.Add.Parameter.Guid);
                    else
                        problems.Add(item.Old.Name + ": " + problem + ".");
                }

                if (replaced.Count == 0)
                {
                    transaction.RollBack();
                    return new ParameterAddResult(replaced, problems);
                }
                if (transaction.Commit() != TransactionStatus.Committed)
                    return ParameterAddResult.Failed("Revit undid the change; its own message said why. Nothing was replaced.");
            }
            return new ParameterAddResult(replaced, problems);
        }

        // One swap in a sub-transaction of its own: when anything goes wrong, rolling it back brings the old
        // parameter back, values and all, and the other swaps still go on. A parameter that two database
        // parameters clash with (one per category) is deleted once; the second swap only binds.
        private static string ReplaceOne(
            Application application, Document document, Dictionary<Guid, ExternalDefinition> definitions,
            ParameterToReplace item, HashSet<long> deleted)
        {
            bool alreadyGone = deleted.Contains(item.Old.ElementId);
            string problem = alreadyGone ? null : CheckOld(document, item.Old) ?? ParameterUses.Describe(document, item.Old.ElementId);
            if (problem != null)
                return problem;

            using (var swap = new SubTransaction(document))
            {
                if (swap.Start() != TransactionStatus.Started)
                    return "Revit couldn't start the swap";
                try
                {
                    problem = alreadyGone ? null : Delete(document, item.Old);
                    if (problem == null)
                        problem = BindOne(application, document, definitions, item.Add);
                    if (problem != null)
                    {
                        swap.RollBack();
                        return problem;
                    }
                    swap.Commit();
                    deleted.Add(item.Old.ElementId);
                    return null;
                }
                catch (Exception ex)
                {
                    // Undoes this swap only; the dialog names the error, and the other swaps go on.
                    if (swap.GetStatus() == TransactionStatus.Started)
                        swap.RollBack();
                    return "it failed unexpectedly (" + ex.Message.TrimEnd('.') + ")";
                }
            }
        }

        // Deletes the old parameter, and refuses when Revit would take anything else with it.
        private static string Delete(Document document, ModelBinding old)
        {
            ICollection<ElementId> gone;
            try
            {
                gone = document.Delete(new ElementId(old.ElementId));
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                return "Revit wouldn't delete the model's parameter: " + ex.Message.TrimEnd('.');
            }

            int others = gone.Count(id => id.Value != old.ElementId);
            return others == 0 ? null
                : "Revit would delete " + others + (others == 1 ? " other element" : " other elements")
                  + " with it; replace it by hand";
        }

        // The element to delete must still be that parameter: never delete on a stale id.
        private static string CheckOld(Document document, ModelBinding old)
        {
            var element = old.ElementId == 0 ? null : document.GetElement(new ElementId(old.ElementId)) as ParameterElement;
            InternalDefinition definition = element == null ? null : element.GetDefinition();
            var shared = element as SharedParameterElement;
            bool same = definition != null
                && string.Equals(definition.Name, old.Name, StringComparison.OrdinalIgnoreCase)
                && (old.Guid.HasValue ? shared != null && shared.GuidValue == old.Guid.Value : shared == null);
            return same ? null : "the model's parameter has changed since the check; check again";
        }

        // Null when bound; otherwise why not, in a few words. One refusal never stops the others.
        private static string BindOne(
            Application application, Document document, Dictionary<Guid, ExternalDefinition> definitions, ParameterToAdd item)
        {
            List<Category> categories;
            string problem = CategoriesOf(document, item, out categories);
            if (problem != null)
                return problem;

            try
            {
                return item.ExtendsBinding
                    ? Extend(document, item, categories)
                    : BindNew(application, document, definitions, item, categories);
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                return ex.Message.TrimEnd('.');
            }
        }

        private static string BindNew(
            Application application, Document document, Dictionary<Guid, ExternalDefinition> definitions,
            ParameterToAdd item, List<Category> categories)
        {
            ExternalDefinition definition;
            if (!definitions.TryGetValue(item.Parameter.Guid, out definition))
                return "Revit found no parameter with its GUID in the master file";

            CategorySet set = application.Create.NewCategorySet();
            foreach (Category category in categories)
                set.Insert(category);

            // A new binding never replaces one: Insert refuses a parameter the model already has.
            return document.ParameterBindings.Insert(definition, application.Create.NewInstanceBinding(set), GroupFor(item.Group))
                ? null
                : "the model has it bound already";
        }

        // Adds the categories to the model's own binding, so its other categories and its palette group stay.
        private static string Extend(Document document, ParameterToAdd item, List<Category> categories)
        {
            SharedParameterElement shared = SharedParameterElement.Lookup(document, item.Parameter.Guid);
            InternalDefinition definition = shared == null ? null : shared.GetDefinition();
            var binding = definition == null ? null : document.ParameterBindings.get_Item(definition) as InstanceBinding;
            if (binding == null)
                return "its binding in the model couldn't be read";

            foreach (Category category in categories)
            {
                if (!binding.Categories.Contains(category))
                    binding.Categories.Insert(category);
            }
            return document.ParameterBindings.ReInsert(definition, binding, definition.GetGroupTypeId())
                ? null
                : "Revit refused to add the categories";
        }

        private static string CategoriesOf(Document document, ParameterToAdd item, out List<Category> categories)
        {
            categories = new List<Category>();
            foreach (string name in item.Categories)
            {
                BuiltInCategory builtIn;
                Category category = Enum.TryParse(name, out builtIn) && Enum.IsDefined(typeof(BuiltInCategory), builtIn)
                    ? Category.GetCategory(document, builtIn)
                    : null;
                if (category == null || !category.AllowsBoundParameters)
                    return "Revit doesn't take project parameters on " + name;
                categories.Add(category);
            }
            return categories.Count == 0 ? "it has no category to go on" : null;
        }

        private static ForgeTypeId GroupFor(PaletteGroup group)
        {
            switch (group)
            {
                case PaletteGroup.IdentityData: return GroupTypeId.IdentityData;
                case PaletteGroup.Geometry: return GroupTypeId.Geometry;
                case PaletteGroup.Constraints: return GroupTypeId.Constraints;
                case PaletteGroup.Data: return GroupTypeId.Data;
                case PaletteGroup.Graphics: return GroupTypeId.Graphics;
                case PaletteGroup.Materials: return GroupTypeId.Materials;
                default: return GroupTypeId.General;
            }
        }
    }
}
