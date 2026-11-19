using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Link
{
    /// <summary>Everything the link reads for one project, read together at one <see cref="Stamp"/>.</summary>
    public sealed class LinkProjectData
    {
        public LinkProjectData(
            int projectId,
            long stamp,
            IEnumerable<LinkParameter> parameters,
            IEnumerable<LinkObjectValue> objectValues,
            IEnumerable<LinkMaterial> materials,
            IEnumerable<LinkMaterialValue> materialValues)
        {
            ProjectId = projectId;
            Stamp = stamp;
            Parameters = Freeze(parameters);
            ObjectValues = Freeze(objectValues);
            Materials = Freeze(materials);
            MaterialValues = Freeze(materialValues);
        }

        public int ProjectId { get; }

        /// <summary>The project's change stamp when this was read.</summary>
        public long Stamp { get; }

        /// <summary>The whole dictionary of parameters that go to Revit; the same for every project.</summary>
        public IReadOnlyList<LinkParameter> Parameters { get; }

        public IReadOnlyList<LinkObjectValue> ObjectValues { get; }

        public IReadOnlyList<LinkMaterial> Materials { get; }

        public IReadOnlyList<LinkMaterialValue> MaterialValues { get; }

        /// <summary>The same data at another stamp, as when the database changed.</summary>
        public LinkProjectData WithStamp(long stamp) =>
            new LinkProjectData(ProjectId, stamp, Parameters, ObjectValues, Materials, MaterialValues);

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) =>
            new ReadOnlyCollection<T>((items ?? Enumerable.Empty<T>()).ToList());
    }
}
