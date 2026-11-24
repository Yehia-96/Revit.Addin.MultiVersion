using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using MH.RevitTools.Database.Connection;
using Newtonsoft.Json.Linq;

namespace MH.RevitTools.Database.Tests.Connection;

/// <summary>
/// The connection file that ships inside the add-in, Revit.Addin.2027\database.json. It's built into these tests the same
/// way. Everybody who has the add-in can read it.
/// </summary>
public sealed class ShippedConnectionFileTests
{
    private static Assembly BuiltWithIt => typeof(ShippedConnectionFileTests).Assembly;

    private static string NoOwnFile => Path.Combine(AppContext.BaseDirectory, "no such folder", "database.json");

    private static string Content()
    {
        using Stream stream = BuiltWithIt.GetManifestResourceStream(ConnectionFile.FileName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Exactly these, so no password gets in under any name.
    [Fact]
    public void ItHoldsTheConnection_AndNothingElse()
    {
        string[] expected = { "database", "encrypt", "login", "masterParameterFile", "server", "trustServerCertificate" };

        Assert.Equal(expected, JObject.Parse(Content()).Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ItIsComplete_ButForThePassword()
    {
        ConnectionFileResult file = ConnectionFile.ReadFirst(NoOwnFile, BuiltWithIt);

        Assert.True(file.IsLoaded, file.Problem);
        Assert.Equal(ConnectionFile.BuiltIn, file.Source);
        Assert.True(file.Settings.NeedsPassword);
        Assert.False(file.Settings.WindowsSignIn);
    }

    // As the database session confirmed it on 2026-11-24. It can't be corrected without a new version of the add-in, so
    // a change here has to be meant: a developer's server, say, must not go out with a release.
    [Fact]
    public void ItNamesTheOfficesDatabase()
    {
        ConnectionFileResult file = ConnectionFile.ReadFirst(NoOwnFile, BuiltWithIt);

        Assert.Equal("CADSQL.mh-frlg.local,1433", file.Settings.Server);
        Assert.Equal("MH_Database", file.Settings.Database);
        Assert.Equal("revit_reader", file.Settings.Login);
        Assert.True(file.Settings.Encrypt);
        Assert.True(file.Settings.TrustServerCertificate);
        Assert.Equal(@"\\CADSQL.mh-frlg.local\PLDB\01.Datenbank\MH_Parameter.txt", file.MasterParameterFile);
    }

    // Built in, under the name the add-in looks for, and nowhere copied: a separate file wouldn't get through Check for
    // Updates, which takes DLLs only, and would lie on the share.
    [Fact]
    public void TheAddinIsBuiltWithIt()
    {
        XDocument project = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Connection", "Revit.Addin.2027.csproj"));

        XElement item = Assert.Single(
            project.Descendants(),
            element => (string)element.Attribute("Include") == ConnectionFile.FileName || (string)element.Attribute("Update") == ConnectionFile.FileName);

        Assert.Equal("EmbeddedResource", item.Name.LocalName);
        Assert.Equal(ConnectionFile.FileName, (string)item.Attribute("LogicalName"));
        Assert.Null(item.Attribute("CopyToOutputDirectory"));
        Assert.Empty(item.Elements());
    }
}
