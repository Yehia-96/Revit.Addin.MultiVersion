using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using MH.RevitTools.Database.Connection;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// Reads the <c>revit</c> views in MH_Database, and nothing else. Read-only by construction: it only
    /// ever runs the SELECTs below, with the project id passed as a parameter. The views build the
    /// Bauteil-IDs and Material-IDs and take the change stamp from Studio's history (their SQL is
    /// <c>MH.RevitTools.Database.Tests/Link/RevitViews.sql</c>). Meant for a background thread. Each
    /// call opens its own pooled connection, so one instance can be shared.
    /// </summary>
    public sealed class SqlLinkDatabase : ILinkDatabase
    {
        // Generous: these run in the background, and a project is a few thousand rows at most.
        internal const int TimeoutSeconds = 30;
        private const int MaxReadAttempts = 3;

        // SQL Server chose this read as a deadlock victim. Reading again is the documented remedy.
        private const int DeadlockVictim = 1205;

        private const string ProjectsQuery =
            "SELECT ProjectId, ProjectNumber, ProjectSubNumber, Name FROM revit.Projects ORDER BY ProjectNumber, ProjectSubNumber;";

        private const string StampQuery =
            "SELECT Stamp FROM revit.ProjectStamps WHERE ProjectId = @ProjectId;";

        // One batch, so the dictionary and the project's rows come in one round trip. The stamp is read
        // first and last: if it moved, something changed half-way and the read is repeated.
        private const string ProjectDataQuery =
            StampQuery
            + " SELECT ParameterId, Category FROM revit.ParameterCategories;"
            + " SELECT ParameterId, Guid, RevitName, SetCode, GroupCode, Name, DataType, Unit, Target FROM revit.Parameters;"
            + " SELECT BauteilId, ParameterId, Value, ChangedAtUtc, ChangedBy FROM revit.ObjectValues WHERE ProjectId = @ProjectId;"
            + " SELECT MaterialId, MaterialName, MaterialGroupCode, MaterialGroup, SurfacePattern, CutPattern FROM revit.Materials WHERE ProjectId = @ProjectId;"
            + " SELECT MaterialId, ParameterId, Value, ChangedAtUtc, ChangedBy FROM revit.MaterialValues WHERE ProjectId = @ProjectId;"
            + " " + StampQuery;

        private readonly ConnectionSettings _settings;
        private readonly string _connectionString;

        public SqlLinkDatabase(ConnectionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionString = ConnectionStringFactory.Build(settings, TimeoutSeconds, pooling: true);
        }

        public IReadOnlyList<LinkProject> GetProjects() => Query(ProjectsQuery, null, ReadProjects);

        public long? GetStamp(int projectId) => Query(StampQuery, projectId, ReadStamp);

        public LinkProjectData GetProjectData(int projectId)
        {
            for (int attempt = 1; attempt <= MaxReadAttempts; attempt++)
            {
                try
                {
                    (bool consistent, LinkProjectData data) = Query(ProjectDataQuery, projectId, reader => ReadProjectData(reader, projectId));
                    if (consistent)
                        return data;
                }
                catch (LinkDatabaseException ex) when (attempt < MaxReadAttempts && IsDeadlockVictim(ex))
                {
                    // Read again; a deadlock on the last attempt is reported like any other error.
                }
            }
            throw new LinkDatabaseException("The project kept changing while it was being read. Try again in a moment.");
        }

        internal static LinkTarget ParseTarget(string text, int parameterId)
        {
            if (string.Equals(text, "Object", StringComparison.OrdinalIgnoreCase))
                return LinkTarget.Object;
            if (string.Equals(text, "Material", StringComparison.OrdinalIgnoreCase))
                return LinkTarget.Material;
            throw new LinkDatabaseException("Parameter " + parameterId + " has the Target \"" + text
                + "\". This add-in knows only Object and Material.");
        }

        private static bool IsDeadlockVictim(LinkDatabaseException error) =>
            error.InnerException is SqlException sql && sql.Number == DeadlockVictim;

        private T Query<T>(string sql, int? projectId, Func<SqlDataReader, T> read)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = TimeoutSeconds;
                    if (projectId.HasValue)
                        command.Parameters.Add("@ProjectId", SqlDbType.Int).Value = projectId.Value;
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                        return read(reader);
                }
            }
            catch (SqlException ex)
            {
                throw new LinkDatabaseException(
                    ConnectionProblems.DescribeSqlError(ex.Number, ex.Message, _settings, TimeoutSeconds), ex);
            }
            catch (InvalidCastException ex)
            {
                throw new LinkDatabaseException(
                    "The revit views don't match what this add-in reads: a column holds another kind of value.", ex);
            }
            catch (SqlNullValueException ex)
            {
                throw new LinkDatabaseException(
                    "The revit views don't match what this add-in reads: a column that always needs a value is empty.", ex);
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new LinkDatabaseException(
                    "The revit views don't match what this add-in reads: a column is missing.", ex);
            }
            catch (InvalidOperationException ex)
            {
                // E.g. the connection broke off half-way, or no pooled connection came free in time.
                throw new LinkDatabaseException("Couldn't read the database: " + ex.Message, ex);
            }
        }

        private static IReadOnlyList<LinkProject> ReadProjects(SqlDataReader reader)
        {
            int id = reader.GetOrdinal("ProjectId");
            int number = reader.GetOrdinal("ProjectNumber");
            int subNumber = reader.GetOrdinal("ProjectSubNumber");
            int name = reader.GetOrdinal("Name");

            var projects = new List<LinkProject>();
            while (reader.Read())
            {
                projects.Add(new LinkProject(
                    reader.GetInt32(id), reader.GetInt32(number), reader.GetInt32(subNumber), TextOrNull(reader, name)));
            }
            return projects.AsReadOnly();
        }

        private static long? ReadStamp(SqlDataReader reader) =>
            reader.Read() ? reader.GetInt64(reader.GetOrdinal("Stamp")) : (long?)null;

        private static (bool consistent, LinkProjectData data) ReadProjectData(SqlDataReader reader, int projectId)
        {
            long? stampBefore = ReadStamp(reader);
            if (stampBefore == null)
                return (true, null);

            NextResult(reader);
            Dictionary<int, List<string>> categories = ReadCategories(reader);
            NextResult(reader);
            List<LinkParameter> parameters = ReadParameters(reader, categories);
            NextResult(reader);
            List<LinkObjectValue> objectValues = ReadObjectValues(reader);
            NextResult(reader);
            List<LinkMaterial> materials = ReadMaterials(reader);
            NextResult(reader);
            List<LinkMaterialValue> materialValues = ReadMaterialValues(reader);
            NextResult(reader);
            long? stampAfter = ReadStamp(reader);

            if (stampAfter != stampBefore)
                return (false, null);
            return (true, new LinkProjectData(projectId, stampBefore.Value, parameters, objectValues, materials, materialValues));
        }

        private static void NextResult(SqlDataReader reader)
        {
            if (!reader.NextResult())
                throw new LinkDatabaseException("The database answered with fewer tables than the link reads.");
        }

        private static Dictionary<int, List<string>> ReadCategories(SqlDataReader reader)
        {
            int id = reader.GetOrdinal("ParameterId");
            int category = reader.GetOrdinal("Category");

            var byParameter = new Dictionary<int, List<string>>();
            while (reader.Read())
            {
                string name = TextOrNull(reader, category);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int parameterId = reader.GetInt32(id);
                List<string> names;
                if (!byParameter.TryGetValue(parameterId, out names))
                {
                    names = new List<string>();
                    byParameter.Add(parameterId, names);
                }
                names.Add(name.Trim());
            }
            return byParameter;
        }

        private static List<LinkParameter> ReadParameters(SqlDataReader reader, Dictionary<int, List<string>> categories)
        {
            int id = reader.GetOrdinal("ParameterId");
            int guid = reader.GetOrdinal("Guid");
            int revitName = reader.GetOrdinal("RevitName");
            int setCode = reader.GetOrdinal("SetCode");
            int groupCode = reader.GetOrdinal("GroupCode");
            int name = reader.GetOrdinal("Name");
            int dataType = reader.GetOrdinal("DataType");
            int unit = reader.GetOrdinal("Unit");
            int target = reader.GetOrdinal("Target");

            var parameters = new List<LinkParameter>();
            while (reader.Read())
            {
                int parameterId = reader.GetInt32(id);
                List<string> parameterCategories;
                categories.TryGetValue(parameterId, out parameterCategories);
                parameters.Add(new LinkParameter(
                    parameterId,
                    reader.IsDBNull(guid) ? Guid.Empty : reader.GetGuid(guid),
                    TextOrNull(reader, revitName),
                    TextOrNull(reader, setCode),
                    TextOrNull(reader, groupCode),
                    TextOrNull(reader, name),
                    TextOrNull(reader, dataType),
                    TextOrNull(reader, unit),
                    ParseTarget(TextOrNull(reader, target), parameterId),
                    parameterCategories));
            }
            return parameters;
        }

        private static List<LinkObjectValue> ReadObjectValues(SqlDataReader reader)
        {
            int bauteilId = reader.GetOrdinal("BauteilId");
            int parameterId = reader.GetOrdinal("ParameterId");
            int value = reader.GetOrdinal("Value");
            int changedAt = reader.GetOrdinal("ChangedAtUtc");
            int changedBy = reader.GetOrdinal("ChangedBy");

            var values = new List<LinkObjectValue>();
            while (reader.Read())
            {
                values.Add(new LinkObjectValue(
                    TextOrNull(reader, bauteilId), reader.GetInt32(parameterId), TextOrNull(reader, value),
                    UtcOrNull(reader, changedAt), TextOrNull(reader, changedBy)));
            }
            return values;
        }

        private static List<LinkMaterial> ReadMaterials(SqlDataReader reader)
        {
            int materialId = reader.GetOrdinal("MaterialId");
            int name = reader.GetOrdinal("MaterialName");
            int groupCode = reader.GetOrdinal("MaterialGroupCode");
            int group = reader.GetOrdinal("MaterialGroup");
            int surfacePattern = reader.GetOrdinal("SurfacePattern");
            int cutPattern = reader.GetOrdinal("CutPattern");

            var materials = new List<LinkMaterial>();
            while (reader.Read())
            {
                materials.Add(new LinkMaterial(
                    TextOrNull(reader, materialId), TextOrNull(reader, name), TextOrNull(reader, groupCode),
                    TextOrNull(reader, group), TextOrNull(reader, surfacePattern), TextOrNull(reader, cutPattern)));
            }
            return materials;
        }

        private static List<LinkMaterialValue> ReadMaterialValues(SqlDataReader reader)
        {
            int materialId = reader.GetOrdinal("MaterialId");
            int parameterId = reader.GetOrdinal("ParameterId");
            int value = reader.GetOrdinal("Value");
            int changedAt = reader.GetOrdinal("ChangedAtUtc");
            int changedBy = reader.GetOrdinal("ChangedBy");

            var values = new List<LinkMaterialValue>();
            while (reader.Read())
            {
                values.Add(new LinkMaterialValue(
                    TextOrNull(reader, materialId), reader.GetInt32(parameterId), TextOrNull(reader, value),
                    UtcOrNull(reader, changedAt), TextOrNull(reader, changedBy)));
            }
            return values;
        }

        private static string TextOrNull(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

        // The history stores UTC in a datetime2, which comes back without a kind; say what it is.
        private static DateTime? UtcOrNull(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? (DateTime?)null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }
}
