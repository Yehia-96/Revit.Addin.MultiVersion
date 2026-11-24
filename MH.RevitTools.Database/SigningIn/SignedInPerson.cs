using System;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>Who signed in, and to which database. Kept in memory until Revit closes, never saved. Immutable.</summary>
    public sealed class SignedInPerson
    {
        private readonly string _databaseKey;
        private readonly string _enteredPassword;

        public SignedInPerson(string name, string role, ConnectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A name is required.", nameof(name));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            Name = OneLine(name);
            Role = OneLine(role);
            Where = DatabaseLabel.For(settings);
            _databaseKey = PullCache.KeyFor(settings);
            _enteredPassword = EnteredIn(settings);
        }

        public string Name { get; }

        /// <summary>The Studio role, e.g. Admin; empty when the database didn't say.</summary>
        public string Role { get; }

        /// <summary>The database, e.g. "CADSQL · MH_Database".</summary>
        public string Where { get; }

        /// <summary>
        /// Whether the sign-in was made with this connection: the same database, and the same password, where that was entered
        /// on this PC. With a new one it has to be made anew.
        /// </summary>
        internal bool IsFor(ConnectionSettings settings) =>
            string.Equals(_databaseKey, PullCache.KeyFor(settings), StringComparison.Ordinal)
            && string.Equals(_enteredPassword, EnteredIn(settings), StringComparison.Ordinal);

        private static string EnteredIn(ConnectionSettings settings) => settings.HasEnteredPassword ? settings.Password : null;

        // As it's shown: a name comes from the database, and is shown in buttons and headlines.
        private static string OneLine(string text) =>
            string.Join(" ", (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
