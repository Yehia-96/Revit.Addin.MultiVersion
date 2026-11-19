using System;
using System.IO;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ConnectionProblemsTests
{
    private static readonly ConnectionSettings SqlLogin =
        new("sql.example.local,1433", "MH_Database", false, "revit_reader", "fake-password-for-tests", true, false);

    private static readonly ConnectionSettings WindowsSignIn =
        new(@"(localdb)\MSSQLLocalDB", "MH_Database", true, null, null, true, false);

    [Fact]
    public void DriverFailure_PlaceholderBuild_SaysTheWrongBuildLoaded()
    {
        string text = ConnectionProblems.DescribeDriverFailure(
            new PlatformNotSupportedException("Microsoft.Data.SqlClient is not supported on this platform."));

        Assert.Contains("wrong build", text);
    }

    [Fact]
    public void DriverFailure_InsideTypeInitializer_IsUnwrapped()
    {
        var wrapped = new TypeInitializationException("Microsoft.Data.SqlClient.SqlConnection",
            new PlatformNotSupportedException("not supported"));

        Assert.Contains("wrong build", ConnectionProblems.DescribeDriverFailure(wrapped));
    }

    [Fact]
    public void DriverFailure_NativePartMissing_NamesTheSniFile()
    {
        string text = ConnectionProblems.DescribeDriverFailure(
            new DllNotFoundException("Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'"));

        Assert.Contains("Microsoft.Data.SqlClient.SNI.dll", text);
    }

    [Fact]
    public void DriverFailure_MissingAssembly_NamesTheFile()
    {
        string text = ConnectionProblems.DescribeDriverFailure(
            new FileNotFoundException("Could not load file or assembly", "Microsoft.Data.SqlClient, Version=5.0.0.0"));

        Assert.Contains("missing", text);
        Assert.Contains("Microsoft.Data.SqlClient, Version=5.0.0.0", text);
    }

    [Fact]
    public void DriverFailure_Unknown_GivesTheFirstLineOfTheMessage()
    {
        string text = ConnectionProblems.DescribeDriverFailure(new InvalidOperationException("First line\nsecond line"));

        Assert.Equal("Couldn't connect: First line", text);
    }

    [Fact]
    public void SqlError_Timeout_NamesServerAndSeconds()
    {
        string text = ConnectionProblems.DescribeSqlError(-2, "Connection Timeout Expired.", SqlLogin, 5);

        Assert.Equal("sql.example.local,1433 didn't answer within 5 seconds.", text);
    }

    [Fact]
    public void SqlError_LoginFailed_NamesTheLogin()
    {
        string text = ConnectionProblems.DescribeSqlError(18456, "Login failed for user 'revit_reader'.", SqlLogin, 5);

        Assert.Contains("\"revit_reader\"", text);
        Assert.Contains("connection file", text);
    }

    [Fact]
    public void SqlError_LoginFailedWithWindowsSignIn_SpeaksOfTheWindowsAccount()
    {
        string text = ConnectionProblems.DescribeSqlError(18456, "Login failed for user 'PC\\someone'.", WindowsSignIn, 5);

        Assert.Contains("Windows account", text);
    }

    [Fact]
    public void SqlError_CannotOpenDatabase_NamesTheDatabase()
    {
        string text = ConnectionProblems.DescribeSqlError(4060, "Cannot open database \"MH_Database\" requested by the login.", SqlLogin, 5);

        Assert.Contains("\"MH_Database\"", text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(26)]
    [InlineData(40)]
    [InlineData(53)]
    [InlineData(10060)]
    [InlineData(10061)]
    [InlineData(11001)]
    public void SqlError_Unreachable_PointsAtServerAndVpn(int number)
    {
        string text = ConnectionProblems.DescribeSqlError(number, "A network-related or instance-specific error occurred.", SqlLogin, 5);

        Assert.StartsWith("Can't reach sql.example.local,1433", text);
        Assert.Contains("VPN", text);
    }

    [Fact]
    public void SqlError_UntrustedCertificate_PointsAtTheSetting()
    {
        string text = ConnectionProblems.DescribeSqlError(-2146893019,
            "A connection was successfully established with the server, but then an error occurred during the login process. "
            + "(provider: SSL Provider, error: 0 - The certificate chain was issued by an authority that is not trusted.)",
            SqlLogin, 5);

        Assert.Contains("\"trustServerCertificate\"", text);
        Assert.DoesNotContain(": true", text);
    }

    [Fact]
    public void SqlError_CertificateMessage_WinsOverTheErrorNumber()
    {
        string text = ConnectionProblems.DescribeSqlError(18456,
            "A connection was successfully established with the server, but then an error occurred during the login process. "
            + "(provider: SSL Provider, error: 0 - The certificate chain was issued by an authority that is not trusted.)",
            SqlLogin, 5);

        Assert.StartsWith("The server's certificate isn't trusted", text);
    }

    [Fact]
    public void SqlError_Unknown_QuotesTheServer()
    {
        string text = ConnectionProblems.DescribeSqlError(50000, "Something odd happened.\r\nMore detail.", SqlLogin, 5);

        Assert.Equal("The server said: Something odd happened.", text);
    }
}
