using System;
using System.IO;
using System.Text.RegularExpressions;
using MH.RevitTools.Database.Connection;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>
/// A LocalDB database of the tests' own: created from a script, dropped afterwards. Never MH_Database.
/// </summary>
public sealed class ThrowawayDatabase : IDisposable
{
    private const string Server = @"(localdb)\MSSQLLocalDB";

    /// <param name="scriptFiles">Scripts in the tests' Link folder, run in order; none for an empty database.</param>
    public ThrowawayDatabase(params string[] scriptFiles)
    {
        Name = "MHRevitLinkTests_" + Guid.NewGuid().ToString("N")[..12];
        Run("master", "CREATE DATABASE [" + Name + "];");
        try
        {
            foreach (string scriptFile in scriptFiles)
                Run(Name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Link", scriptFile)));
        }
        catch
        {
            // xUnit never disposes a fixture whose constructor threw, so drop the database here.
            Dispose();
            throw;
        }
    }

    public string Name { get; }

    public ConnectionSettings Settings => new(Server, Name, true, null, null, true, false);

    /// <summary>Runs more SQL in this database, e.g. to break it on purpose.</summary>
    public void Execute(string script) => Run(Name, script);

    /// <summary>
    /// Runs one query as a database user of this database (EXECUTE AS USER) and returns its first value:
    /// to see what a user with only certain grants can and can't read.
    /// </summary>
    public object QueryAs(string user, string query)
    {
        using var connection = Open(Name);
        using var command = new SqlCommand(
            "EXECUTE AS USER = N'" + user.Replace("'", "''") + "'; " + query + " REVERT;", connection) { CommandTimeout = 60 };
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqlConnection.ClearAllPools();
        Run("master", "IF DB_ID(N'" + Name + "') IS NOT NULL BEGIN "
            + "ALTER DATABASE [" + Name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + Name + "]; END");
    }

    // Unpooled, so nothing outlives the test and an EXECUTE AS ends with the connection.
    private static SqlConnection Open(string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Pooling = false,
            ConnectTimeout = 60,
        };
        var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void Run(string database, string script)
    {
        using var connection = Open(database);
        foreach (string batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            command.ExecuteNonQuery();
        }
    }
}

/// <summary>The revit views over stand-in tables with known rows (Link/StandInTables.sql, Link/RevitViews.sql).</summary>
public sealed class StandInDatabaseFixture : IDisposable
{
    public ThrowawayDatabase Database { get; } = new(StandInScripts);

    public static string[] StandInScripts => new[] { "StandInTables.sql", "RevitViews.sql" };

    public void Dispose() => Database.Dispose();
}
