using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;
using System.Windows.Interop;

namespace Revit.Addin._2027.DatabaseLink.SignIn
{
    /// <summary>
    /// What stands before everything that reads the MH database: someone has to have signed in with their Studio account
    /// since Revit started. It asks the first time that's needed. Call it on Revit's main thread, in API context.
    /// </summary>
    internal static class SignInGate
    {
        /// <summary>Who is signed in, until Revit closes.</summary>
        public static SignInSession Session { get; } = new SignInSession();

        /// <summary>True when someone is signed in to this database, asking first when nobody is.</summary>
        public static bool Ensure(UIApplication application, ConnectionSettings settings) =>
            Session.PersonFor(settings) != null || Ask(application, settings);

        /// <summary>Asks for a Studio account, whoever is signed in now; false when that was cancelled.</summary>
        public static bool Ask(UIApplication application, ConnectionSettings settings)
        {
            var window = new SignInWindow(new StudioSignIn(new SqlSignInDatabase(settings), settings), settings);
            new WindowInteropHelper(window).Owner = application.MainWindowHandle;
            if (window.ShowDialog() != true || window.Person == null)
                return false;
            Session.SignIn(window.Person);
            return true;
        }
    }
}
