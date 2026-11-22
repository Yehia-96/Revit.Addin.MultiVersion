using System;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// What the ribbon's big Database button shows for the active model. As in the mockup, "Database" with
    /// a second line under it: the project's number once the model is linked, with a green dot on the
    /// icon (Yehia, 2026-11-22), or "Link model" while it isn't. Only "Database" while no project model is
    /// active. Immutable.
    /// </summary>
    public sealed class DatabaseButtonFace : IEquatable<DatabaseButtonFace>
    {
        private const string Name = "Database";

        private DatabaseButtonFace(bool isLinked, string text, string toolTip)
        {
            IsLinked = isLinked;
            Text = text;
            ToolTip = toolTip;
        }

        /// <summary>No project model is active: none is open, or the family editor is.</summary>
        public static DatabaseButtonFace NoModel { get; } = new DatabaseButtonFace(false, Name,
            "Links a project model to its project in the MH database. Open a project model first.");

        /// <summary>True once the model is linked: the icon shows the green dot.</summary>
        public bool IsLinked { get; }

        /// <summary>The button's text: "Database", then the second line, e.g. "Database\n1761-01".</summary>
        public string Text { get; }

        public string ToolTip { get; }

        /// <param name="link">The link saved in the active project model; null when it has none.</param>
        /// <param name="timeZone">For when it was linked.</param>
        public static DatabaseButtonFace For(ModelLink link, TimeZoneInfo timeZone)
        {
            if (link == null)
            {
                return new DatabaseButtonFace(false, Name + "\nLink model",
                    "This model isn't linked to a project in the MH database yet. Click to link it; the link is "
                    + "saved in the model.");
            }

            string linked = link.Linked(timeZone);
            return new DatabaseButtonFace(true, Name + "\n" + ProjectCode.Format(link.Number, link.SubNumber),
                "This model is linked to " + link.Describe() + " in the MH database."
                + (linked.Length == 0 ? "" : " " + linked)
                + " Click to see the link or change it.");
        }

        public bool Equals(DatabaseButtonFace other) =>
            other != null && IsLinked == other.IsLinked
            && string.Equals(Text, other.Text, StringComparison.Ordinal)
            && string.Equals(ToolTip, other.ToolTip, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as DatabaseButtonFace);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text) ^ (IsLinked ? 1 : 0);

        public override string ToString() => Text.Replace('\n', ' ');
    }
}
