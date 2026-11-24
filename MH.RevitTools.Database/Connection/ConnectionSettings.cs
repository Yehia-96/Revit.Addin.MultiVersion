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
            : this(server, database, windowsSignIn, login, password, false, encrypt, trustServerCertificate)
        {
            if (!windowsSignIn && string.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required unless Windows sign-in is used.", nameof(password));
        }

        private ConnectionSettings(
            string server,
            string database,
            bool windowsSignIn,
            string login,
            string password,
            bool hasEnteredPassword,
            bool encrypt,
            bool trustServerCertificate)
        {
            if (string.IsNullOrWhiteSpace(server))
                throw new ArgumentException("A server is required.", nameof(server));
            if (string.IsNullOrWhiteSpace(database))
                throw new ArgumentException("A database is required.", nameof(database));
            if (!windowsSignIn && string.IsNullOrWhiteSpace(login))
                throw new ArgumentException("A login is required unless Windows sign-in is used.", nameof(login));

            Server = server.Trim();
            Database = database.Trim();
            WindowsSignIn = windowsSignIn;
            Login = windowsSignIn ? null : login.Trim();
            Password = windowsSignIn || string.IsNullOrEmpty(password) ? null : password;
            HasEnteredPassword = hasEnteredPassword && Password != null;
            Encrypt = encrypt;
            TrustServerCertificate = trustServerCertificate;
        }

        /// <summary>
        /// A connection whose file doesn't hold the password: the person enters it once, and
        /// <see cref="WithPassword"/> completes it. Until then it can't be used to connect.
        /// </summary>
        public static ConnectionSettings WithoutPassword(
            string server, string database, string login, bool encrypt, bool trustServerCertificate) =>
            new ConnectionSettings(server, database, false, login, null, false, encrypt, trustServerCertificate);

        /// <summary>The same connection with the password the person entered.</summary>
        public ConnectionSettings WithPassword(string password)
        {
            if (WindowsSignIn)
                throw new InvalidOperationException("Windows sign-in takes no password.");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required.", nameof(password));
            return new ConnectionSettings(Server, Database, false, Login, password, true, Encrypt, TrustServerCertificate);
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

        /// <summary>
        /// The login's password; null with Windows sign-in, and while the person hasn't entered it. Never show or log it.
        /// </summary>
        public string Password { get; }

        /// <summary>The file doesn't hold the password and the person hasn't entered it: there's nothing to connect with.</summary>
        public bool NeedsPassword => !WindowsSignIn && Password == null;

        /// <summary>The person entered the password; it isn't from the connection file.</summary>
        public bool HasEnteredPassword { get; }

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
