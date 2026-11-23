using System;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// What the Database panel's third row shows, as in the mockup: "Sign in…", or the name of who is signed in.
    /// Immutable.
    /// </summary>
    public sealed class SignInFace
    {
        // Longer names would widen the whole panel.
        private const int MaxLength = 24;

        private SignInFace(string text, string toolTip)
        {
            Text = text;
            ToolTip = toolTip;
        }

        /// <summary>Nobody is signed in.</summary>
        public static SignInFace Nobody { get; } = new SignInFace("Sign in…",
            "Signs you in to the MH database with your MH Database Studio account, once each time Revit starts. Nothing is saved.");

        public string Text { get; }

        public string ToolTip { get; }

        /// <param name="person">Who is signed in; null for nobody.</param>
        public static SignInFace For(SignedInPerson person)
        {
            if (person == null)
                return Nobody;
            string text = person.Name.Length <= MaxLength ? person.Name : person.Name.Substring(0, MaxLength - 1).TrimEnd() + "…";
            return new SignInFace(text, "Signed in as " + person.Name + " to " + person.Where
                + ", until Revit closes. Click to sign out, or to sign in as someone else.");
        }
    }
}
