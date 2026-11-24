using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// Passes everything on to a database, and tells when its server refuses the connection's password, so that nothing
    /// tries that password again. The failure itself still reaches the caller.
    /// </summary>
    public sealed class PasswordWatch : ILinkDatabase
    {
        private readonly ILinkDatabase _database;
        private readonly Action _refused;

        /// <param name="database">Who answers.</param>
        /// <param name="refused">Called on the asking thread, before the failure is passed on.</param>
        public PasswordWatch(ILinkDatabase database, Action refused)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _refused = refused ?? throw new ArgumentNullException(nameof(refused));
        }

        public IReadOnlyList<LinkProject> GetProjects() => Ask(_database.GetProjects);

        public IReadOnlyList<LinkParameter> GetParameters() => Ask(_database.GetParameters);

        public long? GetStamp(int projectId) => Ask(() => _database.GetStamp(projectId));

        public LinkProjectData GetProjectData(int projectId) => Ask(() => _database.GetProjectData(projectId));

        private T Ask<T>(Func<T> question)
        {
            try
            {
                return question();
            }
            catch (LinkDatabaseException ex) when (ex.IsLoginRefused)
            {
                _refused();
                throw;
            }
        }
    }
}
