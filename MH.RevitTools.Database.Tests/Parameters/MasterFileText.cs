using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

/// <summary>Master shared parameter files written the way Studio writes MH_Parameter.txt (REVIT_LINK.md).</summary>
internal static class MasterFileText
{
    public const string Header = "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE";

    public static readonly DateTime WrittenAtUtc = new(2026, 11, 18, 23, 30, 0, DateTimeKind.Utc);

    /// <summary>A file holding these parameters, with Studio's header lines and one file group.</summary>
    public static string With(params (Guid Guid, string Name, string DataType)[] parameters)
    {
        var lines = new List<string>
        {
            "# This is a Revit shared parameter file.",
            "# Do not edit manually.",
            "*META\tVERSION\tMINVERSION",
            "META\t2\t1",
            "*GROUP\tID\tNAME",
            "GROUP\t1\tBAU-GEO Bauteilinformation - Geometrie",
            Header,
        };
        lines.AddRange(parameters.Select(p => string.Join("\t",
            "PARAM", p.Guid.ToString("D"), p.Name, p.DataType, "", "1", "1", "BAU-GEO-" + p.Name, "1", "0")));
        return string.Join("\r\n", lines) + "\r\n";
    }

    public static MasterParameterFile Read(string text) =>
        MasterParameterFile.Read(@"C:\share\MH_Parameter.txt", WrittenAtUtc, new StringReader(text));
}
