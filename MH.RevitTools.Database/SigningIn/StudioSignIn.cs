using System;
using System.Text;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// Signs a person in with their MH Database Studio account: asks the database for the account's salt, hashes the
    /// password with it as Studio does, and asks whether that's right. One call is one try, which the database counts
    /// when it's wrong. Takes a moment: call it on a background thread.
    /// </summary>
    public sealed class StudioSignIn
    {
        private readonly ISignInDatabase _database;
        private readonly ConnectionSettings _settings;

        /// <param name="settings">Which database it is.</param>
        public StudioSignIn(ISignInDatabase database, ConnectionSettings settings)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <param name="userName">Trimmed, as in Studio's own sign-in.</param>
        /// <param name="password">As typed, as in Studio's own sign-in.</param>
        /// <exception cref="LinkDatabaseException">The database couldn't be asked.</exception>
        public SignInResult Try(string userName, string password)
        {
            string name = (userName ?? "").Trim();
            if (name.Length == 0 || string.IsNullOrEmpty(password))
                return SignInResult.Incomplete;

            string salt = _database.GetSalt(name);
            string hash;
            try
            {
                hash = PasswordHash.Compute(password, salt);
            }
            catch (FormatException ex)
            {
                throw new LinkDatabaseException(
                    "The database and this add-in don't hash passwords the same way any more. The add-in needs an update.", ex);
            }
            catch (EncoderFallbackException)
            {
                // Not text, so it can't be a password Studio took; there's nothing to ask the database.
                return SignInResult.Refused(0);
            }

            SignInAnswer answer = _database.Check(name, hash);
            return answer.IsValid
                ? SignInResult.SignedIn(new SignedInPerson(
                    string.IsNullOrWhiteSpace(answer.DisplayName) ? name : answer.DisplayName, answer.Role, _settings))
                : SignInResult.Refused(answer.WaitSeconds);
        }
    }

    /// <summary>How one try ended. Immutable.</summary>
    public sealed class SignInResult
    {
        private SignInResult(SignedInPerson person, bool isIncomplete, int waitSeconds)
        {
            Person = person;
            IsIncomplete = isIncomplete;
            WaitSeconds = waitSeconds;
        }

        /// <summary>The user name or the password is missing; the database wasn't asked.</summary>
        public static SignInResult Incomplete { get; } = new SignInResult(null, true, 0);

        public static SignInResult SignedIn(SignedInPerson person) =>
            new SignInResult(person ?? throw new ArgumentNullException(nameof(person)), false, 0);

        /// <param name="waitSeconds">How long the next try is refused, 0 for not at all.</param>
        public static SignInResult Refused(int waitSeconds) => new SignInResult(null, false, Math.Max(0, waitSeconds));

        /// <summary>Who signed in; null when nobody did.</summary>
        public SignedInPerson Person { get; }

        public bool IsIncomplete { get; }

        /// <summary>
        /// How long the next try for this name is refused. When it's more than 0, this one may have been refused
        /// unchecked, too.
        /// </summary>
        public int WaitSeconds { get; }
    }
}
