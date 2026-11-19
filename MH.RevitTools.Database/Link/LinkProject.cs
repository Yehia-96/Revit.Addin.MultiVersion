namespace MH.RevitTools.Database.Link
{
    /// <summary>One project in the MH database.</summary>
    public sealed class LinkProject
    {
        public LinkProject(int projectId, int number, int subNumber, string name)
        {
            ProjectId = projectId;
            Number = number;
            SubNumber = subNumber;
            Name = name ?? "";
        }

        /// <summary>The database's id for the project; what a linked model stores.</summary>
        public int ProjectId { get; }

        /// <summary>E.g. 1761.</summary>
        public int Number { get; }

        /// <summary>E.g. 1 for 1761-01.</summary>
        public int SubNumber { get; }

        public string Name { get; }
    }
}
