using System;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ConnectionTesterTests
{
    private const string FakePassword = "fake-password-for-tests";

    [Fact]
    public void Run_NothingListening_FailsInPlainWordsWithoutThePassword()
    {
        // Port 1 on this PC: nothing listens there, so the attempt is refused at once and never
        // leaves the machine.
        var settings = new ConnectionSettings("tcp:127.0.0.1,1", "MH_Database", false, "revit_reader", FakePassword, true, false);

        ConnectionTestResult result = ConnectionTester.Run(settings, 3);

        Assert.False(result.Succeeded);
        Assert.Equal("tcp:127.0.0.1,1", result.Server);
        Assert.Equal("MH_Database", result.Database);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
        Assert.DoesNotContain(FakePassword, result.Problem);
        Assert.DoesNotContain(FakePassword, result.Details);
        Assert.Null(result.SignedInAs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Run_RejectsTimeoutsBelowOneSecond(int timeoutSeconds)
    {
        var settings = new ConnectionSettings("s", "d", true, null, null, true, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectionTester.Run(settings, timeoutSeconds));
    }

    [Fact]
    public void Run_RejectsMissingSettings()
    {
        Assert.Throws<ArgumentNullException>(() => ConnectionTester.Run(null, 5));
    }

    [Fact]
    public void FailedResult_MasksThePasswordWhereverItAppears()
    {
        var settings = new ConnectionSettings("s", "d", false, "revit_reader", FakePassword, true, false);

        ConnectionTestResult result = ConnectionTestResult.Failed(
            settings, "problem " + FakePassword, "details " + FakePassword, TimeSpan.FromSeconds(1), false);

        Assert.DoesNotContain(FakePassword, result.Problem);
        Assert.DoesNotContain(FakePassword, result.Details);
    }

    [Fact]
    public void Run_NothingListening_DoesntBlameThePassword()
    {
        ConnectionSettings settings = ConnectionSettings.WithoutPassword("tcp:127.0.0.1,1", "MH_Database", "revit_reader", true, false)
            .WithPassword(FakePassword);

        ConnectionTestResult result = ConnectionTester.Run(settings, 3);

        Assert.False(result.Succeeded);
        Assert.False(result.IsLoginRefused);
    }

    // The caller asks for the password first; forgetting that is reported, not thrown.
    [Fact]
    public void Run_WithoutThePassword_FailsWithoutConnecting()
    {
        ConnectionTestResult result = ConnectionTester.Run(
            ConnectionSettings.WithoutPassword("tcp:127.0.0.1,1", "MH_Database", "revit_reader", true, false), 3);

        Assert.False(result.Succeeded);
        Assert.False(result.IsLoginRefused);
        Assert.Contains("password", result.Problem);
    }

    /// <summary>
    /// Talks to the developer PC's LocalDB through the master database: it reads three server
    /// properties and creates nothing. Starts LocalDB if it is stopped, which can take a while,
    /// hence the long timeout here (the ribbon button uses 5 seconds).
    /// </summary>
    [Fact]
    [Trait("Category", "LocalDB")]
    public void Run_LocalDb_ConnectsAndSaysWhoWeAre()
    {
        var settings = new ConnectionSettings(@"(localdb)\MSSQLLocalDB", "master", true, null, null, true, false);

        ConnectionTestResult result = ConnectionTester.Run(settings, 60);

        Assert.True(result.Succeeded, result.Problem + Environment.NewLine + result.Details);
        Assert.Equal("master", result.Database);
        Assert.Equal(Environment.UserName, result.SignedInAs.Substring(result.SignedInAs.IndexOf('\\') + 1), ignoreCase: true);
        Assert.StartsWith("SQL Server", result.ServerVersion);
        Assert.Null(result.Problem);
    }
}
