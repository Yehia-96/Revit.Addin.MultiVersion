using System;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// Who is signed in, from sign-in until Revit closes. In memory only: each start of Revit begins with nobody. It
    /// holds for the database it was made with; another one, after another connection file, asks again. Safe to use
    /// from several threads: the pane's check asks it from its own.
    /// </summary>
    public sealed class SignInSession
    {
        private readonly object _gate = new object();
        private SignedInPerson _person;

        /// <summary>Someone signed in or out; raised on the thread that did it.</summary>
        public event EventHandler Changed;

        /// <summary>Who signed in last, to whichever database; null when nobody is signed in.</summary>
        public SignedInPerson Person
        {
            get
            {
                lock (_gate)
                    return _person;
            }
        }

        /// <summary>Who is signed in to this database; null when nobody is.</summary>
        public SignedInPerson PersonFor(ConnectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            SignedInPerson person = Person;
            return person != null && string.Equals(person.DatabaseKey, PullCache.KeyFor(settings), StringComparison.Ordinal) ? person : null;
        }

        public void SignIn(SignedInPerson person)
        {
            if (person == null)
                throw new ArgumentNullException(nameof(person));
            Put(person);
        }

        public void SignOut() => Put(null);

        private void Put(SignedInPerson person)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_person, person))
                    return;
                _person = person;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
