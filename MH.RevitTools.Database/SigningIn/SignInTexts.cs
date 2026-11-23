using System;
using System.Globalization;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>What the sign-in says, in the mockup's words, so the wording sits under tests.</summary>
    public static class SignInTexts
    {
        private const int SecondsPerMinute = 60;

        public const string Title = "Sign in to the MH database";

        public const string Subtitle = "With your MH Database Studio account. Once each time Revit starts; nothing is saved.";

        public const string ConnectionNote =
            "From this computer's connection file, set by the installer. The link only reads: nothing in the database can be changed from Revit.";

        public const string Footer = "Every active Studio account can sign in.";

        public const string Incomplete = "Enter your user name and your password.";

        public const string Wrong = "Wrong user name or password, or the account is not active in MH Database Studio.";

        /// <summary>The pane's card while nobody is signed in.</summary>
        public const string CardTitle = "Sign in to check for changes";

        public const string CardText =
            "With your MH Database Studio account, once each time Revit starts. Until then the model keeps its values and nothing is checked.";

        /// <summary>The window's line about the connection, e.g. "CADSQL.mh-frlg.local,1433 · MH_Database · read-only".</summary>
        public static string Connection(ConnectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            return settings.Server + " · " + settings.Database + " · read-only";
        }

        /// <summary>While the database makes a name wait. It doesn't say whether the last try was right: it may not have been checked.</summary>
        public static string Wait(int seconds) =>
            "Too many wrong tries for this user name. Try again in " + Duration(seconds) + "; until then even the right password is refused.";

        /// <summary>The window's button, e.g. "Sign in", or "Wait 28 s" while the database makes the name wait.</summary>
        public static string Button(int waitSeconds) => waitSeconds > 0 ? "Wait " + Duration(waitSeconds) : "Sign in";

        /// <summary>What the window says when the database couldn't be asked.</summary>
        public static string Problem(Exception error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            return "Can't sign in now. " + (error is LinkDatabaseException ? error.Message : "Something went wrong in the add-in: " + error.Message);
        }

        /// <summary>The pane's line, e.g. "Signed in as A. Wagner · until Revit closes".</summary>
        public static string SignedIn(string name) => "Signed in as " + name + " · until Revit closes";

        /// <summary>The message behind the ribbon's name: who, and where.</summary>
        public static string WhoTitle(SignedInPerson person)
        {
            if (person == null)
                throw new ArgumentNullException(nameof(person));
            return "Signed in as " + person.Name;
        }

        public static string WhoText(SignedInPerson person)
        {
            if (person == null)
                throw new ArgumentNullException(nameof(person));
            return person.Where + (person.Role.Length == 0 ? "" : " · " + person.Role + " in MH Database Studio")
                + Environment.NewLine + "Until Revit closes. Nothing about it is saved.";
        }

        private static string Duration(int seconds)
        {
            int whole = Math.Max(1, seconds);
            int minutes = whole / SecondsPerMinute, rest = whole % SecondsPerMinute;
            if (minutes == 0)
                return Number(rest) + " s";
            return rest == 0 ? Number(minutes) + " min" : Number(minutes) + " min " + Number(rest) + " s";
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
