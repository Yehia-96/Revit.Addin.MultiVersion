using MH.RevitTools.Database.Diagnostics;

namespace MH.RevitTools.Database.Tests.Diagnostics;

public sealed class SqlClientInfoTests
{
    [Fact]
    public void Describe_NamesTheLoadedDriverAndRuntime()
    {
        string text = SqlClientInfo.Describe();

        Assert.Contains("Microsoft.Data.SqlClient 5.2.2 from ", text);
        Assert.Contains("Microsoft.Data.SqlClient.dll", text);
        Assert.Contains(".NET", text);
    }
}
