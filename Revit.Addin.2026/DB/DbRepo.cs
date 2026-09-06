using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Revit.Addin._2026.DB
{
    public sealed class DbRepo
    {
        private readonly string _cs;

        public DbRepo(string connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            _cs = connectionString;
        }

        public List<(int ProjectId, string Name)> GetProjects()
        {
            var list = new List<(int, string)>();

            const string sql = @"
SELECT ProjectId, Name
FROM Projects
ORDER BY Name;";

            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();

                using (var cmd = new SqlCommand(sql, conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = r.GetInt32(0);
                        string name = r.IsDBNull(1) ? "" : r.GetString(1);
                        list.Add((id, name));
                    }
                }
            }

            return list;
        }

        public Dictionary<string, List<(string ParamName, string Value)>> GetParamMapByProject(int projectId)
        {
            var result = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);

            const string sql = @"
SELECT o.Name AS ObjectName,
       pa.Name AS ParamName,
       pop.Value
FROM ProjectObjectParameters pop
JOIN Objects o     ON o.ObjectEntityId = pop.ObjectEntityId
JOIN Parameters pa ON pa.ParameterId   = pop.ParameterId
WHERE pop.ProjectId = @ProjectId;";

            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectId", projectId);

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string objectName = r.IsDBNull(0) ? "" : r.GetString(0);
                            string paramName = r.IsDBNull(1) ? "" : r.GetString(1);
                            string value = r.IsDBNull(2) ? "" : r.GetString(2);

                            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(paramName))
                                continue;

                            List<(string, string)> list;
                            if (!result.TryGetValue(objectName, out list))
                            {
                                list = new List<(string, string)>();
                                result[objectName] = list;
                            }

                            list.Add((paramName, value));
                        }
                    }
                }
            }

            return result;
        }
    }
}
