using System;
using System.Linq;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class StudioSignInTests
{
    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local,1433", "MH_Database", false, "reader", "fake-password-for-tests", true, true);

    private readonly FakeSignInDatabase _database = new();

    private SignInResult Try(string userName, string password) => new StudioSignIn(_database, Office).Try(userName, password);

    [Fact]
    public void TheRightPassword_SignsIn()
    {
        SignInResult result = Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword);

        Assert.Equal("Anna Wagner", result.Person.Name);
        Assert.Equal("Editor", result.Person.Role);
        Assert.Equal("CADSQL · MH_Database", result.Person.Where);
        Assert.Equal(0, result.WaitSeconds);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void TheNameIsTrimmed_AndThePasswordIsnt_AsInStudio()
    {
        Assert.NotNull(Try("  A.Wagner\t", FakeSignInDatabase.MadeUpPassword).Person);
        Assert.Null(Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword + " ").Person);

        Assert.Equal("A.Wagner", _database.Checks[0].UserName);
        Assert.Equal("A.Wagner", _database.SaltsAskedFor[0]);
    }

    [Fact]
    public void OnlyTheHashGoesToTheDatabase_NeverThePassword()
    {
        Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword);

        Assert.DoesNotContain(FakeSignInDatabase.MadeUpPassword, _database.Checks.Single().Hash);
        Assert.Equal(PasswordHash.Compute(FakeSignInDatabase.MadeUpPassword, _database.Salt), _database.Checks.Single().Hash);
    }

    [Fact]
    public void APersonWithoutAName_IsShownByTheirUserName()
    {
        _database.DisplayName = " ";
        _database.Role = null;

        SignedInPerson person = Try(" a.wagner ", FakeSignInDatabase.MadeUpPassword).Person;

        Assert.Equal("a.wagner", person.Name);
        Assert.Equal("", person.Role);
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("  ", "x")]
    [InlineData("a.wagner", null)]
    [InlineData("a.wagner", "")]
    public void AMissingNameOrPassword_IsSaid_WithoutAskingTheDatabase(string userName, string password)
    {
        SignInResult result = Try(userName, password);

        Assert.True(result.IsIncomplete);
        Assert.Null(result.Person);
        Assert.Empty(_database.SaltsAskedFor);
        Assert.Empty(_database.Checks);
    }

    [Fact]
    public void AWrongPassword_AnUnknownNameAndAnInactiveAccount_AreRefusedAlike()
    {
        SignInResult wrong = Try(FakeSignInDatabase.UserName, "something else");
        SignInResult unknown = Try("nobody", FakeSignInDatabase.MadeUpPassword);
        _database.IsActive = false;
        SignInResult inactive = Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword);

        foreach (SignInResult result in new[] { wrong, unknown, inactive })
        {
            Assert.Null(result.Person);
            Assert.False(result.IsIncomplete);
            Assert.Equal(0, result.WaitSeconds);
        }
    }

    [Fact]
    public void TheDatabasesWait_IsPassedOn()
    {
        int[] waits = Enumerable.Range(1, 8).Select(_ => Try(FakeSignInDatabase.UserName, "wrong").WaitSeconds).ToArray();

        Assert.Equal(new[] { 0, 0, 0, 0, 30, 60, 120, 300 }, waits);
    }

    [Fact]
    public void OneTry_IsCheckedOnce_EvenWhenTheDatabaseFails()
    {
        _database.CheckFailure = new LinkDatabaseException("CADSQL didn't answer within 15 seconds.");

        var error = Assert.Throws<LinkDatabaseException>(() => Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword));

        Assert.Equal("CADSQL didn't answer within 15 seconds.", error.Message);
        Assert.Single(_database.Checks);
    }

    [Fact]
    public void ADatabaseThatCantBeReached_IsntAskedForTheCheck()
    {
        _database.Failure = new LinkDatabaseException("Can't reach CADSQL.");

        Assert.Throws<LinkDatabaseException>(() => Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword));

        Assert.Empty(_database.Checks);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void ASaltOfAnotherKind_MeansTheHashingChanged_AndIsntSentOn(string salt)
    {
        _database.Salt = salt;

        var error = Assert.Throws<LinkDatabaseException>(() => Try(FakeSignInDatabase.UserName, FakeSignInDatabase.MadeUpPassword));

        Assert.Contains("needs an update", error.Message);
        Assert.Empty(_database.Checks);
    }

    [Fact]
    public void WhatIsntText_IsRefused_WithoutCounting()
    {
        SignInResult result = Try(FakeSignInDatabase.UserName, "a\uD800b");

        Assert.Null(result.Person);
        Assert.Empty(_database.Checks);
    }
}
