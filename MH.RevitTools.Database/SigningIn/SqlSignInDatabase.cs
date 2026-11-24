using System;
using System.Data;
using System.Data.SqlTypes;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// Calls the two sign-in procedures in MH_Database's <c>revit</c> schema (Studio's migration AddRevitSignIn), and
    /// nothing else. Each call has a connection of its own, outside any transaction, as <c>revit.CheckSignIn</c> asks,
    /// and is never repeated: one that timed out may have been counted. The connection is always encrypted, as the hash
    /// is as good as the password to whoever could read it on the way.
    /// </summary>
    public sealed class SqlSignInDatabase : ISignInDatabase
    {
        internal const int TimeoutSeconds = 15;

        internal const int NoSuchProcedure = 2812;
        internal const int PermissionDenied = 229;
        internal const int SecretMissing = 51001;
        private const int TimedOut = -2;

        // As the procedures declare them. A longer name is cut here, and then it's nobody's: names end at 100.
        private const int ParameterLength = 256;

        private readonly ConnectionSettings _settings;
        private readonly string _connectionString;

        public SqlSignInDatabase(ConnectionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionString = ConnectionStringFactory.Build(settings, TimeoutSeconds, pooling: false, alwaysEncrypt: true);
        }

        public string GetSalt(string userName) =>
            Call("revit.GetSignInSalt", false,
                command => AddText(command, "@Username", userName),
                reader => reader.GetString(reader.GetOrdinal("Salt")));

        public SignInAnswer Check(string userName, string hash) =>
            Call("revit.CheckSignIn", true,
                command =>
                {
                    AddText(command, "@Username", userName);
                    AddText(command, "@Hash", hash);
                },
                reader => new SignInAnswer(
                    reader.GetBoolean(reader.GetOrdinal("IsValid")),
                    TextOrNull(reader, "DisplayName"),
                    TextOrNull(reader, "Role"),
                    reader.GetInt32(reader.GetOrdinal("WaitSeconds"))));

        /// <summary>What an error from the server means, in plain words.</summary>
        /// <param name="isTry">The call was one the database counts.</param>
        internal static string Describe(int number, string message, ConnectionSettings settings, bool isTry)
        {
            switch (number)
            {
                case NoSuchProcedure:
                    return "This database doesn't know the sign-in from Revit yet. It comes with a newer MH Database Studio.";
                case PermissionDenied:
                    return "The connection file's login isn't allowed to ask the database for the sign-in.";
                case SecretMissing:
                    return "Nobody can sign in to Revit at the moment, as something is missing in the database. Tell whoever looks after MH Database Studio.";
                case TimedOut:
                    return ConnectionProblems.DescribeSqlError(number, message, settings, TimeoutSeconds)
                        + (isTry ? " The try may have been counted." : "");
                default:
                    return ConnectionProblems.DescribeSqlError(number, message, settings, TimeoutSeconds);
            }
        }

        private T Call<T>(string procedure, bool isTry, Action<SqlCommand> addParameters, Func<SqlDataReader, T> read)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = procedure;
                    command.CommandTimeout = TimeoutSeconds;
                    addParameters(command);
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new LinkDatabaseException("The database didn't answer the sign-in.");
                        return read(reader);
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new LinkDatabaseException(Describe(ex.Number, ex.Message, _settings, isTry), ex,
                    ConnectionProblems.IsLoginRefused(ex.Number, ex.Message, _settings));
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is SqlNullValueException || ex is IndexOutOfRangeException)
            {
                throw new LinkDatabaseException("The database's sign-in doesn't match this add-in. The add-in needs an update.", ex);
            }
            catch (InvalidOperationException ex)
            {
                // E.g. the connection broke off half-way.
                throw new LinkDatabaseException("Couldn't ask the database: " + ex.Message, ex);
            }
        }

        private static void AddText(SqlCommand command, string name, string value) =>
            command.Parameters.Add(name, SqlDbType.NVarChar, ParameterLength).Value = value ?? "";

        private static string TextOrNull(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }
}
