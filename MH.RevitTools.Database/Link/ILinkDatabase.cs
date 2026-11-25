using System.Collections.Generic;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// Everything the add-in reads from the MH database, and nothing it could write.
    /// <see cref="SqlLinkDatabase"/> reads MH_Database's <c>revit</c> views; <see cref="FakeLinkDatabase"/> holds
    /// data in memory for tests. Every method throws <see cref="LinkDatabaseException"/> when the
    /// database can't be read.
    /// </summary>
    public interface ILinkDatabase
    {
        /// <summary>All projects, for the dialog that links a model to its project.</summary>
        IReadOnlyList<LinkProject> GetProjects();

        /// <summary>The dictionary's parameters that go to Revit, with their categories; the same for every project.</summary>
        IReadOnlyList<LinkParameter> GetParameters();

        /// <summary>
        /// The project's change stamp: it moves whenever anything the link reads for that project
        /// changes. Cheap, so the change check can ask every minute. Null if there is no such project.
        /// </summary>
        long? GetStamp(int projectId);

        /// <summary>
        /// The parameter dictionary plus the project's values, its materials and what its objects are made of, all read at
        /// one stamp.
        /// Null if there is no such project.
        /// </summary>
        LinkProjectData GetProjectData(int projectId);
    }
}
