using System;
using System.IO;
using System.Reflection;
using System.Text;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

/// <summary>Which connection file counts: the PC's own if there is one, otherwise the one built into the add-in.</summary>
public sealed class ConnectionFileReadFirstTests : IDisposable
{
    private const string FakePassword = "fake-password-for-tests";

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    private readonly string _own;

    public ConnectionFileReadFirstTests()
    {
        Directory.CreateDirectory(_folder);
        _own = Path.Combine(_folder, "own", "database.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_own));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Json(string server) => $$"""
        { "server": "{{server}}", "database": "MH_Database", "login": "revit_reader", "password": "{{FakePassword}}" }
        """;

    private static string Broken => $$"""
        { "server": "broken", "database": "MH_Database", "login": "revit_reader", "password": "{{FakePassword}}
        """;

    private static MemoryStream Stream(string json) => new(Encoding.UTF8.GetBytes(json));

    private ConnectionFileResult ReadFirst(string builtIn) => ConnectionFile.ReadFirst(_own, () => Stream(builtIn));

    private ConnectionFileResult ReadFirst() => ReadFirst(Json("built-in"));

    [Fact]
    public void WithoutAnOwnFile_TheAddinsCounts()
    {
        ConnectionFileResult result = ReadFirst();

        Assert.True(result.IsLoaded, result.Problem);
        Assert.Equal("built-in", result.Settings.Server);
        Assert.Equal("built into the add-in", result.Source);
    }

    [Fact]
    public void WithoutTheOwnFolder_TheAddinsCounts()
    {
        Directory.Delete(Path.GetDirectoryName(_own));

        Assert.Equal("built-in", ReadFirst().Settings.Server);
    }

    // As on a developer's PC.
    [Fact]
    public void OwnFile_WinsOverTheAddins()
    {
        File.WriteAllText(_own, Json("own"));

        ConnectionFileResult result = ConnectionFile.ReadFirst(_own, () => throw new InvalidOperationException("Nobody asked for the add-in's."));

        Assert.True(result.IsLoaded, result.Problem);
        Assert.Equal("own", result.Settings.Server);
        Assert.Equal(_own, result.Source);
    }

    // A mistake in a developer's file must never quietly lead to the office's database.
    [Fact]
    public void BrokenOwnFile_IsReportedInsteadOfUsingTheAddins()
    {
        File.WriteAllText(_own, Broken);

        ConnectionFileResult result = ReadFirst();

        Assert.False(result.IsLoaded);
        Assert.Equal(_own, result.Source);
        Assert.Contains(_own, result.Problem);
        Assert.Contains("isn't valid JSON", result.Problem);
        Assert.DoesNotContain(FakePassword, result.Problem);
    }

    // Otherwise nobody who sees it would know why the add-in's file doesn't count.
    [Fact]
    public void BrokenOwnFile_SaysThatItCountsInsteadOfTheAddins()
    {
        File.WriteAllText(_own, Broken);

        string problem = ReadFirst().Problem;

        Assert.Contains("this computer's own", problem);
        Assert.Contains("instead of the one built into the add-in", problem);
    }

    [Fact]
    public void OwnFileThatCantBeOpened_IsReportedInsteadOfUsingTheAddins()
    {
        File.WriteAllText(_own, Json("own"));

        using (new FileStream(_own, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ConnectionFileResult result = ReadFirst();

            Assert.False(result.IsLoaded);
            Assert.Equal(_own, result.Source);
        }
    }

    // Only a place where Windows says there's nothing leads on to the add-in's.
    [Fact]
    public void SomethingElseInTheOwnPlace_IsReportedInsteadOfUsingTheAddins()
    {
        Directory.CreateDirectory(_own);

        ConnectionFileResult result = ReadFirst();

        Assert.False(result.IsLoaded);
        Assert.Equal(_own, result.Source);
    }

    // Can't come from a tested build; if it does, nobody may be sent looking for a file on the PC.
    [Fact]
    public void AddinsFileWithAMistake_SaysWhoseItIs()
    {
        ConnectionFileResult result = ReadFirst(Broken);

        Assert.False(result.IsLoaded);
        Assert.Equal("built into the add-in", result.Source);
        Assert.StartsWith("The connection file built into the add-in isn't valid JSON", result.Problem);
        Assert.DoesNotContain("this computer's own", result.Problem);
        Assert.DoesNotContain(FakePassword, result.Problem);
    }

    [Fact]
    public void AddinsFileWithoutAServer_SaysWhoseItIs() =>
        Assert.Equal("The connection file built into the add-in has no \"server\".", ReadFirst("""{ "database": "MH_Database" }""").Problem);

    [Fact]
    public void AddinBuiltWithoutAFile_SaysSo()
    {
        ConnectionFileResult result = ConnectionFile.ReadFirst(_own, () => null);

        Assert.False(result.IsLoaded);
        Assert.Null(result.Settings);
        Assert.Equal("built into the add-in", result.Source);
        Assert.Contains("built without its connection file", result.Problem);
    }

    // The library itself has none: it's the add-in's.
    [Fact]
    public void AssemblyWithoutAFile_SaysSo() =>
        Assert.Contains("built without its connection file", ConnectionFile.ReadFirst(_own, typeof(ConnectionFile).Assembly).Problem);

    [Fact]
    public void AddinsFile_MayStartWithAByteOrderMark()
    {
        byte[] text = Encoding.UTF8.GetBytes(Json("built-in"));
        var withMark = new MemoryStream();
        withMark.Write(Encoding.UTF8.GetPreamble());
        withMark.Write(text);
        withMark.Position = 0;

        ConnectionFileResult result = ConnectionFile.ReadFirst(_own, () => withMark);

        Assert.True(result.IsLoaded, result.Problem);
    }

    [Fact]
    public void AddinsFile_IsClosedAgain()
    {
        MemoryStream stream = Stream(Json("built-in"));

        ConnectionFile.ReadFirst(_own, () => stream);

        Assert.False(stream.CanRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void OwnPlace_IsRequired(string own) =>
        Assert.Throws<ArgumentException>(() => ConnectionFile.ReadFirst(own, typeof(ConnectionFile).Assembly));

    [Fact]
    public void Addin_IsRequired() =>
        Assert.Throws<ArgumentNullException>(() => ConnectionFile.ReadFirst(_own, (Assembly)null));
}
