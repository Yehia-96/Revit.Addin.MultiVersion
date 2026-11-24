using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;
using System.Windows.Interop;

namespace Revit.Addin._2027.DatabaseLink.SignIn
{
    /// <summary>
    /// What stands before everything that reads the MH database: its password has to have been entered on this PC, once,
    /// and someone has to have signed in with their Studio account since Revit started. It asks the first time that's
    /// needed. Call it on Revit's main thread, in API context.
    /// </summary>
    internal static class SignInGate
    {
        // As long as the sign-in itself waits for the server.
        private const int PasswordTestSeconds = 15;

        /// <summary>Who is signed in, until Revit closes.</summary>
        public static SignInSession Session { get; } = new SignInSession();

        /// <summary>
        /// The connection to use, with its password and someone signed in. Asks for what's missing; null when that was
        /// cancelled.
        /// </summary>
        public static ConnectionSettings Ensure(UIApplication application, ConnectionFileResult file)
        {
            ConnectionSettings settings = EnsurePassword(application, file);
            return settings == null || Session.PersonFor(settings) != null ? settings : Ask(application, settings);
        }

        /// <summary>The connection with its password, whoever is signed in. Asks for it; null when that was cancelled.</summary>
        public static ConnectionSettings EnsurePassword(UIApplication application, ConnectionFileResult file) =>
            file.Settings.NeedsPassword
                ? AskForPassword(application, file.Settings, file.IsSavedPasswordRefused)
                : file.Settings;

        /// <summary>
        /// Asks for a Studio account, whoever is signed in now. Null when that was cancelled; otherwise the connection to
        /// go on with, as the database password may turn out to be an old one.
        /// </summary>
        public static ConnectionSettings Ask(UIApplication application, ConnectionSettings settings)
        {
            while (settings != null)
            {
                var window = new SignInWindow(new StudioSignIn(new SqlSignInDatabase(settings), settings), settings);
                new WindowInteropHelper(window).Owner = application.MainWindowHandle;
                if (window.ShowDialog() == true && window.Person != null)
                {
                    Session.SignIn(window.Person);
                    return settings;
                }
                if (!window.IsDatabasePasswordRefused)
                    return null;

                // It has been changed since it was entered here: the new one first, then the account again.
                RefusePassword(settings);
                settings = AskForPassword(application, settings, true);
            }
            return null;
        }

        /// <summary>The server refused this connection's password: it isn't used again, and nobody is signed in with it.</summary>
        public static void RefusePassword(ConnectionSettings settings)
        {
            ConnectionFiles.Passwords.Refuse(settings);
            SignOutIfStale();
        }

        /// <summary>
        /// Signs out whoever signed in with another connection than the one that counts now: another database, or a database
        /// password that was refused.
        /// </summary>
        public static void SignOutIfStale()
        {
            ConnectionFileResult file = ConnectionFiles.Read();
            if (Session.Person != null && file.IsLoaded && Session.PersonFor(file.Settings) == null)
                Session.SignOut();
        }

        private static ConnectionSettings AskForPassword(UIApplication application, ConnectionSettings settings, bool wasRefused)
        {
            var window = new PasswordWindow(settings, wasRefused, entered => ConnectionTester.Run(entered, PasswordTestSeconds));
            new WindowInteropHelper(window).Owner = application.MainWindowHandle;
            if (window.ShowDialog() != true || window.Settings == null)
                return null;

            string problem;
            if (!ConnectionFiles.Passwords.TrySave(window.Settings, out problem))
                TaskDialog.Show(PasswordTexts.Title, PasswordTexts.NotKept(problem));
            return window.Settings;
        }
    }
}
