using System;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class DatabaseButtonFaceTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTime LinkedAt = new(2026, 11, 22, 14, 32, 0, DateTimeKind.Utc);
    private static readonly ModelLink Ulm = new(13, 1761, 1, "Ulm", LinkedAt, "Y.Abdelazziz");

    [Fact]
    public void ALinkedModel_ShowsItsProjectNumber_AndTheGreenDot_WhenUpToDate()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.UpToDate, 0);

        Assert.Equal(ButtonDot.Green, face.Dot);
        Assert.Equal("Database\n1761-01", face.Text);
        Assert.Equal(
            "This model is linked to 1761-01 Ulm in the MH database. Linked by Y.Abdelazziz, 2026-11-22 15:32. "
            + "The model matches the database. Click to show or hide the MH Database pane.",
            face.ToolTip);
    }

    [Fact]
    public void WaitingChanges_GiveTheBlueDot_WithTheirNumber()
    {
        DatabaseButtonFace three = DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 3);
        DatabaseButtonFace one = DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 1);

        Assert.Equal(ButtonDot.Blue, three.Dot);
        Assert.Equal(3, three.Changes);
        Assert.Contains("3 changes from the database are waiting.", three.ToolTip);
        Assert.Contains("1 change from the database is waiting.", one.ToolTip);
    }

    [Fact]
    public void NoChanges_IsUpToDate()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 0);

        Assert.Equal(ButtonDot.Green, face.Dot);
        Assert.Equal(0, face.Changes);
    }

    [Theory]
    [InlineData(LinkCondition.NotReachable, ButtonDot.Red, "The database isn't reachable")]
    [InlineData(LinkCondition.ProjectProblem, ButtonDot.Red, "no longer has this model's project")]
    [InlineData(LinkCondition.NotSetUp, ButtonDot.Grey, "connection file can't be used")]
    [InlineData(LinkCondition.NotSignedIn, ButtonDot.Grey, "Nobody is signed in")]
    [InlineData(LinkCondition.Checking, ButtonDot.Grey, "Checking the database")]
    public void OtherStates_HaveTheirDot_AndSaySo(LinkCondition condition, ButtonDot dot, string said)
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(Ulm, PlusOne, condition, 5);

        Assert.Equal(dot, face.Dot);
        Assert.Equal(0, face.Changes);
        Assert.Contains(said, face.ToolTip);
        Assert.Equal("Database\n1761-01", face.Text);
    }

    [Fact]
    public void WithoutASubNumber_TheNumberStandsAlone_AndAnUnknownWhoOrWhenIsLeftOut()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(new ModelLink(7, 1761, 0, "Ulm", null, null), PlusOne, LinkCondition.UpToDate, 0);

        Assert.Equal("Database\n1761", face.Text);
        Assert.Equal("This model is linked to 1761 Ulm in the MH database. The model matches the database. "
            + "Click to show or hide the MH Database pane.", face.ToolTip);
    }

    [Fact]
    public void AModelThatIsntLinked_AsksToLinkIt_WithoutADot()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(null, PlusOne, LinkCondition.UpToDate, 4);

        Assert.Equal(ButtonDot.None, face.Dot);
        Assert.Equal("Database\nLink model", face.Text);
        Assert.StartsWith("This model isn't linked to a project in the MH database yet.", face.ToolTip);
    }

    [Fact]
    public void WithNoProjectModel_ItSaysDatabaseAlone()
    {
        Assert.Equal(ButtonDot.None, DatabaseButtonFace.NoModel.Dot);
        Assert.Equal("Database", DatabaseButtonFace.NoModel.Text);
    }

    [Fact]
    public void TheText_NeverHoldsTheProjectName_SoNeverAPercentSign_WhichRevitRefuses()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(new ModelLink(13, 1761, 1, "Ulm 100%", LinkedAt, null), PlusOne, LinkCondition.UpToDate, 0);

        Assert.DoesNotContain("%", face.Text);
        Assert.Contains("1761-01 Ulm 100%", face.ToolTip);
    }

    [Fact]
    public void Faces_AreEqual_WhenTheyLookTheSame()
    {
        Assert.Equal(DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 2),
            DatabaseButtonFace.For(new ModelLink(13, 1761, 1, "Ulm", LinkedAt, "Y.Abdelazziz"), PlusOne, LinkCondition.Changes, 2));
        Assert.NotEqual(DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 2), DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.Changes, 3));
        Assert.NotEqual(DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.UpToDate, 0), DatabaseButtonFace.For(Ulm, PlusOne, LinkCondition.NotReachable, 0));
        Assert.NotEqual(DatabaseButtonFace.For(null, PlusOne, LinkCondition.UpToDate, 0), DatabaseButtonFace.NoModel);
    }
}
