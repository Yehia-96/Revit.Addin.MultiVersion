using System;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// Turns <see cref="ConnectionSettings"/> into a SqlClient connection string. The result holds
    /// the password: pass it straight to a connection and never show or log it.
    /// </summary>
    internal static class ConnectionStringFactory
    {
        /// <summary>How the add-in shows up in the server's list of sessions.</summary>
        internal const string ApplicationName = "MH.RevitTools";

        /// <param name="pooling">
        /// False for a one-off check: the session then really ends when the connection closes, and
        /// the next check signs in afresh instead of reusing a pooled session.
        /// </param>
        internal static string Build(ConnectionSettings settings, int timeoutSeconds, bool pooling)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (timeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "The timeout must be at least one second.");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = settings.Server,
                InitialCatalog = settings.Database,
                IntegratedSecurity = settings.WindowsSignIn,
                Encrypt = settings.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
                TrustServerCertificate = settings.TrustServerCertificate,
                ConnectTimeout = timeoutSeconds,
                ApplicationName = ApplicationName,
                PersistSecurityInfo = false,
                Pooling = pooling,
            };
            if (!settings.WindowsSignIn)
            {
                builder.UserID = settings.Login;
                builder.Password = settings.Password;
            }
            return builder.ConnectionString;
        }
    }
}
