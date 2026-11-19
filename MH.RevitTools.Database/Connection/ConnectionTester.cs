using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// Checks that the database can be reached: opens a connection, runs one read-only query and
    /// reports who the server thinks we are. A failed connection doesn't throw; the result says
    /// what went wrong.
    /// </summary>
    public static class ConnectionTester
    {
        /// <summary>How long the ribbon button waits, for connecting and for the query alike.</summary>
        public const int DefaultTimeoutSeconds = 5;

        // Reads no table, so any login can run it: the database we landed in, who we are, and the
        // server's version.
        private const string Query =
            "SELECT DB_NAME(), SUSER_SNAME(), CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), @@VERSION;";

        public static ConnectionTestResult Run(ConnectionSettings settings, int timeoutSeconds)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (timeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "The timeout must be at least one second.");

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                return Connect(settings, timeoutSeconds, watch);
            }
            catch (Exception ex)
            {
                // Server errors are handled in Connect. Everything else lands here: above all the
                // driver itself failing to load or start (a missing or wrong build of
                // Microsoft.Data.SqlClient, its native part), which is what the test is for, but also
                // odd cases such as an empty answer. Report them all; don't throw.
                return ConnectionTestResult.Failed(settings, ConnectionProblems.DescribeDriverFailure(ex),
                    ExceptionText.Describe(ex, null), watch.Elapsed);
            }
        }

        // Separate and never inlined: if Microsoft.Data.SqlClient can't load, that happens when Run
        // calls this method, inside Run's try, rather than when Run itself is compiled.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConnectionTestResult Connect(ConnectionSettings settings, int timeoutSeconds, Stopwatch watch)
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionStringFactory.Build(settings, timeoutSeconds, pooling: false)))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = Query;
                    command.CommandTimeout = timeoutSeconds;
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException("The server answered the test query with no row.");

                        string database = TextOrNull(reader, 0) ?? settings.Database;
                        string signedInAs = TextOrNull(reader, 1) ?? "";
                        string version = ServerVersionText.Describe(TextOrNull(reader, 3), TextOrNull(reader, 2));
                        return ConnectionTestResult.Connected(settings, database, signedInAs, version, watch.Elapsed);
                    }
                }
            }
            catch (SqlException ex)
            {
                return ConnectionTestResult.Failed(settings,
                    ConnectionProblems.DescribeSqlError(ex.Number, ex.Message, settings, timeoutSeconds),
                    ExceptionText.Describe(ex, "SQL error " + ex.Number + ", state " + ex.State + ", class " + ex.Class),
                    watch.Elapsed);
            }
        }

        private static string TextOrNull(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
