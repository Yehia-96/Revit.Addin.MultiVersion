using System;
using System.IO;
using System.Reflection;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>Puts connection failures into plain words. The technical side goes in the details.</summary>
    internal static class ConnectionProblems
    {
        /// <summary>
        /// The driver itself failed to load or start; no server involved. Must not touch SqlClient
        /// types: it runs exactly when SqlClient may be broken.
        /// </summary>
        internal static string DescribeDriverFailure(Exception error)
        {
            Exception e = Unwrap(error);
            if (e is PlatformNotSupportedException)
                return "The wrong build of the database driver loaded (the placeholder for other platforms). "
                    + "The add-in folder needs the Windows build of Microsoft.Data.SqlClient.dll.";
            if (e is DllNotFoundException)
                return "The database driver's native part, Microsoft.Data.SqlClient.SNI.dll, couldn't be loaded.";
            if (e is FileNotFoundException notFound)
                return "A file the database driver needs is missing: " + (notFound.FileName ?? FirstLine(notFound.Message)) + ".";
            if (e is FileLoadException loadFailed)
                return "A file the database driver needs couldn't be loaded (blocked, or another version is already loaded): "
                    + (loadFailed.FileName ?? FirstLine(loadFailed.Message)) + ".";
            if (e is BadImageFormatException badImage)
                return "A database driver file is damaged or built for another platform: "
                    + (badImage.FileName ?? FirstLine(badImage.Message)) + ".";
            return "Couldn't connect: " + FirstLine(e.Message);
        }

        /// <summary>The server, or the way to it, answered with an error.</summary>
        internal static string DescribeSqlError(int number, string message, ConnectionSettings settings, int timeoutSeconds)
        {
            // Encryption errors carry the operating system's error code, so go by the message. Name the
            // setting for whoever sets up the PC, but don't nudge users to switch the check off.
            if (message != null && message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The server's certificate isn't trusted on this PC, so the connection was stopped. "
                    + "The certificate setting in the connection file (\"trustServerCertificate\") has to match "
                    + "how the server is set up; the installer writes it.";

            switch (number)
            {
                case -2:
                    return settings.Server + " didn't answer within " + timeoutSeconds + " seconds.";
                case 18456:
                    return settings.WindowsSignIn
                        ? "The server didn't accept your Windows account."
                        : "The server refused the login \"" + settings.Login + "\". Check the login and password in the connection file.";
                case 4060:
                    return "The database \"" + settings.Database + "\" doesn't exist on " + settings.Server
                        + ", or the login can't open it.";
                case 207:    // invalid column name
                case 208:    // invalid object name
                    return "The database's revit views are missing or don't match this add-in (" + FirstLine(message) + ").";
                case -1:     // generic network error
                case 2:      // server not found or not accessible
                case 26:     // error locating server/instance
                case 40:     // could not open a connection
                case 53:     // network path not found
                case 10060:  // connection timed out
                case 10061:  // connection refused
                case 11001:  // host not known
                    return "Can't reach " + settings.Server + ". Check the server name, and the VPN if you're out of the office.";
                default:
                    return "The server said: " + FirstLine(message);
            }
        }

        private static Exception Unwrap(Exception error)
        {
            Exception e = error;
            while (e.InnerException != null
                && (e is TypeInitializationException || e is TargetInvocationException || e is AggregateException))
            {
                e = e.InnerException;
            }
            return e;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "(no message)";
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            return (end < 0 ? text : text.Substring(0, end)).Trim();
        }
    }
}
