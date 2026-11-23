using System;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class SignInTextsTests
{
    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local", "MH_Database", false, "revit_reader", "fake-password-for-tests", true, true);

    [Fact]
    public void TheWindow_SaysWhatTheMockupSays()
    {
        Assert.Equal("Sign in to the MH database", SignInTexts.Title);
        Assert.Equal("With your MH Database Studio account. Once each time Revit starts; nothing is saved.", SignInTexts.Subtitle);
        Assert.Equal("Wrong user name or password, or the account is not active in MH Database Studio.", SignInTexts.Wrong);
        Assert.Equal("Every active Studio account can sign in.", SignInTexts.Footer);
        Assert.Equal("Sign in to check for changes", SignInTexts.CardTitle);
    }

    [Fact]
    public void TheConnection_IsShown_WithoutItsLoginAndPassword()
    {
        string text = SignInTexts.Connection(Office);

        Assert.Equal("CADSQL.mh-frlg.local · MH_Database · read-only", text);
        Assert.DoesNotContain("revit_reader", text);
        Assert.DoesNotContain("fake-password", text);
    }

    [Theory]
    [InlineData(1, "1 s")]
    [InlineData(30, "30 s")]
    [InlineData(60, "1 min")]
    [InlineData(119, "1 min 59 s")]
    [InlineData(300, "5 min")]
    [InlineData(0, "1 s")]
    public void AWait_IsSaidInWords_AndDoesntClaimThePasswordWasWrong(int seconds, string expected)
    {
        Assert.Equal("Too many wrong tries for this user name. Try again in " + expected + "; until then even the right password is refused.",
            SignInTexts.Wait(seconds));
    }

    [Theory]
    [InlineData(0, "Sign in")]
    [InlineData(28, "Wait 28 s")]
    public void TheButton_SaysHowLongToWait(int seconds, string expected) => Assert.Equal(expected, SignInTexts.Button(seconds));

    [Fact]
    public void WhatWentWrong_IsSaid()
    {
        Assert.Equal("Can't sign in now. Can't reach CADSQL.", SignInTexts.Problem(new LinkDatabaseException("Can't reach CADSQL.")));
        Assert.Equal("Can't sign in now. Something went wrong in the add-in: boom", SignInTexts.Problem(new InvalidOperationException("boom")));
    }

    [Fact]
    public void WhoIsSignedIn_IsSaid()
    {
        var person = new SignedInPerson("Anna Wagner", "Editor", Office);

        Assert.Equal("Signed in as Anna Wagner · until Revit closes", SignInTexts.SignedIn(person.Name));
        Assert.Equal("Signed in as Anna Wagner", SignInTexts.WhoTitle(person));
        Assert.Equal("CADSQL · MH_Database · Editor in MH Database Studio" + Environment.NewLine + "Until Revit closes. Nothing about it is saved.",
            SignInTexts.WhoText(person));
        Assert.StartsWith("CADSQL · MH_Database" + Environment.NewLine, SignInTexts.WhoText(new SignedInPerson("Anna", null, Office)));
    }

    [Fact]
    public void TheRibbonsRow_SaysSignIn_OrTheName()
    {
        Assert.Equal("Sign in…", SignInFace.For(null).Text);

        SignInFace face = SignInFace.For(new SignedInPerson("Anna Wagner", "Editor", Office));
        Assert.Equal("Anna Wagner", face.Text);
        Assert.Contains("Signed in as Anna Wagner to CADSQL · MH_Database", face.ToolTip);
    }

    [Fact]
    public void ALongName_IsCutInTheRow_ButNotInItsToolTip()
    {
        const string name = "Maximiliane von und zu Musterstadt";

        SignInFace face = SignInFace.For(new SignedInPerson(name, "Editor", Office));

        Assert.Equal("Maximiliane von und zu…", face.Text);
        Assert.True(face.Text.Length <= 24);
        Assert.Contains(name, face.ToolTip);
    }
}
