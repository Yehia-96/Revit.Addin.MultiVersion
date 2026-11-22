using System;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class ParameterCheckTests
{
    private const string GenericModels = "OST_GenericModel";
    private const string Materials = "OST_Materials";

    private static readonly LinkParameter Dicke = new(61, new Guid("3f1c2a6e-8b4d-4c1e-9a7f-61d1c4e0a061"),
        "Dicke", "BAU", "GEO", "Dicke", "Double", "m", LinkTarget.Object, new[] { GenericModels });

    private static readonly LinkParameter BauteilId = new(10, new Guid("8a0b1c2d-3e4f-4a5b-8c6d-008e9f0a1b2c"),
        "Bauteil_ID", "IDE", "KAT", "Bauteil_ID", "String", null, LinkTarget.Object, new[] { GenericModels });

    private static readonly LinkParameter Bezeichnung = new(140, new Guid("0c5e9b7a-2d3f-4a8b-b1c6-140d2e3f4a5b"),
        "Bezeichnung (Material)", "MAT", "BES", "Bezeichnung", "String", null, LinkTarget.Material, new[] { Materials });

    private static readonly LinkParameter[] Dictionary = { Dicke, BauteilId, Bezeichnung };

    private static readonly MasterParameterFile FullFile = MasterFileText.Read(MasterFileText.With(
        (Dicke.Guid, "Dicke", "LENGTH"), (BauteilId.Guid, "Bauteil_ID", "TEXT"), (Bezeichnung.Guid, "Bezeichnung (Material)", "TEXT")));

    private static ModelBinding Bound(string name, Guid? guid, params string[] categories) => new(name, guid, true, categories);

    private static ParameterCheckRow RowOf(ParameterCheck check, LinkParameter parameter) =>
        Assert.Single(check.Rows, r => r.Parameter == parameter);

    [Fact]
    public void EmptyModel_EverythingIsMissing_AndAddBindsEachOnItsCategory()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, Array.Empty<ModelBinding>());

        Assert.Null(check.FileProblem);
        Assert.All(check.Rows, r => Assert.Equal(ParameterState.Missing, r.State));
        Assert.Equal(3, check.ToAdd.Count);
        ParameterToAdd dicke = Assert.Single(check.ToAdd, a => a.Parameter == Dicke);
        Assert.Equal(new[] { GenericModels }, dicke.Categories);
        Assert.False(dicke.ExtendsBinding);
        Assert.Equal(PaletteGroup.Geometry, dicke.Group);
        Assert.Equal(PaletteGroup.IdentityData, Assert.Single(check.ToAdd, a => a.Parameter == Bezeichnung).Group);
    }

    [Fact]
    public void ParameterBoundByItsGuid_IsReady_EvenUnderAnotherName()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { Bound("Dicke (alt)", Dicke.Guid, GenericModels) });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.Equal(ParameterState.Ready, row.State);
        Assert.Equal("Dicke (alt)", row.NameInRevit);
        Assert.Equal("The database calls it “Dicke” now.", row.Note);
        Assert.DoesNotContain(check.ToAdd, a => a.Parameter == Dicke);
    }

    [Fact]
    public void SameNameWithAnotherGuid_IsOtherGuid_AndLeftAlone()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { Bound("dicke", Guid.NewGuid(), GenericModels) });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.Equal(ParameterState.OtherGuid, row.State);
        Assert.Equal("dicke", row.NameInRevit);
        Assert.Contains("other GUID", row.Note);
        Assert.DoesNotContain(check.ToAdd, a => a.Parameter == Dicke);
    }

    [Fact]
    public void OtherGuid_CanBeReplaced_WhenTheDatabasesParameterCanBeAdded()
    {
        var old = new ModelBinding("Dicke", Guid.NewGuid(), true, new[] { GenericModels, "OST_Walls" }, 4711);

        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { old });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.True(row.CanBeReplaced);
        Assert.Same(old, row.Clash);
        Assert.Equal("Same name, other GUID: left empty unless you replace it.", row.Note);
        ParameterToReplace swap = Assert.Single(check.ToReplace);
        Assert.Same(old, swap.Old);
        Assert.Equal(4711, swap.Old.ElementId);
        Assert.Same(Dicke, swap.Add.Parameter);
        Assert.Equal(new[] { GenericModels }, swap.Add.Categories);
        Assert.False(swap.Add.ExtendsBinding);
        Assert.Equal(PaletteGroup.Geometry, swap.Add.Group);
    }

    [Fact]
    public void OtherGuid_WhenTheDatabasesParameterIsElsewhereInTheModel_ReplaceExtendsIt()
    {
        var model = new[] { Bound("Dicke", Guid.NewGuid(), GenericModels), Bound("Dicke", Dicke.Guid, "OST_Walls") };

        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, model);

        ParameterToReplace swap = Assert.Single(check.ToReplace);
        Assert.True(swap.Add.ExtendsBinding);
        Assert.Equal(new[] { GenericModels }, swap.Add.Categories);
    }

    [Fact]
    public void OneModelParameterInTheWayOfTwo_IsReplacedForBoth()
    {
        // Studio allows one name once per category, so two database parameters may share it on two categories.
        var onObjects = new LinkParameter(1, Guid.NewGuid(), "Kategorie", "BAU", "KAT", "Kategorie", "String", null, LinkTarget.Object, new[] { GenericModels });
        var onMaterials = new LinkParameter(2, Guid.NewGuid(), "Kategorie", "MAT", "KAT", "Kategorie", "String", null, LinkTarget.Material, new[] { Materials });
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((onObjects.Guid, "Kategorie", "TEXT"), (onMaterials.Guid, "Kategorie", "TEXT")));
        var old = new ModelBinding("Kategorie", Guid.NewGuid(), true, new[] { GenericModels, Materials }, 99);

        ParameterCheck check = ParameterCheck.Run(new[] { onObjects, onMaterials }, file, new[] { old });

        Assert.Equal(2, check.ToReplace.Count);
        Assert.All(check.ToReplace, swap => Assert.Same(old, swap.Old));
        Assert.Equal(new[] { onObjects, onMaterials }, check.ToReplace.Select(s => s.Add.Parameter));
    }

    [Fact]
    public void OtherGuid_CantBeReplaced_WhenTheMasterFileLacksTheDatabasesParameter()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((BauteilId.Guid, "Bauteil_ID", "TEXT")));

        ParameterCheck check = ParameterCheck.Run(Dictionary, file, new[] { Bound("Dicke", Guid.NewGuid(), GenericModels) });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.Equal(ParameterState.OtherGuid, row.State);
        Assert.False(row.CanBeReplaced);
        Assert.StartsWith("Same name, other GUID: left empty. It can't be replaced: not in MH_Parameter.txt yet", row.Note);
        Assert.Empty(check.ToReplace);
    }

    [Fact]
    public void SameNameNotShared_IsOtherGuid_Too()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { Bound("Bauteil_ID", null, GenericModels) });

        ParameterCheckRow row = RowOf(check, BauteilId);
        Assert.Equal(ParameterState.OtherGuid, row.State);
        Assert.Contains("isn't shared", row.Note);
    }

    [Fact]
    public void SameNameOnAnotherCategory_DoesNotClash()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { Bound("Dicke", Guid.NewGuid(), "OST_Walls") });

        Assert.Equal(ParameterState.Missing, RowOf(check, Dicke).State);
    }

    [Fact]
    public void ParameterOnOtherCategoriesOnly_IsMissing_AndAddExtendsItsBinding()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { Bound("Dicke", Dicke.Guid, "OST_Walls") });

        Assert.Equal(ParameterState.Missing, RowOf(check, Dicke).State);
        ParameterToAdd dicke = Assert.Single(check.ToAdd, a => a.Parameter == Dicke);
        Assert.Equal(new[] { GenericModels }, dicke.Categories);
        Assert.True(dicke.ExtendsBinding);
    }

    [Fact]
    public void TypeParameterOnOtherCategories_CantBeAdded()
    {
        ParameterCheck check = ParameterCheck.Run(Dictionary, FullFile, new[] { new ModelBinding("Dicke", Dicke.Guid, false, new[] { "OST_Walls" }) });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.Equal(ParameterState.CantAdd, row.State);
        Assert.Contains("type parameter", row.Note);
        Assert.DoesNotContain(check.ToAdd, a => a.Parameter == Dicke);
    }

    [Fact]
    public void ParameterNotInTheMasterFileYet_CantBeAdded()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH")));

        ParameterCheck check = ParameterCheck.Run(Dictionary, file, Array.Empty<ModelBinding>());

        ParameterCheckRow row = RowOf(check, BauteilId);
        Assert.Equal(ParameterState.CantAdd, row.State);
        Assert.Equal("Not in MH_Parameter.txt yet: Studio writes it a few seconds after a change.", row.Note);
        Assert.Equal(new[] { Dicke }, check.ToAdd.Select(a => a.Parameter));
    }

    [Fact]
    public void UnreadMasterFile_AddsNothing_AndSaysWhy()
    {
        MasterParameterFile file = MasterFileText.Read("not a shared parameter file");

        ParameterCheck check = ParameterCheck.Run(Dictionary, file, Array.Empty<ModelBinding>());

        Assert.Equal(file.Problem, check.FileProblem);
        Assert.All(check.Rows, r => Assert.Equal(ParameterState.CantAdd, r.State));
        Assert.Empty(check.ToAdd);
        Assert.Equal("The master shared parameter file wasn't read.",
            ParameterCheck.Run(Dictionary, null, Array.Empty<ModelBinding>()).FileProblem);
    }

    [Fact]
    public void MasterFileOfAnotherDatabase_AddsNothing_AndSaysSo()
    {
        MasterParameterFile otherDatabase = MasterFileText.Read(MasterFileText.With((Guid.NewGuid(), "Dicke", "LENGTH")));

        ParameterCheck check = ParameterCheck.Run(Dictionary, otherDatabase, Array.Empty<ModelBinding>());

        Assert.Contains("belongs to another database", check.FileProblem);
        Assert.Contains("\"masterParameterFile\"", check.FileProblem);
        Assert.Empty(check.ToAdd);
    }

    [Fact]
    public void JustAdded_ShowsAsAdded()
    {
        ParameterCheck check = ParameterCheck.Run(
            Dictionary, FullFile, new[] { Bound("Dicke", Dicke.Guid, GenericModels) }, new[] { Dicke.Guid });

        ParameterCheckRow row = RowOf(check, Dicke);
        Assert.Equal(ParameterState.Added, row.State);
        Assert.Equal("Added from the master file.", row.Note);
        Assert.True(row.IsReady);
    }

    [Fact]
    public void ParameterWithoutACategory_GetsARowSayingWhy()
    {
        var wall = new LinkParameter(8, Guid.NewGuid(), "Wandstärke", "BAU", "GEO", "Wandstärke", "Double", "cm", LinkTarget.Object, null);

        ParameterCheck check = ParameterCheck.Run(new[] { wall }, FullFile, Array.Empty<ModelBinding>());

        ParameterCheckRow row = Assert.Single(check.Rows);
        Assert.Equal(ParameterState.CantAdd, row.State);
        Assert.Equal("", row.Category);
        Assert.Contains("no Revit category", row.Note);
    }

    [Fact]
    public void Rows_ShowProblemsFirst_AndTheTabsCountThem()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH"), (Bezeichnung.Guid, "Bezeichnung (Material)", "TEXT")));
        var model = new[] { Bound("Bezeichnung (Material)", Bezeichnung.Guid, Materials), Bound("Bauteil_ID", Guid.NewGuid(), GenericModels) };

        ParameterCheck check = ParameterCheck.Run(Dictionary, file, model);

        Assert.Equal(
            new[] { ParameterState.OtherGuid, ParameterState.Missing, ParameterState.Ready },
            check.Rows.Select(r => r.State));
        Assert.Equal(3, check.Count(ParameterFilter.All));
        Assert.Equal(1, check.Count(ParameterFilter.Missing));
        Assert.Equal(1, check.Count(ParameterFilter.OtherGuid));
        Assert.Equal(1, check.Count(ParameterFilter.Ready));
        Assert.Equal(1, check.ReadyCount);
        Assert.Equal(new[] { Dicke }, check.Filter(ParameterFilter.Missing).Select(r => r.Parameter));
    }

    [Fact]
    public void Key_IsTheDictionarysFullName()
    {
        Assert.Equal("BAU-GEO-Dicke", ParameterCheck.KeyOf(Dicke));
        Assert.Equal("Notiz", ParameterCheck.KeyOf(new LinkParameter(7, Guid.NewGuid(), "Notiz", null, null, "Notiz", "String", null, LinkTarget.Object, null)));
    }
}
