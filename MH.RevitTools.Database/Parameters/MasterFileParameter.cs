using System;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>One parameter in the master shared parameter file.</summary>
    public sealed class MasterFileParameter
    {
        public MasterFileParameter(Guid guid, string name, string dataType, string group)
        {
            Guid = guid;
            Name = name ?? "";
            DataType = dataType ?? "";
            Group = group ?? "";
        }

        public Guid Guid { get; }

        /// <summary>The name Revit shows.</summary>
        public string Name { get; }

        /// <summary>Revit's type name in the file, e.g. TEXT or LENGTH.</summary>
        public string DataType { get; }

        /// <summary>The file group's name, e.g. "BAU-GEO Bauteilinformation - Geometrie"; empty if unknown.</summary>
        public string Group { get; }
    }
}
