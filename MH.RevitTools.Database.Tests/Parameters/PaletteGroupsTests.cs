using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class PaletteGroupsTests
{
    // The same palette groups the Excel route gives these groups' names (ParamMetaData.GroupMap).
    [Theory]
    [InlineData("KAT", PaletteGroup.IdentityData)]
    [InlineData("bes", PaletteGroup.IdentityData)]
    [InlineData("GEO", PaletteGroup.Geometry)]
    [InlineData("ORT", PaletteGroup.Constraints)]
    [InlineData("SPE", PaletteGroup.Data)]
    [InlineData("DAR", PaletteGroup.Graphics)]
    [InlineData("BET", PaletteGroup.Materials)]
    [InlineData(" MAT ", PaletteGroup.Materials)]
    [InlineData("ALL", PaletteGroup.General)]
    [InlineData("SON", PaletteGroup.General)]
    [InlineData("ZUL", PaletteGroup.General)]
    [InlineData(null, PaletteGroup.General)]
    public void GroupCode_PicksThePaletteGroup(string code, PaletteGroup group)
    {
        Assert.Equal(group, PaletteGroups.ForGroupCode(code));
    }
}
