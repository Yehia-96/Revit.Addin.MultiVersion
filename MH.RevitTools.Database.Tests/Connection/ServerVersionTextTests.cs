using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ServerVersionTextTests
{
    [Fact]
    public void Describe_SqlServer_GivesProductAndVersion()
    {
        const string atAtVersion = "Microsoft SQL Server 2019 (RTM-CU32-GDR) (KB5068404) - 15.0.4382.1 (X64) \n"
            + "\tJul 30 2025 13:43:27 \n\tCopyright (C) 2019 Microsoft Corporation\n\tExpress Edition (64-bit)";

        Assert.Equal("SQL Server 2019 (15.0.4382.1)", ServerVersionText.Describe(atAtVersion, "15.0.4382.1"));
    }

    [Fact]
    public void Describe_AzureSql_GivesProductAndVersion()
    {
        Assert.Equal("SQL Azure (12.0.2000.8)",
            ServerVersionText.Describe("Microsoft SQL Azure (RTM) - 12.0.2000.8 \n\tSep 18 2024", "12.0.2000.8"));
    }

    [Fact]
    public void Describe_WithoutVersionText_FallsBackToTheNumber()
    {
        Assert.Equal("SQL Server 16.0.1000.6", ServerVersionText.Describe(null, "16.0.1000.6"));
    }

    [Fact]
    public void Describe_WithoutVersionNumber_GivesTheProduct()
    {
        Assert.Equal("SQL Server 2022", ServerVersionText.Describe("Microsoft SQL Server 2022 (RTM) - 16.0.1000.6 (X64)", " "));
    }

    [Fact]
    public void Describe_WithNothing_SaysUnknown()
    {
        Assert.Equal("SQL Server (version unknown)", ServerVersionText.Describe(null, null));
    }
}
