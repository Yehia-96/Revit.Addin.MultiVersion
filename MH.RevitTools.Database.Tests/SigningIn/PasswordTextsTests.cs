using System;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class PasswordTextsTests
{
    private static readonly ConnectionSettings Office =
        ConnectionSettings.WithoutPassword("CADSQL.mh-frlg.local,1433", "MH_Database", "revit_reader", true, true).WithPassword("fakePasswordForTests1");

    private static ConnectionTestResult Failed(string problem, bool isLoginRefused) =>
        ConnectionTestResult.Failed(Office, problem, "details", TimeSpan.FromSeconds(1), isLoginRefused);

    [Fact]
    public void TheWindow_SaysWhereThePasswordComesFrom_AndWhatHappensToIt()
    {
        Assert.Equal("Database password", PasswordTexts.Title);
        Assert.Contains("Teams message", PasswordTexts.Subtitle);
        Assert.Contains("encrypted", PasswordTexts.Note);
        Assert.Contains("don't pass it on", PasswordTexts.Note);
    }

    [Fact]
    public void ARefusedPassword_IsToBeCopiedAgain() =>
        Assert.Equal(PasswordTexts.Refused, PasswordTexts.Problem(Failed("The server refused the login.", true)));

    // Not reachable says nothing about the password.
    [Fact]
    public void AnyOtherFailure_DoesntBlameThePassword()
    {
        string text = PasswordTexts.Problem(Failed("Can't reach CADSQL.mh-frlg.local,1433.", false));

        Assert.Equal("The password couldn't be checked, so it isn't kept. Can't reach CADSQL.mh-frlg.local,1433.", text);
        Assert.DoesNotContain("refused", text);
    }

    [Fact]
    public void WhatCouldntBeKept_IsSaid() =>
        Assert.Equal("The password works, but it couldn't be kept on this computer. Access denied. Revit asks for it again after a restart.",
            PasswordTexts.NotKept("Access denied."));

    [Fact]
    public void ThePanesCard_SaysWhatSignInAsksFor()
    {
        Assert.Contains("database password", PasswordTexts.CardMissing);
        Assert.Contains("Studio account", PasswordTexts.CardMissing);
        Assert.Contains("refused", PasswordTexts.CardRefused);
        Assert.Contains("Sign in…", PasswordTexts.CardRefused);
    }
}
