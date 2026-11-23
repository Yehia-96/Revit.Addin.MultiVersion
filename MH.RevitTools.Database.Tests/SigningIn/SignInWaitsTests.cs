using System;
using MH.RevitTools.Database.SigningIn;

namespace MH.RevitTools.Database.Tests.SigningIn;

public sealed class SignInWaitsTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AName_WaitsAsLongAsTheDatabaseSaid()
    {
        var waits = new SignInWaits();

        waits.Remember("a.wagner", 30, Now);

        Assert.Equal(30, waits.SecondsLeft("a.wagner", Now));
        Assert.Equal(1, waits.SecondsLeft("a.wagner", Now.AddSeconds(29.1)));
        Assert.Equal(0, waits.SecondsLeft("a.wagner", Now.AddSeconds(30)));
        Assert.Equal(0, waits.SecondsLeft("a.wagner", Now.AddHours(1)));
    }

    [Fact]
    public void OtherNames_DontWait_ButOtherSpellingsDo()
    {
        var waits = new SignInWaits();

        waits.Remember(" A.Wagner ", 30, Now);

        Assert.Equal(30, waits.SecondsLeft("a.wagner", Now));
        Assert.Equal(0, waits.SecondsLeft("m.keller", Now));
        Assert.Equal(0, waits.SecondsLeft("", Now));
        Assert.Equal(0, waits.SecondsLeft(null, Now));
    }

    [Fact]
    public void TheLastWord_Counts()
    {
        var waits = new SignInWaits();

        waits.Remember("a.wagner", 30, Now);
        waits.Remember("a.wagner", 60, Now.AddSeconds(31));
        Assert.Equal(60, waits.SecondsLeft("a.wagner", Now.AddSeconds(31)));

        waits.Remember("a.wagner", 0, Now.AddSeconds(40));
        Assert.Equal(0, waits.SecondsLeft("a.wagner", Now.AddSeconds(40)));
    }
}
