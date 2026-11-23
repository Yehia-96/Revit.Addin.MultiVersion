using System;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class SignInSessionTests
{
    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local,1433", "MH_Database", false, "reader", "fake-password-for-tests", true, true);
    private static readonly ConnectionSettings Dev = new("(localdb)\\MSSQLLocalDB", "MH_Database", true, null, null, false, false);

    private static SignedInPerson Anna(ConnectionSettings settings) => new("Anna Wagner", "Editor", settings);

    [Fact]
    public void ItStartsWithNobody()
    {
        var session = new SignInSession();

        Assert.Null(session.Person);
        Assert.Null(session.PersonFor(Office));
    }

    [Fact]
    public void SigningIn_HoldsForThatDatabaseOnly()
    {
        var session = new SignInSession();
        SignedInPerson anna = Anna(Office);

        session.SignIn(anna);

        Assert.Same(anna, session.Person);
        Assert.Same(anna, session.PersonFor(Office));
        Assert.Null(session.PersonFor(Dev));
    }

    [Fact]
    public void AnotherLoginInTheConnectionFile_IsStillTheSameDatabase()
    {
        var session = new SignInSession();
        session.SignIn(Anna(Office));

        Assert.NotNull(session.PersonFor(new ConnectionSettings("cadsql.mh-frlg.local,1433", "mh_database", false, "other", "fake-password-for-tests-2", true, false)));
    }

    [Fact]
    public void SigningInAgain_ReplacesThePerson_AndSigningOutLeavesNobody()
    {
        var session = new SignInSession();
        session.SignIn(Anna(Office));
        var other = new SignedInPerson("M. Keller", "Viewer", Office);

        session.SignIn(other);
        Assert.Same(other, session.PersonFor(Office));

        session.SignOut();
        Assert.Null(session.Person);
        Assert.Null(session.PersonFor(Office));
    }

    [Fact]
    public void EveryChange_IsTold_AndNothingElse()
    {
        var session = new SignInSession();
        int told = 0;
        session.Changed += (sender, e) => told++;
        SignedInPerson anna = Anna(Office);

        session.SignOut();
        session.SignIn(anna);
        session.SignIn(anna);
        session.SignOut();
        session.SignOut();

        Assert.Equal(2, told);
    }

    [Fact]
    public void WhoeverIsTold_SeesTheNewPersonAlready()
    {
        var session = new SignInSession();
        SignedInPerson seen = null;
        session.Changed += (sender, e) => seen = ((SignInSession)sender).Person;
        SignedInPerson anna = Anna(Dev);

        session.SignIn(anna);

        Assert.Same(anna, seen);
    }

    [Fact]
    public void ANameFromTheDatabase_IsOneLine()
    {
        var person = new SignedInPerson(" Anna\r\n\tWagner  ", " Editor\n", Office);

        Assert.Equal(("Anna Wagner", "Editor"), (person.Name, person.Role));
    }

    [Fact]
    public void APersonNeedsANameAndADatabase()
    {
        Assert.Throws<ArgumentException>(() => new SignedInPerson(" ", "Editor", Office));
        Assert.Throws<ArgumentNullException>(() => new SignedInPerson("Anna", "Editor", null));
        Assert.Throws<ArgumentNullException>(() => new SignInSession().SignIn(null));
    }
}
