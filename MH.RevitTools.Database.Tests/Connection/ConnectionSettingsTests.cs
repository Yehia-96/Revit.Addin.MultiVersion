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
