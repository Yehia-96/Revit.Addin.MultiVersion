using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using MH.RevitTools.Database.Linking;
using System;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Keeps a model's link to its MH database project in the model itself: one Extensible Storage
    /// entity on Project Information. It travels with the file and syncs with central like any other
    /// change, so everyone who opens the model works with the same project.
    ///
    /// Any add-in may read it. Only add-ins of this vendor (YH07, the VendorId in the .addin manifest)
    /// may change it: keep that VendorId, or existing links can be read but no longer changed.
    /// </summary>
    internal static class ProjectLinkStorage
    {
        // Never change the GUID or the fields: models saved with them would lose their link. A new
        // layout needs a new GUID (and reading the old one).
        private static readonly Guid SchemaGuid = new Guid("3403db21-4563-4fbe-b539-103297093036");
        private const string SchemaName = "MH_ProjectLink";
        private const string VendorId = "YH07";

        private const string ProjectIdField = "ProjectId";
        private const string NumberField = "ProjectNumber";
        private const string SubNumberField = "ProjectSubNumber";
        private const string NameField = "ProjectName";
        private const string LinkedAtField = "LinkedAtUtc";
        private const string LinkedByField = "LinkedBy";

        /// <summary>The link saved in the model; null when it has none.</summary>
        public static ModelLink Read(Document document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            // Revit knows a schema once a document that uses it is open, or once it's been made.
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
                return null;

            Entity entity = document.ProjectInformation.GetEntity(schema);
            if (entity == null || !entity.IsValid())
                return null;

            return new ModelLink(
                entity.Get<int>(ProjectIdField),
                entity.Get<int>(NumberField),
                entity.Get<int>(SubNumberField),
                entity.Get<string>(NameField),
                StoredTime.FromText(entity.Get<string>(LinkedAtField)),
                entity.Get<string>(LinkedByField));
        }

        /// <summary>Saves the link in the model, replacing any earlier one. Call inside a transaction.</summary>
        public static void Write(Document document, ModelLink link)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (link == null)
                throw new ArgumentNullException(nameof(link));

            Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
            var entity = new Entity(schema);
            entity.Set(ProjectIdField, link.ProjectId);
            entity.Set(NumberField, link.Number);
            entity.Set(SubNumberField, link.SubNumber);
            entity.Set(NameField, link.Name);
            entity.Set(LinkedAtField, StoredTime.ToText(link.LinkedAtUtc));
            entity.Set(LinkedByField, link.LinkedBy ?? "");
            document.ProjectInformation.SetEntity(entity);
        }

        private static Schema CreateSchema()
        {
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation("The MH database project this model is linked to (MH.RevitTools).");
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.AddSimpleField(ProjectIdField, typeof(int)).SetDocumentation("The database's id for the project.");
            builder.AddSimpleField(NumberField, typeof(int)).SetDocumentation("E.g. 1761.");
            builder.AddSimpleField(SubNumberField, typeof(int)).SetDocumentation("E.g. 1 for 1761-01.");
            builder.AddSimpleField(NameField, typeof(string)).SetDocumentation("The project's name when it was linked.");
            builder.AddSimpleField(LinkedAtField, typeof(string)).SetDocumentation("When, in UTC, e.g. 2026-11-19T14:32:05Z.");
            builder.AddSimpleField(LinkedByField, typeof(string)).SetDocumentation("Who linked it: Revit's user name.");
            return builder.Finish();
        }
    }
}
