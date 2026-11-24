using System;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// What a connection test found. On failure <see cref="Problem"/> says what went wrong in plain
    /// words and <see cref="Details"/> holds the technical side. Neither ever holds the password.
    /// </summary>
    public sealed class ConnectionTestResult
    {
        private const string PasswordMask = "•••";

        private ConnectionTestResult(
            bool succeeded,
            string server,
            string database,
            string signedInAs,
            string serverVersion,
            TimeSpan elapsed,
            string problem,
            string details,
            bool isLoginRefused)
        {
            Succeeded = succeeded;
            Server = server;
            Database = database;
            SignedInAs = signedInAs;
            ServerVersion = serverVersion;
            Elapsed = elapsed;
            Problem = problem;
            Details = details;
            IsLoginRefused = isLoginRefused;
        }

        public bool Succeeded { get; }

        public string Server { get; }

        /// <summary>The database the server put us in when connected; otherwise the one the file asks for.</summary>
        public string Database { get; }

        /// <summary>The login the server knows us by, e.g. <c>PC\name</c> with Windows sign-in; null on failure.</summary>
        public string SignedInAs { get; }

        /// <summary>E.g. "SQL Server 2019 (15.0.4382.1)"; null on failure.</summary>
        public string ServerVersion { get; }

        public TimeSpan Elapsed { get; }

        /// <summary>What went wrong, in plain words; null on success.</summary>
        public string Problem { get; }

        /// <summary>The technical side of a failure, for a details section; null on success.</summary>
        public string Details { get; }

        /// <summary>The server answered, but doesn't take the login with this password.</summary>
        public bool IsLoginRefused { get; }

        internal static ConnectionTestResult Connected(
            ConnectionSettings settings, string database, string signedInAs, string serverVersion, TimeSpan elapsed) =>
            new ConnectionTestResult(true, settings.Server, database, signedInAs, serverVersion, elapsed, null, null, false);

        internal static ConnectionTestResult Failed(
            ConnectionSettings settings, string problem, string details, TimeSpan elapsed, bool isLoginRefused) =>
            new ConnectionTestResult(false, settings.Server, settings.Database, null, null, elapsed,
                Hide(problem, settings.Password), Hide(details, settings.Password), isLoginRefused);

        // SqlClient's messages don't quote the password, but a dialog is the wrong place to find out
        // the hard way that one does.
        private static string Hide(string text, string password)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(password))
                return text;
            return text.Replace(password, PasswordMask);
        }
    }
}
