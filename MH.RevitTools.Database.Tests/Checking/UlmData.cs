using System;
using System.Collections.Generic;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Checking;

/// <summary>A small project 1761-01 Ulm like dev MH_Database's, for the check and the change list.</summary>
internal static class UlmData
{
    public const int ProjectId = 13;

    private static readonly string[] GenericModels = { "OST_GenericModel" };
    private static readonly string[] Materials = { "OST_Materials" };

    public static readonly LinkParameter Dicke = new(61, new Guid("6f0c4b8e-0000-4000-8000-000000000061"), "Dicke", "BAU", "GEO", "Dicke", "Double", "m", LinkTarget.Object, GenericModels);
    public static readonly LinkParameter Guete = new(70, new Guid("6f0c4b8e-0000-4000-8000-000000000070"), "Betonstahl Güte", "BAU", "SPE", "Betonstahl Güte", "String", null, LinkTarget.Object, GenericModels);
    public static readonly LinkParameter Bewehrt = new(80, new Guid("6f0c4b8e-0000-4000-8000-000000000080"), "Bewehrt", "BAU", "SPE", "Bewehrt", "Boolean", null, LinkTarget.Object, GenericModels);
    public static readonly LinkParameter Bauteil = new(103, new Guid("6f0c4b8e-0000-4000-8000-000000000103"), "Bauteil", "BAU", "KAT", "Bauteil", "String", null, LinkTarget.Object, GenericModels);
    public static readonly LinkParameter Teilelement = new(104, new Guid("6f0c4b8e-0000-4000-8000-000000000104"), "Teilelement", "BAU", "KAT", "Teilelement", "String", null, LinkTarget.Object, GenericModels);
    public static readonly LinkParameter BauteilId = new(97, new Guid("6f0c4b8e-0000-4000-8000-000000000097"), "Bauteil_ID", "IDE", "KAT", "Bauteil_ID", "String", null, LinkTarget.Object, GenericModels);
    public static readonly LinkParameter MaterialId = new(34, new Guid("6f0c4b8e-0000-4000-8000-000000000034"), "Material-ID", "IDE", "KAT", "Material-ID", "String", null, LinkTarget.Material, Materials);
    public static readonly LinkParameter Festigkeit = new(120, new Guid("6f0c4b8e-0000-4000-8000-000000000120"), "Festigkeitsklasse", "MAT", "BET", "Festigkeitsklasse", "String", null, LinkTarget.Material, Materials);

    public static readonly IReadOnlyList<LinkParameter> Dictionary = new[] { Dicke, Guete, Bewehrt, Bauteil, Teilelement, BauteilId, MaterialId, Festigkeit };

    public static readonly LinkProject Project = new(ProjectId, 1761, 1, "Ulm");

    public static LinkProjectData With(long stamp, IEnumerable<LinkObjectValue> objectValues,
        IEnumerable<LinkMaterial> materials = null, IEnumerable<LinkMaterialValue> materialValues = null) => new(
        ProjectId, stamp, Dictionary, objectValues,
        materials ?? Array.Empty<LinkMaterial>(), materialValues ?? Array.Empty<LinkMaterialValue>());

    public static LinkObjectValue Value(string bauteilId, LinkParameter parameter, string value,
        DateTime? changedAtUtc = null, string changedBy = null) =>
        new(bauteilId, parameter.ParameterId, value, changedAtUtc, changedBy);

    /// <summary>Bodenplatte BT-1101-01 with a few values, changed by TheAdmin on 2026-10-22.</summary>
    public static LinkProjectData Bodenplatte(long stamp, string guete = "B500B")
    {
        DateTime at = new(2026, 10, 22, 14, 54, 39, DateTimeKind.Utc);
        return With(stamp, new[]
        {
            Value("BT-1101-01", Bauteil, "Bodenplatte", at, "TheAdmin"),
            Value("BT-1101-01", Guete, guete, at, "TheAdmin"),
            Value("BT-1101-01", Dicke, "[Modeller]", at, "TheAdmin"),
        });
    }
}
