using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// One dictionary parameter that goes to Revit. In Revit the add-in finds it by
    /// <see cref="Guid"/>, never by name.
    /// </summary>
    public sealed class LinkParameter
    {
        public LinkParameter(
            int parameterId,
            Guid guid,
            string revitName,
            string setCode,
            string groupCode,
            string name,
            string dataType,
            string unit,
            LinkTarget target,
            IEnumerable<string> categories)
        {
            ParameterId = parameterId;
            Guid = guid;
            RevitName = revitName ?? "";
            SetCode = setCode ?? "";
            GroupCode = groupCode ?? "";
            Name = name ?? "";
            DataType = dataType ?? "";
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
            Target = target;
            Categories = new ReadOnlyCollection<string>((categories ?? Enumerable.Empty<string>()).ToList());
        }

        public int ParameterId { get; }

        /// <summary>
        /// The GUID in the master shared parameter file. Studio gives every parameter one, and each
        /// database its own; empty only if a view returns none.
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        /// The name Revit shows, e.g. "Dicke", with a suffix only where plain names clash on one category,
        /// e.g. "Beschreibung (Bauteilinformation)". The plain name until Studio has settled one.
        /// </summary>
        public string RevitName { get; }

        public string SetCode { get; }

        public string GroupCode { get; }

        /// <summary>The dictionary name. With the set and group codes it is the lookup key, e.g. BAU-GEO-Dicke.</summary>
        public string Name { get; }

        /// <summary>String, Integer, Double, Boolean, Date, Url or Coordinates.</summary>
        public string DataType { get; }

        /// <summary>Spelled like the Excel's Einheit (m, cm, mm, m2, m3, grad); null when there is none.</summary>
        public string Unit { get; }

        public LinkTarget Target { get; }

        /// <summary>The Revit categories it goes on, as BuiltInCategory names such as OST_GenericModel.</summary>
        public IReadOnlyList<string> Categories { get; }
    }
}
