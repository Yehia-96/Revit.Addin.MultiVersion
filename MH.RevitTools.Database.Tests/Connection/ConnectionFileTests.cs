using System;
using System.IO;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ConnectionFileTests : IDisposable
{
    private const string FakePassword = "fake-password-for-tests";

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    public ConnectionFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string WriteFile(string json)
    {
        string path = Path.Combine(_folder, "database.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void DefaultPath_IsTheProgramDataConnectionFile()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MH", "RevitTools", "database.json");

        Assert.Equal(expected, ConnectionFile.DefaultPath);
    }

    [Fact]
    public void Read_OfficeFile_ReturnsSqlLoginSettings()
    {
        string path = WriteFile($$"""
            {
              "server": "sql.example.local,1433",
              "database": "MH_Database",
              "login": "revit_reader",
              "password": "{{FakePassword}}",
              "encrypt": true,
              "trustServerCertificate": true
            }
            """);

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.True(result.IsLoaded);
        Assert.Null(result.Problem);
        Assert.Equal(path, result.Path);
        ConnectionSettings settings = result.Settings;
        Assert.Equal("sql.example.local,1433", settings.Server);
        Assert.Equal("MH_Database", settings.Database);
        Assert.False(settings.WindowsSignIn);
        Assert.Equal("revit_reader", settings.Login);
        Assert.Equal(FakePassword, settings.Password);
        Assert.True(settings.Encrypt);
        Assert.True(settings.TrustServerCertificate);
    }

    [Fact]
    public void Read_DevFile_UsesWindowsSignInWithoutLogin()
    {
        string path = WriteFile("""
            {
              "server": "(localdb)\\MSSQLLocalDB",
              "database": "MH_Database",
              "windowsSignIn": true
            }
            """);

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.True(result.IsLoaded);
        Assert.Equal(@"(localdb)\MSSQLLocalDB", result.Settings.Server);
        Assert.True(result.Settings.WindowsSignIn);
        Assert.Null(result.Settings.Login);
        Assert.Null(result.Settings.Password);
    }

    [Fact]
    public void Read_WithoutEncryptionKeys_EncryptsAndChecksTheCertificate()
    {
        string path = WriteFile("""{ "server": "s", "database": "d", "windowsSignIn": true }""");

        ConnectionSettings settings = ConnectionFile.Read(path).Settings;

        Assert.True(settings.Encrypt);
        Assert.False(settings.TrustServerCertificate);
    }

    [Fact]
    public void Read_TrimsServerAndDatabase()
    {
        string path = WriteFile("""{ "server": "  s  ", "database": " d ", "windowsSignIn": true }""");

        ConnectionSettings settings = ConnectionFile.Read(path).Settings;

        Assert.Equal("s", settings.Server);
        Assert.Equal("d", settings.Database);
    }

    [Fact]
    public void Read_IgnoresKeysItDoesNotKnow()
    {
        string path = WriteFile("""{ "server": "s", "database": "d", "windowsSignIn": true, "comment": "dev PC" }""");

        Assert.True(ConnectionFile.Read(path).IsLoaded);
    }

    [Fact]
    public void Read_KeysInAnotherCase_AreAccepted()
    {
        string path = WriteFile("""{ "Server": "s", "DATABASE": "d", "WindowsSignIn": true }""");

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.True(result.IsLoaded);
        Assert.Equal("s", result.Settings.Server);
    }

    [Fact]
    public void Read_SameKeyTwiceInDifferentCase_SaysSoInsteadOfPickingOne()
    {
        string path = WriteFile("""{ "server": "s", "Server": "t", "database": "d", "windowsSignIn": true }""");

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains("\"server\" more than once", result.Problem);
    }

    [Fact]
    public void Read_HugeFile_IsRejectedUnread()
    {
        string path = WriteFile("{ \"comment\": \"" + new string('x', 70 * 1024) + "\" }");

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains("too big", result.Problem);
    }

    [Fact]
    public void Read_MissingFile_NamesTheFile()
    {
        string path = Path.Combine(_folder, "database.json");

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Null(result.Settings);
        Assert.Contains(path, result.Problem);
    }

    [Fact]
    public void Read_InvalidJson_GivesThePositionButNotTheText()
    {
        string path = WriteFile($$"""
            {
              "server": "s",
              "database": "d",
              "login": "revit_reader",
              "password": "{{FakePassword}}
            }
            """);

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains("isn't valid JSON", result.Problem);
        Assert.Contains("line", result.Problem);
        Assert.DoesNotContain(FakePassword, result.Problem);
    }

    [Fact]
    public void Read_ValueOfTheWrongKind_NamesTheKeyButNotTheValue()
    {
        string path = WriteFile("""{ "server": "s", "database": "d", "windowsSignIn": "sometimes" }""");

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains("windowsSignIn", result.Problem);
        Assert.DoesNotContain("sometimes", result.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void Read_EmptyFile_SaysSo(string content)
    {
        string path = WriteFile(content);

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains("empty", result.Problem);
    }

    [Theory]
    [InlineData("""{ "database": "d", "windowsSignIn": true }""", "\"server\"")]
    [InlineData("""{ "server": " ", "database": "d", "windowsSignIn": true }""", "\"server\"")]
    [InlineData("""{ "server": "s", "windowsSignIn": true }""", "\"database\"")]
    [InlineData("""{ "server": "s", "database": "d" }""", "\"login\"")]
    [InlineData("""{ "server": "s", "database": "d", "windowsSignIn": false, "password": "x" }""", "\"login\"")]
    [InlineData("""{ "server": "s", "database": "d", "login": "revit_reader" }""", "\"password\"")]
    public void Read_MissingKey_NamesTheKey(string json, string expectedKey)
    {
        string path = WriteFile(json);

        ConnectionFileResult result = ConnectionFile.Read(path);

        Assert.False(result.IsLoaded);
        Assert.Contains(expectedKey, result.Problem);
    }
}
