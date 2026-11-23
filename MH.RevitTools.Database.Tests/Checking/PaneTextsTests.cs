using System;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;

namespace MH.RevitTools.Database.Tests.Checking;

public sealed class PaneTextsTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTime Now = new(2026, 11, 22, 13, 32, 0, DateTimeKind.Utc);
    private static readonly CheckInterval EveryMinute = CheckInterval.For(60);
    private static readonly CheckInterval WhenAsked = CheckInterval.For(0);

    [Fact]
    public void Times_AreTheClockToday_YesterdaySpelledOut_ElseTheDate()
    {
        Assert.Equal("14:28", PaneTexts.When(Now.AddMinutes(-4), Now, PlusOne));
        Assert.Equal("yesterday 14:28", PaneTexts.When(Now.AddDays(-1).AddMinutes(-4), Now, PlusOne));
        Assert.Equal("2026-11-20 14:28", PaneTexts.When(Now.AddDays(-2).AddMinutes(-4), Now, PlusOne));
        Assert.Equal("today 14:28", PaneTexts.WhenInSentence(Now.AddMinutes(-4), Now, PlusOne));
        Assert.Equal("yesterday 14:28", PaneTexts.WhenInSentence(Now.AddDays(-1).AddMinutes(-4), Now, PlusOne));
    }

    [Fact]
    public void TheDayFollowsThePcsTimeZone()
    {
        DateTime lateYesterdayUtc = new(2026, 11, 21, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("00:30", PaneTexts.When(lateYesterdayUtc, Now, PlusOne));
    }

    [Fact]
    public void TheHeader_SaysWhenItChecked_OrWhenTheDatabaseLastAnswered()
    {
        Assert.Equal("Checked 14:32 · every minute", PaneTexts.Checked(Now, Now, PlusOne, EveryMinute));
        Assert.Equal("Last answer 11:05 · tries again every minute",
            PaneTexts.NoAnswer(new DateTime(2026, 11, 22, 10, 5, 0, DateTimeKind.Utc), Now, PlusOne, EveryMinute));
        Assert.Equal("No answer yet · try again when you like", PaneTexts.NoAnswer(null, Now, PlusOne, WhenAsked));
    }

    [Fact]
    public void TheRedBanner_GivesTheReason_AndWhatHappensNext()
    {
        Assert.Equal("Can't reach the server. Is the VPN on? The model keeps its values, and the link tries again every minute.",
            PaneTexts.NotReachableDetail("Can't reach the server", EveryMinute));
        Assert.Equal("The server doesn't answer. Is the VPN on? The model keeps its values; Try again checks once more.",
            PaneTexts.NotReachableDetail(null, WhenAsked));
    }

    [Fact]
    public void ChangeCounts_AreSingularOrPlural()
    {
        Assert.Equal("1 change from the database", PaneTexts.ChangesTitle(1));
        Assert.Equal("3 changes from the database", PaneTexts.ChangesTitle(3));
        Assert.Equal("element linked", PaneTexts.LinkedLabel(1));
        Assert.Equal("elements linked", PaneTexts.LinkedLabel(405));
    }

    [Fact]
    public void UpToDate_NamesTheDatabasesLastChange()
    {
        ChangeReport report = ChangeFinder.Find(UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500B", Now.AddMinutes(-1), "S. Brandt"),
        }), Array.Empty<ModelElement>(), Array.Empty<ModelMaterial>(), (id, p, v) => ValueCheck.Same);

        Assert.Equal("The model matches the database. Last change there: today 14:31 · S. Brandt.", PaneTexts.UpToDate(report, Now, PlusOne));
        Assert.Equal("The model matches the database.",
            PaneTexts.UpToDate(ChangeFinder.Find(UlmData.With(1, Array.Empty<MH.RevitTools.Database.Link.LinkObjectValue>()),
                Array.Empty<ModelElement>(), Array.Empty<ModelMaterial>(), (id, p, v) => ValueCheck.Same), Now, PlusOne));
    }

    [Fact]
    public void TheModelSection_SaysWhoLinkedIt_AndHowElementsMeetTheProject()
    {
        Assert.Equal("Linked by Y.Abdelazziz, 2026-11-19 15:32. Elements meet it by Bauteil_ID.",
            PaneTexts.LinkedLine("Linked by Y.Abdelazziz, 2026-11-19 15:32."));
        Assert.Equal("Elements meet it by Bauteil_ID.", PaneTexts.LinkedLine(""));
        Assert.Equal("Values from the last pull: today 11:12.", PaneTexts.FromLastPull(Now.AddHours(-3).AddMinutes(-20), Now, PlusOne));
        Assert.Equal("Values from the last pull: today 11:12", PaneTexts.NotSignedIn(Now.AddHours(-3).AddMinutes(-20), Now, PlusOne));
        Assert.Equal("Nothing pulled on this computer yet", PaneTexts.NotSignedIn(null, Now, PlusOne));
    }

    [Theory]
    [InlineData("CADSQL.mh-frlg.local,1433", "CADSQL · MH_Database")]
    [InlineData("tcp:CADSQL.mh-frlg.local", "CADSQL · MH_Database")]
    [InlineData("(localdb)\\MSSQLLocalDB", "(localdb)\\MSSQLLocalDB · MH_Database")]
    [InlineData("SQLHOST\\MH", "SQLHOST\\MH · MH_Database")]
    [InlineData("10.0.0.5,1433", "10.0.0.5 · MH_Database")]
    public void TheDatabase_IsNamedShortly(string server, string expected)
    {
        Assert.Equal(expected, DatabaseLabel.For(new ConnectionSettings(server, "MH_Database", true, null, null, false, false)));
    }
}
