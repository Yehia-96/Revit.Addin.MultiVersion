using System;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>What Revit says when it asks for the database password, so the wording sits under tests.</summary>
    public static class PasswordTexts
    {
        public const string Title = "Database password";

        public const string Subtitle = "Once on this computer. You got it in a private Teams message.";

        public const string Note =
            "Kept encrypted for your Windows account on this computer. Please don't pass it on, and don't save it anywhere else.";

        public const string Footer = "No message? Ask whoever looks after the MH database.";

        public const string Button = "Continue";

        public const string Trying = "Checking…";

        public const string Incomplete = "Paste the password from the message.";

        /// <summary>When the window opens because the server refused the one that was kept.</summary>
        public const string RefusedBefore = "The server refused the password kept on this computer. If you got a new one, enter it here.";

        public const string Refused = "The server refused this password. Copy it from the message again, without anything before or after it.";

        /// <summary>The pane's card while nobody has entered it on this PC.</summary>
        public const string CardMissing =
            "With your MH Database Studio account. The first time, Revit also asks for the database password, which you got in a private "
            + "Teams message. Until then the model keeps its values and nothing is checked.";

        /// <summary>The pane's card after the server refused the one that was kept.</summary>
        public const string CardRefused =
            "The server refused the database password kept on this computer. If you got a new one, Sign in… asks for it. Until then the "
            + "model keeps its values and nothing is checked.";

        /// <summary>Why an entered password isn't kept.</summary>
        public static string Problem(ConnectionTestResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            return result.IsLoginRefused ? Refused : "The password couldn't be checked, so it isn't kept. " + result.Problem;
        }

        public static string NotKept(string problem) =>
            "The password works, but it couldn't be kept on this computer. " + problem + " Revit asks for it again after a restart.";
    }
}
