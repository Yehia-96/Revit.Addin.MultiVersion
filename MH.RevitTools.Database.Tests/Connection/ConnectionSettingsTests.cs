using System;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ConnectionSettingsTests
{
    private const string FakePassword = "fake-password-for-tests";

    [Fact]
    public void ToString_NamesDatabaseAndServerButNeverThePassword()
    {
        var settings = new ConnectionSettings("sql.example.local", "MH_Database", false, "revit_reader", FakePassword, true, false);

        Assert.Equal("MH_Database on sql.example.local", settings.ToString());
        Assert.DoesNotContain(FakePassword, settings.ToString());
    }

    [Theory]
    [InlineData(true, false, "on, server certificate checked")]
    [InlineData(true, true, "on, server certificate not checked")]
    [InlineData(false, false, "off (only the sign-in is encrypted)")]
    [InlineData(false, true, "off (only the sign-in is encrypted)")]
    public void DescribeEncryption_SaysHowTheConnectionIsProtected(bool encrypt, bool trustServerCertificate, string expected)
    {
        var settings = new ConnectionSettings("s", "d", true, null, null, encrypt, trustServerCertificate);

        Assert.Equal(expected, settings.DescribeEncryption());
    }

    [Fact]
    public void WindowsSignIn_DropsLoginAndPassword()
    {
        var settings = new ConnectionSettings("s", "d", true, "someone", FakePassword, true, false);

        Assert.Null(settings.Login);
        Assert.Null(settings.Password);
    }

    [Fact]
    public void WithoutPassword_NeedsOne()
    {
        ConnectionSettings settings = ConnectionSettings.WithoutPassword(" sql.example.local ", " MH_Database ", " revit_reader ", true, true);

        Assert.True(settings.NeedsPassword);
        Assert.False(settings.HasEnteredPassword);
        Assert.Null(settings.Password);
        Assert.Equal(("sql.example.local", "MH_Database", "revit_reader"), (settings.Server, settings.Database, settings.Login));
        Assert.Equal((false, true, true), (settings.WindowsSignIn, settings.Encrypt, settings.TrustServerCertificate));
    }

    [Fact]
    public void WithPassword_IsTheSameConnection_AndKnowsItWasEntered()
    {
        ConnectionSettings without = ConnectionSettings.WithoutPassword("sql.example.local", "MH_Database", "revit_reader", true, false);

        ConnectionSettings settings = without.WithPassword(FakePassword);

        Assert.Equal(FakePassword, settings.Password);
        Assert.False(settings.NeedsPassword);
        Assert.True(settings.HasEnteredPassword);
        Assert.Equal((without.Server, without.Database, without.Login), (settings.Server, settings.Database, settings.Login));
        Assert.Equal((without.Encrypt, without.TrustServerCertificate), (settings.Encrypt, settings.TrustServerCertificate));
        Assert.True(without.NeedsPassword);
        Assert.DoesNotContain(FakePassword, settings.ToString());
    }

    [Fact]
    public void AnotherPassword_TakesTheEnteredOnesPlace()
    {
        ConnectionSettings settings = ConnectionSettings.WithoutPassword("s", "d", "l", true, false).WithPassword("first").WithPassword("second");

        Assert.Equal("second", settings.Password);
        Assert.True(settings.HasEnteredPassword);
    }

    [Fact]
    public void APasswordFromTheFile_WasNotEntered()
    {
        var settings = new ConnectionSettings("s", "d", false, "revit_reader", FakePassword, true, false);

        Assert.False(settings.NeedsPassword);
        Assert.False(settings.HasEnteredPassword);
    }

    [Fact]
    public void WindowsSignIn_NeedsAndTakesNoPassword()
    {
        var settings = new ConnectionSettings("s", "d", true, null, null, true, false);

        Assert.False(settings.NeedsPassword);
        Assert.False(settings.HasEnteredPassword);
        Assert.Throws<InvalidOperationException>(() => settings.WithPassword(FakePassword));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithPassword_RejectsNone(string password) =>
        Assert.Throws<ArgumentException>(() => ConnectionSettings.WithoutPassword("s", "d", "l", true, false).WithPassword(password));

    [Theory]
    [InlineData(" ", "d", "l")]
    [InlineData("s", null, "l")]
    [InlineData("s", "d", "")]
    public void WithoutPassword_RejectsIncompleteSettings(string server, string database, string login) =>
        Assert.Throws<ArgumentException>(() => ConnectionSettings.WithoutPassword(server, database, login, true, false));

    [Theory]
    [InlineData(null, "d", true, null, null)]
    [InlineData(" ", "d", true, null, null)]
    [InlineData("s", "", true, null, null)]
    [InlineData("s", "d", false, null, FakePassword)]
    [InlineData("s", "d", false, "revit_reader", "")]
    public void Constructor_RejectsIncompleteSettings(string server, string database, bool windowsSignIn, string login, string password)
    {
        Assert.Throws<ArgumentException>(() =>
            new ConnectionSettings(server, database, windowsSignIn, login, password, true, false));
    }
}
