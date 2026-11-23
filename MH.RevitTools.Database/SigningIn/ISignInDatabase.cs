using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// The MH database's two sign-in procedures. The stored hash never leaves the server: the add-in asks for the
    /// account's salt, hashes the password with it, and asks whether that's the stored one. Both throw
    /// <see cref="LinkDatabaseException"/> when the database can't be asked.
    /// </summary>
    public interface ISignInDatabase
    {
        /// <summary>The account's salt, in base64. A name nobody has gets one too, always the same.</summary>
        string GetSalt(string userName);

        /// <summary>
        /// Whether the account's password has this hash. The database counts wrong tries and makes the next one wait,
        /// so it's never asked twice for one click.
        /// </summary>
        SignInAnswer Check(string userName, string hash);
    }

    /// <summary>What the database answers to one try. Immutable.</summary>
    public sealed class SignInAnswer
    {
        public SignInAnswer(bool isValid, string displayName, string role, int waitSeconds)
        {
            IsValid = isValid;
            DisplayName = displayName;
            Role = role;
            WaitSeconds = waitSeconds;
        }

        public bool IsValid { get; }

        /// <summary>The person's name, or their user name; null unless valid.</summary>
        public string DisplayName { get; }

        /// <summary>E.g. Admin; null unless valid. Only shown: every active account may sign in.</summary>
        public string Role { get; }

        /// <summary>How long the next try for this name is refused, whatever the password; 0 for not at all.</summary>
        public int WaitSeconds { get; }
    }
}
