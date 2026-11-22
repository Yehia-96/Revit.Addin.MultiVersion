using System;
using System.Linq;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>Names the database for the pane's header, e.g. "CADSQL · MH_Database".</summary>
    public static class DatabaseLabel
    {
        public static string For(ConnectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            return ShortServer(settings.Server) + " · " + settings.Database;
        }

        /// <summary>
        /// The server without its port or domain: "CADSQL.mh-frlg.local,1433" is "CADSQL". A LocalDB or named
        /// instance ("(localdb)\MSSQLLocalDB") and an IP address stay as they are.
        /// </summary>
        internal static string ShortServer(string server)
        {
            string name = (server ?? "").Trim();
            if (name.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);
            int comma = name.IndexOf(',');
            if (comma >= 0)
                name = name.Substring(0, comma).Trim();
            if (name.StartsWith("(", StringComparison.Ordinal) || name.IndexOf('\\') >= 0 || IsAddress(name))
                return name;
            int dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static bool IsAddress(string name) =>
            name.Length > 0 && name.All(c => char.IsDigit(c) || c == '.' || c == ':' || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
            && name.Any(char.IsDigit) && (name.IndexOf('.') >= 0 || name.IndexOf(':') >= 0);
    }
}
