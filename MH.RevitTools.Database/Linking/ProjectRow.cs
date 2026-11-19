using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>One line of the Link dialog's project list.</summary>
    public sealed class ProjectRow
    {
        public ProjectRow(LinkProject project, string code, string name, string note)
        {
            Project = project;
            Code = code ?? "";
            Name = name ?? "";
            Note = note ?? "";
        }

        public LinkProject Project { get; }

        /// <summary>E.g. "1761-01".</summary>
        public string Code { get; }

        public string Name { get; }

        /// <summary>"Linked", "Suggested", "Same number", or empty.</summary>
        public string Note { get; }

        public override string ToString() => Code + " " + Name;
    }
}
