using System;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// What the connection file says: which SQL Server and database to use, and how to sign in to
    /// it. Immutable. <see cref="ToString"/> is safe to show; it never includes the password.
    /// </summary>
    public sealed class ConnectionSettings
    {
        public ConnectionSettings(
            string server,
            string database,
            bool windowsSignIn,
            string login,
            string password,
            bool encrypt,
            bool trustServerCertificate)
        {
            if (string.IsNullOrWhiteSpace(server))
                throw new ArgumentException("A server is required.", nameof(server));
            if (string.IsNullOrWhiteSpace(database))
                throw new ArgumentException("A database is required.", nameof(database));
            if (!windowsSignIn && string.IsNullOrWhiteSpace(login))
                throw new ArgumentException("A login is required unless Windows sign-in is used.", nameof(login));
            if (!windowsSignIn && string.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required unless Windows sign-in is used.", nameof(password));

            Server = server.Trim();
            Database = database.Trim();
            WindowsSignIn = windowsSignIn;
            Login = windowsSignIn ? null : login.Trim();
            Password = windowsSignIn ? null : password;
            Encrypt = encrypt;
            TrustServerCertificate = trustServerCertificate;
        }

        /// <summary>The SQL Server, e.g. <c>CADSQL.mh-frlg.local,1433</c> or <c>(localdb)\MSSQLLocalDB</c>.</summary>
        public string Server { get; }

        public string Database { get; }

        /// <summary>
        /// Sign in as the Windows user running Revit instead of with <see cref="Login"/>. Only for a
        /// developer PC: the office PCs aren't in a domain.
        /// </summary>
        public bool WindowsSignIn { get; }

        /// <summary>The read-only SQL login; null with Windows sign-in.</summary>
        public string Login { get; }

        /// <summary>The login's password; null with Windows sign-in. Never show or log it.</summary>
        public string Password { get; }

        public bool Encrypt { get; }

        public bool TrustServerCertificate { get; }

        /// <summary>How the connection is protected, in plain words, e.g. "on, server certificate checked".</summary>
        public string DescribeEncryption()
        {
            if (!Encrypt)
                return "off (only the sign-in is encrypted)";
            return TrustServerCertificate
                ? "on, server certificate not checked"
                : "on, server certificate checked";
        }

        public override string ToString() => Database + " on " + Server;
    }
}
