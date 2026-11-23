using System;
using MH.RevitTools.Database.Connection;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Tests.Connection;

public sealed class ConnectionStringFactoryTests
{
    private const string FakePassword = "fake-password-for-tests";

    private static SqlConnectionStringBuilder Build(ConnectionSettings settings, int timeoutSeconds = 5, bool pooling = true) =>
        new(ConnectionStringFactory.Build(settings, timeoutSeconds, pooling));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pooling_IsTheCallersChoice(bool pooling)
    {
        var builder = Build(new ConnectionSettings("s", "d", true, null, null, true, false), pooling: pooling);

        Assert.Equal(pooling, builder.Pooling);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhatMustAlwaysBeEncrypted_Is_WhateverTheConnectionFileSays(bool trustServerCertificate)
    {
        var settings = new ConnectionSettings("s", "d", true, null, null, false, trustServerCertificate);

        var always = new SqlConnectionStringBuilder(ConnectionStringFactory.Build(settings, 5, pooling: false, alwaysEncrypt: true));

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, always.Encrypt);
        Assert.Equal(trustServerCertificate, always.TrustServerCertificate);
        Assert.Equal(SqlConnectionEncryptOption.Optional, Build(settings).Encrypt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoConnection_JoinsATransactionAroundIt(bool pooling)
    {
        var builder = Build(new ConnectionSettings("s", "d", true, null, null, true, false), pooling: pooling);

        Assert.False(builder.Enlist);
    }

    [Fact]
    public void WindowsSignIn_UsesIntegratedSecurityWithoutCredentials()
    {
        var builder = Build(new ConnectionSettings(@"(localdb)\MSSQLLocalDB", "MH_Database", true, null, null, true, false));

        Assert.Equal(@"(localdb)\MSSQLLocalDB", builder.DataSource);
        Assert.Equal("MH_Database", builder.InitialCatalog);
        Assert.True(builder.IntegratedSecurity);
        Assert.Equal("", builder.UserID);
        Assert.Equal("", builder.Password);
    }

    [Fact]
    public void SqlLogin_PassesLoginAndPassword()
    {
        var builder = Build(new ConnectionSettings("sql.example.local,1433", "MH_Database", false, "revit_reader", FakePassword, true, false));

        Assert.False(builder.IntegratedSecurity);
        Assert.Equal("revit_reader", builder.UserID);
        Assert.Equal(FakePassword, builder.Password);
        Assert.False(builder.PersistSecurityInfo);
    }

    [Fact]
    public void Timeout_BecomesTheConnectTimeout()
    {
        var builder = Build(new ConnectionSettings("s", "d", true, null, null, true, false), timeoutSeconds: 7);

        Assert.Equal(7, builder.ConnectTimeout);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EncryptionSettings_AreCarriedOver(bool encrypt, bool trustServerCertificate)
    {
        var builder = Build(new ConnectionSettings("s", "d", true, null, null, encrypt, trustServerCertificate));

        Assert.Equal(encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional, builder.Encrypt);
        Assert.Equal(trustServerCertificate, builder.TrustServerCertificate);
    }

    [Fact]
    public void ApplicationName_ShowsUpAsTheAddIn()
    {
        var builder = Build(new ConnectionSettings("s", "d", true, null, null, true, false));

        Assert.Equal("MH.RevitTools", builder.ApplicationName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_RejectsTimeoutsBelowOneSecond(int timeoutSeconds)
    {
        var settings = new ConnectionSettings("s", "d", true, null, null, true, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectionStringFactory.Build(settings, timeoutSeconds, pooling: true));
    }
}
