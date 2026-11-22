using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>Where a database parameter stands in the model, on one of its categories.</summary>
    public enum ParameterState
    {
        /// <summary>The model has it: a shared parameter with its GUID is bound to the category.</summary>
        Ready,

        /// <summary>The model lacks it, and Add can bind it from the master file.</summary>
        Missing,

        /// <summary>The model lacks it, and the add-in can't add it; the note says why.</summary>
        CantAdd,

        /// <summary>The model has a parameter of the same name with another GUID, or not shared at all.</summary>
        OtherGuid,

        /// <summary>Added just now.</summary>
        Added,
    }

    /// <summary>One line of Check parameters: a database parameter on one of its categories.</summary>
    public sealed class ParameterCheckRow
    {
        public ParameterCheckRow(
            LinkParameter parameter, string category, ParameterState state, string nameInRevit, string note,
            ModelBinding clash = null, bool canBeReplaced = false)
        {
            Parameter = parameter;
            Category = category ?? "";
            State = state;
            NameInRevit = nameInRevit ?? "";
            Note = note ?? "";
            Clash = clash;
            CanBeReplaced = canBeReplaced && clash != null;
        }

        /// <summary>For Other GUID: the model's parameter of the same name; otherwise null.</summary>
        public ModelBinding Clash { get; }

        /// <summary>For Other GUID: whether Replace can put the database's parameter in its place.</summary>
        public bool CanBeReplaced { get; }

        public LinkParameter Parameter { get; }

        /// <summary>The BuiltInCategory name, e.g. OST_GenericModel; empty when the parameter has none.</summary>
        public string Category { get; }

        public ParameterState State { get; }

        /// <summary>The name Revit shows: the model's name when it has the parameter, else the database's.</summary>
        public string NameInRevit { get; }

        /// <summary>The dictionary's full name, e.g. BAU-GEO-Dicke.</summary>
        public string Key => ParameterCheck.KeyOf(Parameter);

        /// <summary>A short explanation under the name; empty when there's nothing to add.</summary>
        public string Note { get; }

        public bool IsReady => State == ParameterState.Ready || State == ParameterState.Added;

        public override string ToString() => NameInRevit + " (" + Category + "): " + State;
    }
}
