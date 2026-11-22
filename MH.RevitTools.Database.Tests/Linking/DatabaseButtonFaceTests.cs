using System;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class DatabaseButtonFaceTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTime LinkedAt = new(2026, 11, 22, 14, 32, 0, DateTimeKind.Utc);

    [Fact]
    public void ALinkedModel_ShowsItsProjectNumber_AndTheGreenDot()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(new ModelLink(13, 1761, 1, "Ulm", LinkedAt, "Y.Abdelazziz"), PlusOne);

        Assert.True(face.IsLinked);
        Assert.Equal("Database\n1761-01", face.Text);
        Assert.Equal(
            "This model is linked to 1761-01 Ulm in the MH database. Linked by Y.Abdelazziz, 2026-11-22 15:32. "
            + "Click to see the link or change it.",
            face.ToolTip);
    }

    [Fact]
    public void WithoutASubNumber_TheNumberStandsAlone_AndAnUnknownWhoOrWhenIsLeftOut()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(new ModelLink(7, 1761, 0, "Ulm", null, null), PlusOne);

        Assert.Equal("Database\n1761", face.Text);
        Assert.Equal("This model is linked to 1761 Ulm in the MH database. Click to see the link or change it.", face.ToolTip);
    }

    [Fact]
    public void AModelThatIsntLinked_AsksToLinkIt()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(null, PlusOne);

        Assert.False(face.IsLinked);
        Assert.Equal("Database\nLink model", face.Text);
        Assert.StartsWith("This model isn't linked to a project in the MH database yet.", face.ToolTip);
    }

    [Fact]
    public void WithNoProjectModel_ItSaysDatabaseAlone()
    {
        Assert.False(DatabaseButtonFace.NoModel.IsLinked);
        Assert.Equal("Database", DatabaseButtonFace.NoModel.Text);
    }

    [Fact]
    public void TheText_NeverHoldsTheProjectName_SoNeverAPercentSign_WhichRevitRefuses()
    {
        DatabaseButtonFace face = DatabaseButtonFace.For(new ModelLink(13, 1761, 1, "Ulm 100%", LinkedAt, null), PlusOne);

        Assert.DoesNotContain("%", face.Text);
        Assert.Contains("1761-01 Ulm 100%", face.ToolTip);
    }

    [Fact]
    public void Faces_AreEqual_WhenTheyLookTheSame()
    {
        var link = new ModelLink(13, 1761, 1, "Ulm", LinkedAt, "Y.Abdelazziz");

        Assert.Equal(DatabaseButtonFace.For(link, PlusOne), DatabaseButtonFace.For(new ModelLink(13, 1761, 1, "Ulm", LinkedAt, "Y.Abdelazziz"), PlusOne));
        Assert.NotEqual(DatabaseButtonFace.For(link, PlusOne), DatabaseButtonFace.For(new ModelLink(14, 1761, 2, "Ulm", LinkedAt, "Y.Abdelazziz"), PlusOne));
        Assert.NotEqual(DatabaseButtonFace.For(null, PlusOne), DatabaseButtonFace.NoModel);
    }
}
