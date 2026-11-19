using System.Collections.Generic;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Linking;

/// <summary>A few projects like dev MH_Database's, plus a second Ulm so a number can fit two projects.</summary>
internal static class TestProjects
{
    public static readonly LinkProject Pfaffensteig = new(5, 1547, 13, "Pfaffensteigtunnel");
    public static readonly LinkProject Muenchen = new(1, 1760, 1, "2. S-Bahn-Stammstrecke München");
    public static readonly LinkProject Ulm = new(13, 1761, 1, "Ulm");
    public static readonly LinkProject UlmSecond = new(20, 1761, 2, "Ulm, 2. Bauabschnitt");
    public static readonly LinkProject Brenner = new(4, 1821, 1, "Brenner Basistunnel");

    /// <summary>In the database's order: by number, then sub-number.</summary>
    public static readonly IReadOnlyList<LinkProject> All = new[] { Pfaffensteig, Muenchen, Ulm, UlmSecond, Brenner };
}
