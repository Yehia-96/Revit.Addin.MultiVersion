using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Changes;

/// <summary>What an object is made of (revit.ObjectMaterials): listed and applied like its values.</summary>
public sealed class ObjectMaterialTests
{
    private static readonly DateTime Monday = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 11, 25, 1, 23, 0, DateTimeKind.Utc);

    private static readonly LinkMaterial Beton13 = new("MB-1761-01-BET-0013", "13-Beton, C20/25", "BET", "Beton", null, null);
    private static readonly LinkMaterial Beton31 = new("MB-1761-01-BET-0031", "31-Beton, C35/45", "BET", "Beton", null, null);

    private static readonly ModelElement[] TwoPlates =
        { new(1, "BT-1101-01", "Bodenplatte: 30 cm"), new(2, " bt-1101-01", "Bodenplatte: 30 cm") };

    private static readonly ModelMaterial[] Model = { new(70, "13-Beton, C20/25"), new(71, "31-Beton, C35/45") };

    private static LinkObjectMaterial MadeOf(string bauteilId, LinkMaterial material, DateTime? at = null, string by = null) =>
        new(bauteilId, material.MaterialId, material.Name, at, by);

    private static LinkProjectData Data(IEnumerable<LinkObjectMaterial> objectMaterials, IEnumerable<LinkObjectValue> values = null,
        IEnumerable<LinkMaterial> materials = null) => UlmData.With(
        1, values ?? Array.Empty<LinkObjectValue>(), materials ?? new[] { Beton13, Beton31 }, null, objectMaterials);

    private static ChangeReport Find(LinkProjectData data, CompareMaterial compare, IEnumerable<ModelElement> elements = null,
        CompareValue values = null, IEnumerable<ModelMaterial> model = null) => ChangeFinder.Find(
        data, elements ?? TwoPlates, model ?? Model, values ?? ((id, parameter, value) => ValueCheck.Same), null, compare);

    private static ValueCheck NothingToAsk(long id, string name) => throw new InvalidOperationException("Nothing to ask for.");

    [Fact]
    public void ADifferingMaterial_IsAChangeOfTheObject()
    {
        var asked = new List<(long, string)>();
        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13, Today, "Yehia Abdelazziz") }), (id, name) =>
        {
            asked.Add((id, name));
            return id == 1 ? ValueCheck.Differs("<By Category>") : ValueCheck.Same;
        });

        Assert.Equal(new[] { (1L, "13-Beton, C20/25"), (2L, "13-Beton, C20/25") }, asked);
        Assert.Equal(1, report.ChangeCount);
        ChangeGroup group = Assert.Single(report.Groups);
        Assert.False(group.IsMaterial);
        Assert.Equal("BT-1101-01", group.Key);
        Assert.Equal(new[] { 1L, 2L }, group.ElementIds);
        ChangeRow row = Assert.Single(group.Rows);
        Assert.True(row.IsObjectMaterial);
        Assert.Null(row.Pattern);
        Assert.Equal(("Material", "<By Category>", "13-Beton, C20/25", "13-Beton, C20/25"), (row.Parameter, row.From, row.To, row.Value));
        Assert.Equal(new[] { 1L }, row.ElementIds);
        Assert.Equal(Today, row.ChangedAtUtc);
        Assert.Equal("Yehia Abdelazziz", row.ChangedBy);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void AnObjectsMaterial_SitsWithItsValues_TheMostRecentFirst()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13, Today, "Yehia Abdelazziz") }, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Bauteil, "Bodenplatte", Monday, "TheAdmin"),
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500A", Monday, "TheAdmin"),
        });

        ChangeReport report = Find(data, (id, name) => ValueCheck.Differs(""), values: (id, parameter, value) => ValueCheck.Differs("old"));

        ChangeGroup group = Assert.Single(report.Groups);
        Assert.Equal("Bodenplatte", group.Name);
        Assert.Equal(new[] { "Material", "Betonstahl Güte", "Bauteil" }, group.Rows.Select(row => row.Parameter));
        Assert.Equal(new[] { true, false, false }, group.Rows.Select(row => row.IsObjectMaterial));
        Assert.Equal("(empty)", group.Rows[0].From);
        Assert.Equal(3, report.ChangeCount);
        Assert.Equal(Today, group.NewestChangeUtc);
    }

    // Cleared, or never set: the link doesn't take a material away.
    [Fact]
    public void WithoutAMaterialInTheDatabase_TheElementsKeepTheirs()
    {
        LinkProjectData data = Data(Array.Empty<LinkObjectMaterial>(), new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500B") });

        ChangeReport report = Find(data, NothingToAsk);

        Assert.True(report.IsUpToDate);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void AnObjectWithNothingButAMaterial_IsOneOfTheProjects()
    {
        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13) }), (id, name) => ValueCheck.Differs("Beton"));

        Assert.Equal(new[] { 1L, 2L }, report.Counts.Linked);
        Assert.Empty(report.Counts.IdNotInProject);
        Assert.Equal("Bodenplatte: 30 cm", Assert.Single(report.Groups).Name);
    }

    [Fact]
    public void AnObjectTheModelDoesntHave_IsntAskedFor()
    {
        ChangeReport report = Find(Data(new[] { MadeOf("ET-2101-01", Beton13) }), NothingToAsk);

        Assert.True(report.IsUpToDate);
        Assert.Equal(new[] { 1L, 2L }, report.Counts.IdNotInProject);
    }

    [Fact]
    public void TheNewestChange_CanBeAMaterials()
    {
        LinkProjectData data = Data(new[] { MadeOf("ET-2101-01", Beton13, Today, "S. Brandt") },
            new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500B", Monday, "TheAdmin") });

        ChangeReport report = Find(data, (id, name) => ValueCheck.Same);

        Assert.Equal(Today, report.NewestChangeUtc);
        Assert.Equal("S. Brandt", report.NewestChangeBy);
    }

    [Fact]
    public void ElementsWithDifferentMaterials_ShowHowManyThereAre()
    {
        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13) }), (id, name) => ValueCheck.Differs("Beton " + id));

        ChangeRow row = Assert.Single(Assert.Single(report.Groups).Rows);
        Assert.Equal("2 different values", row.From);
        Assert.Equal(new[] { 1L, 2L }, row.ElementIds);
    }

    [Fact]
    public void AMaterialThatCantBeSetThere_IsNoChange_AndListedWithTheReason()
    {
        NotAppliedItem Blocked(ValueCheck check, bool blocked = true)
        {
            ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13) }), (id, name) => check);
            Assert.True(report.IsUpToDate);
            Assert.Equal(blocked, report.HasBlockedDifferences);
            NotAppliedItem item = Assert.Single(report.NotApplied);
            Assert.Equal(new[] { "Material" }, item.Parameters);
            return item;
        }

        NotAppliedItem borrowed = Blocked(ValueCheck.Borrowed("M. Keller"));
        Assert.Equal((NotAppliedReason.Borrowed, "M. Keller"), (borrowed.Reason, borrowed.Name));
        Assert.Equal(new[] { 1L, 2L }, borrowed.ElementIds);
        Assert.Equal(NotAppliedReason.OutOfDate, Blocked(ValueCheck.OutOfDate).Reason);
        Assert.Equal("Material is read-only in 2 elements", ApplyTexts.Title(Blocked(ValueCheck.ReadOnly)));
        Assert.Equal("Material sits on the type of 2 elements", ApplyTexts.Title(Blocked(ValueCheck.OnType)));
        Assert.Equal("Material: Revit couldn't read it", ApplyTexts.Title(Blocked(ValueCheck.CantCompare("Revit couldn't read it"), false)));
        Assert.Equal("Material: Revit gave no answer for it", ApplyTexts.Title(Blocked(null, false)));
    }

    [Fact]
    public void ABorrowedElement_IsNamedOnce_ForItsValuesAndItsMaterial()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13) }, new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500A") });

        ChangeReport report = Find(data, (id, name) => ValueCheck.Borrowed("M. Keller"), values: (id, parameter, value) => ValueCheck.Borrowed("M. Keller"));

        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal("2 elements are borrowed by M. Keller", ApplyTexts.Title(item));
        Assert.Equal(new[] { "Material", "Betonstahl Güte" }, item.Parameters.OrderByDescending(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ElementsWithoutAMaterialParameter_AreListed()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13) });

        NotAppliedItem two = Assert.Single(Find(data, (id, name) => ValueCheck.NoParameter).NotApplied);
        NotAppliedItem one = Assert.Single(Find(data, (id, name) => id == 1 ? ValueCheck.NoParameter : ValueCheck.Same).NotApplied);

        Assert.Equal(NotAppliedReason.NoMaterialParameter, two.Reason);
        Assert.Equal(new[] { 1L, 2L }, two.ElementIds);
        Assert.Equal("2 elements have no parameter for their material", ApplyTexts.Title(two));
        Assert.Equal("The database says what they're made of. Their families need an instance parameter called Material that takes a material, "
            + "as for the Excel import.", ApplyTexts.Detail(two));
        Assert.Equal("1 element has no parameter for its material", ApplyTexts.Title(one));
        Assert.Equal("The database says what it's made of. Its family needs an instance parameter called Material that takes a material, as for "
            + "the Excel import.", ApplyTexts.Detail(one));
    }

    [Fact]
    public void AMaterialTheModelDoesntHave_IsNotApplied_OnceForAllObjects()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton31), MadeOf("BT-1102-01", Beton31) });

        ChangeReport report = Find(data, (id, name) => ValueCheck.NoMaterial,
            new[] { new ModelElement(1, "BT-1101-01", null), new ModelElement(3, "BT-1102-01", null) }, null, new[] { Model[0] });

        Assert.Equal("31-Beton, C35/45", Assert.Single(report.MissingMaterials).Name);
        Assert.True(report.IsUpToDate);
        Assert.Equal("Nothing to apply now", ApplyTexts.UpToDateTitle(report));
        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal((NotAppliedReason.MissingMaterial, "31-Beton, C35/45"), (item.Reason, item.Name));
        Assert.Equal(new[] { 1L, 3L }, item.ElementIds);
        Assert.Equal("The material “31-Beton, C35/45” is not in this model", ApplyTexts.Title(item));
        Assert.Equal("It's the material of 2 elements. Create… makes it (Not in this model); then it can be applied.", ApplyTexts.Detail(item));
    }

    [Theory]
    [InlineData("MP-9999-99-BET-0101", true)]
    [InlineData("MP-9999-98-BET-0101", true)]
    [InlineData("MB-1760-01-BET-0001", false)] // moved to another project in Studio
    [InlineData(null, false)] // without a full ID
    public void AMaterialThatsNotTheProjectsOwn_IsntCreated_AndItSaysSo(string materialId, bool isGlobal)
    {
        LinkProjectData data = Data(new[] { new LinkObjectMaterial("BT-1101-01", materialId, "0101-Beton", null, null) });

        NotAppliedItem item = Assert.Single(Find(data, (id, name) => id == 1 ? ValueCheck.NoMaterial : ValueCheck.Same).NotApplied);

        Assert.Equal("The material “0101-Beton” is not in this model", ApplyTexts.Title(item));
        Assert.Equal("It's the material of 1 element. " + (isGlobal ? "It's a global one, and Create… makes the project's own only." : "Create… can't make it.")
            + " Once the model has a material of that name, it can be applied.", ApplyTexts.Detail(item));
    }

    [Fact]
    public void AnOwnMaterialThatCantBeCreated_IsntPromised()
    {
        var noGroup = new LinkMaterial("MB-1761-01-BET-0014", "14-Beton", null, " ", null, null);

        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", noGroup) }, null, new[] { noGroup }), (id, name) => ValueCheck.NoMaterial);

        Assert.Equal(new[] { NotAppliedReason.MissingMaterial, NotAppliedReason.NotCreatable }, report.NotApplied.Select(item => item.Reason));
        Assert.Contains("Create… can't make it.", ApplyTexts.Detail(report.NotApplied[0]));
    }

    [Fact]
    public void ObjectsThatShareABauteilId_AndAreMadeOfDifferentMaterials_GetNeither()
    {
        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13), MadeOf("bt-1101-01 ", Beton31) }), NothingToAsk);

        Assert.True(report.IsUpToDate);
        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal("BT-1101-01 has different values for Material", ApplyTexts.Title(item));
        Assert.Contains("another Bauteilunternummer in Studio", ApplyTexts.Detail(item));
    }

    // They can be alike in every column; then there's nothing to choose.
    [Fact]
    public void ObjectsThatShareABauteilId_AndAreAlike_GetTheirMaterial()
    {
        LinkObjectMaterial first = MadeOf("BT-1101-01", Beton13, Monday, "TheAdmin"), second = MadeOf("BT-1101-01", Beton13, Today, "S. Brandt");

        ChangeReport report = Find(Data(new[] { first, first, second }), (id, name) => ValueCheck.Differs(""));

        ChangeRow row = Assert.Single(Assert.Single(report.Groups).Rows);
        Assert.Equal(("13-Beton, C20/25", "S. Brandt"), (row.To, row.ChangedBy));
        Assert.Empty(report.NotApplied);
    }

    // Revit has one material per name, and it's the first one's.
    [Fact]
    public void AMaterialThatSharesItsNameWithAnotherOne_IsntTakenForIt()
    {
        var second = new LinkMaterial("MB-1761-01-BET-0099", "13-beton, c20/25", "BET", "Beton", null, null);

        NotAppliedItem own = Assert.Single(Find(Data(new[] { MadeOf("BT-1101-01", second) }, null, new[] { Beton13, second }), NothingToAsk).NotApplied);
        NotAppliedItem global = Assert.Single(Find(Data(new[]
        {
            new LinkObjectMaterial("BT-1101-01", "MP-9999-99-BET-0101", Beton13.Name, null, null),
        }), NothingToAsk).NotApplied);

        Assert.Equal((NotAppliedReason.SameMaterialName, "13-beton, c20/25", "MB-1761-01-BET-0099"), (own.Reason, own.Name, own.Text));
        Assert.Equal((NotAppliedReason.SameMaterialName, "13-Beton, C20/25", "MP-9999-99-BET-0101"), (global.Reason, global.Name, global.Text));
    }

    [Fact]
    public void AMaterialWithoutAnId_GoesByItsName()
    {
        ChangeReport report = Find(Data(new[] { new LinkObjectMaterial("BT-1101-01", " ", " 13-Beton, C20/25 ", null, null) }), (id, name) => ValueCheck.Differs(""));

        Assert.Equal("13-Beton, C20/25", Assert.Single(Assert.Single(report.Groups).Rows).Value);
    }

    [Fact]
    public void AMaterialWithoutAName_IsLeftOut()
    {
        ChangeReport report = Find(Data(new[] { new LinkObjectMaterial("BT-1101-01", null, "  ", null, null) }), NothingToAsk);

        Assert.True(report.IsUpToDate);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void Apply_GivesTheListedElementsTheMaterial_AndNoOthers()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13) }, new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500A") });
        ChangeReport listed = Find(data, (id, name) => id == 1 ? ValueCheck.Differs("") : ValueCheck.Same);

        ApplyPlan plan = ApplyPlan.For(data, listed.Groups);

        Assert.False(plan.IsEmpty);
        Assert.Equal(0, plan.Outdated);
        PlannedValues planned = Assert.Single(plan.Objects);
        Assert.Equal(("BT-1101-01", "13-Beton, C20/25"), (planned.Key, planned.Material));
        Assert.Empty(planned.Values);
        Assert.True(plan.IsMaterialListed("bt-1101-01", 1));
        Assert.False(plan.IsMaterialListed("BT-1101-01", 2));
        Assert.False(plan.IsMaterialListed("ET-2101-01", 1));
    }

    [Fact]
    public void WithoutAMaterialInTheList_NoneIsPlanned()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13) }, new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500A") });
        ChangeReport listed = Find(data, (id, name) => ValueCheck.Same, values: (id, parameter, value) => ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(data, listed.Groups);

        Assert.Null(Assert.Single(plan.Objects).Material);
        Assert.False(plan.IsMaterialListed("BT-1101-01", 1));
    }

    [Fact]
    public void AMaterialTheDatabaseHasChangedSince_IsLeftOut_AsNobodyHasSeenTheNewOne()
    {
        LinkProjectData data = Data(new[] { MadeOf("BT-1101-01", Beton13) });
        ChangeReport listed = Find(data, (id, name) => ValueCheck.Differs(""));

        foreach (LinkObjectMaterial[] now in new[]
                 {
                     new[] { MadeOf("BT-1101-01", Beton31) },
                     Array.Empty<LinkObjectMaterial>(),
                     new[] { MadeOf("BT-1101-01", Beton13), MadeOf("BT-1101-01", Beton31) },
                     new[] { new LinkObjectMaterial("BT-1101-01", Beton13.MaterialId, "13-Beton, C20/25, renamed", null, null) },
                     // Another one of that name, which the pane wouldn't list.
                     new[] { new LinkObjectMaterial("BT-1101-01", "MP-9999-99-BET-0101", Beton13.Name, null, null) },
                 })
        {
            ApplyPlan plan = ApplyPlan.For(Data(now), listed.Groups);

            Assert.True(plan.IsEmpty);
            Assert.Equal(1, plan.Outdated);
            Assert.False(plan.IsMaterialListed("BT-1101-01", 1));
        }
    }

    [Fact]
    public void AMaterialThatsStillTheSame_IsPlanned_WhoeverSetItAgain()
    {
        ChangeReport listed = Find(Data(new[] { MadeOf("BT-1101-01", Beton13, Monday, "TheAdmin") }), (id, name) => ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(Data(new[] { new LinkObjectMaterial("bt-1101-01", " mb-1761-01-bet-0013", Beton13.Name + " ", Today, "S. Brandt") }), listed.Groups);

        Assert.Equal("13-Beton, C20/25", Assert.Single(plan.Objects).Material);
        Assert.Equal(0, plan.Outdated);
    }

    [Fact]
    public void AMaterialWithoutAnId_IsPlannedByItsName()
    {
        LinkProjectData data = Data(new[] { new LinkObjectMaterial("BT-1101-01", null, "Ohne Nummer", null, null) });

        ApplyPlan plan = ApplyPlan.For(data, Find(data, (id, name) => ValueCheck.Differs("")).Groups);

        Assert.Equal("Ohne Nummer", Assert.Single(plan.Objects).Material);
    }

    [Fact]
    public void WhatWentIn_CountsAMaterialAsOneChangeOfTheObject()
    {
        var tally = new ApplyTally();
        tally.WrittenMaterial("BT-1101-01", 1);
        tally.WrittenMaterial("bt-1101-01 ", 2);
        tally.Written("BT-1101-01", false, UlmData.Guete.Guid, 1);

        ApplyResult result = tally.Result(0);

        Assert.Equal((2, 2, 0), (result.Changes, result.Elements, result.Materials));
        Assert.Equal("2 changes applied to 2 elements", ApplyTexts.AppliedTitle(result));
    }

    [Fact]
    public void TheQuestionBeforeApplying_CountsTheElements()
    {
        ChangeReport report = Find(Data(new[] { MadeOf("BT-1101-01", Beton13) }), (id, name) => ValueCheck.Differs(""));

        Assert.StartsWith("They go into 2 elements of this model", ApplyTexts.ConfirmText(report, "1761-01 Ulm"));
    }
}
