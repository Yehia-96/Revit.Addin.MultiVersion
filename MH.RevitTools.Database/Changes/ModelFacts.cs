namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// One model element of a category the database's object parameters go on, as the Revit side reads it.
    /// Immutable.
    /// </summary>
    public sealed class ModelElement
    {
        public ModelElement(long id, string bauteilId, string typeName)
        {
            Id = id;
            BauteilId = string.IsNullOrWhiteSpace(bauteilId) ? null : bauteilId.Trim();
            TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName.Trim();
        }

        /// <summary>Revit's element id.</summary>
        public long Id { get; }

        /// <summary>
        /// The Bauteil_ID the modeller entered, e.g. BT-1101-01; null when there's none. Typed by hand, so it
        /// matches the database's whatever its case, as in the Excel route.
        /// </summary>
        public string BauteilId { get; }

        /// <summary>Its family and type, e.g. "Bodenplatte: 30 cm"; null when it has none.</summary>
        public string TypeName { get; }
    }

    /// <summary>One material in the model. Revit materials are named by the database's Materialname. Immutable.</summary>
    public sealed class ModelMaterial
    {
        public ModelMaterial(long id, string name)
        {
            Id = id;
            Name = (name ?? "").Trim();
        }

        public long Id { get; }

        public string Name { get; }
    }
}
