// File: ElementUpdater.cs (cleaned for Revit 2024)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2024.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Revit.Addin._2024.Updaters
{   // This class implements an updater for Revit elements, specifically FamilyInstance elements.
    // It listens for changes to these elements and updates their parameters based on data from an Excel file.
    // The specific change that the class implements a method that handles the addition of new elements (Instances´)
    // Inside the project, by retrieving the newly added elements' ids, we can automatically update their parameters values
    // Since the updater functionality is not working in Revit2024 because the API GetChangeTypeElementAddition() Method and
    // GetAddedElementIds() are bugged and not working in Revit2024, we wont use this event for now.
    // Instead, try the Revit.Addin.2026 project, where this functionality is working as expected.(Same code)
    public class ElementUpdater : IUpdater
    {
        private static UpdaterId _updaterId;

        public ElementUpdater(AddInId id)
        {
            _updaterId = new UpdaterId(id, new Guid("A5232569-B89C-691D-E472-426614174000"));
        }

        public void Execute(UpdaterData data)
        {

            Document doc = data.GetDocument();


            var elements = data.GetAddedElementIds()
                               .Select(id => doc.GetElement(id))
                               .OfType<FamilyInstance>()
                               .ToList();
            Debug.WriteLine($"{elements.Count}");
            
            foreach (var element in elements)
            {
                if(element.LookupParameter("Bauteilnummer1") == null){
                    continue;
                }
                FamilyUpdateQueue.EnqueueUpdate(doc, element.Id);

            }
            FamilyUpdateEvent.Raise(); // see below


        }


        public UpdaterId GetUpdaterId() => _updaterId;

        public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;

        public string GetUpdaterName() => "Auto Parameter Updater";

        public string GetAdditionalInformation() => "Updates new elements with their corresponding parameters and values.";

        public static void Unregister(AddInId id)
        {
            var updaterId = new UpdaterId(id, new Guid("A5232569-B89C-691D-E472-426614174000"));
            if (UpdaterRegistry.IsUpdaterRegistered(updaterId))
                UpdaterRegistry.UnregisterUpdater(updaterId);
        }
    }

    public static class FamilyUpdateQueue
    {
        public static List<FamilyUpdateData> PendingUpdates { get; } = new List<FamilyUpdateData>();

        public static void EnqueueUpdate(Document doc, ElementId id)
        {
            PendingUpdates.Add(new FamilyUpdateData { Doc = doc, ElementId = id });
        }
    }

    public class FamilyUpdateData
    {
        public Document Doc { get; set; }
        public ElementId ElementId { get; set; }
    }

    public class FamilyUpdateExternalEvent : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Debug.WriteLine("here");

            var updates = FamilyUpdateQueue.PendingUpdates.ToList();
            FamilyUpdateQueue.PendingUpdates.Clear();

            var tuples = new List<Tuple<Parameter, ParameterValue>>();

            foreach (var update in updates)
            {
                Document doc = update.Doc;
                Element elem = doc.GetElement(update.ElementId);
                FamilyInstance instance = elem as FamilyInstance;
                Debug.WriteLine(elem.Id);
                Debug.WriteLine("passed");
                
                


                Parameter bnParam = instance.LookupParameter("Bauteilnummer1")
                                     ?? instance.Symbol?.LookupParameter("Bauteilnummer1");
               
                if (bnParam == null)
                {
                    TaskDialog.Show("DEBUG", $"Bauteilnummer1 param is missing on {instance.Id}");
                    continue;
                }
                if (!bnParam.HasValue)
                {
                    TaskDialog.Show("DEBUG", $"Bauteilnummer1 param has no value on {instance.Id}");
                    continue;
                }
                


                string bn = bnParam.AsString();
                   
                if (!ExcelParser.FamilyData.TryGetValue(bn, out var parameters))
                    continue;

                Family family = instance.Symbol.Family;
                DefinitionFile defFile = doc.Application.OpenSharedParameterFile();
                if (defFile == null) continue;

                Document famDoc = doc.EditFamily(family);
                FamilyManager fm = famDoc.FamilyManager;

                using (Transaction tx = new Transaction(famDoc, "Inject Shared Parameters"))
                {
                    tx.Start();
                    foreach (var kvp in parameters)
                    {
                        string paramName = kvp.Key;
                        if (fm.Parameters.Cast<FamilyParameter>().Any(p => p.Definition.Name == paramName))
                            continue;

                        Definition def = defFile.Groups
                            .SelectMany(g => g.Definitions.Cast<Definition>())
                            .FirstOrDefault(d => d.Name == paramName);

                        if (def is ExternalDefinition extDef && ExcelParser.ParameterDefinitions.TryGetValue(paramName, out var meta))
                        {
                            fm.AddParameter(extDef, meta.GroupNameRevit, true);
                        }
                    }
                    tx.Commit();
                }

                famDoc.LoadFamily(doc, new SimpleLoad());
                famDoc.Close(true);

                foreach (var kvp in parameters)
                {
                    if (!ExcelParser.ParameterDefinitions.TryGetValue(kvp.Key, out var meta))
                        continue;

                    Parameter param = elem.LookupParameter(kvp.Key);
                    if (param != null && !param.IsReadOnly)
                    {
                        ParameterValue val = ParamMetaData.ConvertValue(doc, meta.DataTypeRevit, kvp.Value);
                        tuples.Add(Tuple.Create(param, val));
                    }
                }
            }

            if (tuples.Any())
            {
                using (Transaction tx = new Transaction(updates[0].Doc, "Apply Parameter Values"))
                {
                    tx.Start();
#if REVIT2024
                    Parameter.SetMultiple(tuples);
#else
                    foreach (var t in tuples)
                    {
                        var p = t.Item1;
                        var v = t.Item2;
                        if (v is StringParameterValue sv) p.Set(sv.Value);
                        else if (v is DoubleParameterValue dv) p.Set(dv.Value);
                        else if (v is IntegerParameterValue iv) p.Set(iv.Value);
                        else if (v is ElementIdParameterValue ev) p.Set(ev.Value);
                    }
#endif
                    tx.Commit();
                }
            }
        }

        public string GetName() => "Family Updater External Event";
    }

    public static class FamilyUpdateEvent
    {
        private static readonly ExternalEvent _event;
        static FamilyUpdateEvent()
        {
            _event = ExternalEvent.Create(new FamilyUpdateExternalEvent());
        }
        public static void Raise()
        {
            Debug.WriteLine("here");

            _event.Raise();
        }
    }
}
