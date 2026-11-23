using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class SqlSignInDatabaseTests
{
    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local", "MH_Database", false, "reader", "fake-password-for-tests", true, true);

    private static string Describe(int number, bool isTry = true) => SqlSignInDatabase.Describe(number, "Msg " + number, Office, isTry);

    [Fact]
    public void ADatabaseWithoutTheProcedures_NeedsANewerStudio() =>
        Assert.Contains("newer MH Database Studio", Describe(SqlSignInDatabase.NoSuchProcedure));

    [Fact]
    public void ALoginWithoutTheRight_AndAMissingSecret_AreSaidInPlainWords()
    {
        Assert.Contains("isn't allowed", Describe(SqlSignInDatabase.PermissionDenied));
        Assert.Contains("Nobody can sign in", Describe(SqlSignInDatabase.SecretMissing));
    }

    [Fact]
    public void ATimeout_SaysTheTryMayHaveBeenCounted_OnlyWhereItCan()
    {
        Assert.Equal("CADSQL.mh-frlg.local didn't answer within 15 seconds. The try may have been counted.", Describe(-2));
        Assert.Equal("CADSQL.mh-frlg.local didn't answer within 15 seconds.", Describe(-2, isTry: false));
    }

    [Fact]
    public void OtherErrors_AreTheConnectionsOwn() => Assert.StartsWith("Can't reach CADSQL.mh-frlg.local", Describe(53));
}
