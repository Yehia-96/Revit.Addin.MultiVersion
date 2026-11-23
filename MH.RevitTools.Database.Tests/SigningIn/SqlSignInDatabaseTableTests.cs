using System;
using System.Linq;
using System.Transactions;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.SigningIn;
using MH.RevitTools.Database.Tests.Link;

namespace MH.RevitTools.Database.Tests.SigningIn;

/// <summary>
/// The sign-in against the database's real procedures (the tests' copy, Link/RevitSignIn.sql), on a throwaway
/// database with made-up accounts. Never on MH_Database: a try is counted there.
/// </summary>
[Trait("Category", "LocalDB")]
public sealed class SqlSignInDatabaseTableTests
{
    private const string MadeUp = "Prüfwort für Tests 1 €";

    private static readonly string Salt = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static ThrowawayDatabase Database()
    {
        var database = new ThrowawayDatabase("StandInTables.sql", "RevitViews.sql", "StandInUsers.sql", "RevitSignIn.sql");
        string hash = PasswordHash.Compute(MadeUp, Salt);
        database.Execute(
            "INSERT dbo.Users (UserId, Username, FirstName, LastName, PasswordHash, PasswordSalt, Role, IsActive) VALUES"
            + " (1, N'a.wagner', N'Anna', N'Wagner', N'" + hash + "', N'" + Salt + "', N'Editor', 1),"
            + " (2, N'm.keller', NULL, NULL, N'" + hash + "', N'" + Salt + "', N'Viewer', 1),"
            + " (3, N's.brandt', N'S.', N'Brandt', N'" + hash + "', N'" + Salt + "', N'Viewer', 0);");
        return database;
    }

    private static SignInResult Try(ThrowawayDatabase database, string userName, string password) =>
        new StudioSignIn(new SqlSignInDatabase(database.Settings), database.Settings).Try(userName, password);

    [Fact]
    public void TheRightPassword_SignsIn_WhateverTheNamesCase()
    {
        using ThrowawayDatabase database = Database();

        SignedInPerson anna = Try(database, " A.Wagner ", MadeUp).Person;
        SignedInPerson keller = Try(database, "m.keller", MadeUp).Person;

        Assert.Equal(("Anna Wagner", "Editor"), (anna.Name, anna.Role));
        Assert.Equal(("m.keller", "Viewer"), (keller.Name, keller.Role));
    }

    [Fact]
    public void AWrongPassword_AnUnknownNameAndAnInactiveAccount_AreRefused()
    {
        using ThrowawayDatabase database = Database();

        Assert.Null(Try(database, "a.wagner", MadeUp + " ").Person);
        Assert.Null(Try(database, "nobody", MadeUp).Person);
        Assert.Null(Try(database, "s.brandt", MadeUp).Person);
        Assert.Null(Try(database, new string('x', 300), MadeUp).Person);
    }

    [Fact]
    public void TheFifthWrongTry_MakesTheNextOneWait_EvenWithTheRightPassword()
    {
        using ThrowawayDatabase database = Database();

        int[] waits = Enumerable.Range(1, 5).Select(_ => Try(database, "a.wagner", "wrong").WaitSeconds).ToArray();
        SignInResult during = Try(database, "a.wagner", MadeUp);

        Assert.Equal(new[] { 0, 0, 0, 0, 30 }, waits);
        Assert.Null(during.Person);
        Assert.InRange(during.WaitSeconds, 1, 30);
        Assert.NotNull(Try(database, "m.keller", MadeUp).Person);
    }

    [Fact]
    public void AnUnknownName_GetsASaltThatStaysTheSame()
    {
        using ThrowawayDatabase database = Database();
        var signIn = new SqlSignInDatabase(database.Settings);

        Assert.Equal(Salt, signIn.GetSalt("a.wagner"));
        Assert.Equal(signIn.GetSalt("nobody"), signIn.GetSalt("NOBODY"));
        Assert.Equal(44, signIn.GetSalt("nobody").Length);
        Assert.NotEqual(Salt, signIn.GetSalt("s.brandt"));
    }

    [Fact]
    public void ATransactionAroundIt_DoesntReachTheSignIn()
    {
        using ThrowawayDatabase database = Database();
        using var scope = new TransactionScope();

        Assert.NotNull(Try(database, "a.wagner", MadeUp).Person);
    }

    [Fact]
    public void ADatabaseWithoutTheProcedures_SaysWhatItNeeds()
    {
        using var database = new ThrowawayDatabase(StandInDatabaseFixture.StandInScripts);

        var error = Assert.Throws<LinkDatabaseException>(() => Try(database, "a.wagner", MadeUp));

        Assert.Contains("newer MH Database Studio", error.Message);
    }
}
