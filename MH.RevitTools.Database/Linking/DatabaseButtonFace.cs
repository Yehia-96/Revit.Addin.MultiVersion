using System;
using MH.RevitTools.Database.Checking;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>The dot on the Database button's icon, as in the mockup's ribbon.</summary>
    public enum ButtonDot
    {
        /// <summary>No dot: no project model, or a model that isn't linked.</summary>
        None,

        /// <summary>Linked, but nothing known: being checked, nobody signed in, or the PC has no connection file.</summary>
        Grey,

        /// <summary>The model matches the database.</summary>
        Green,

        /// <summary>Changes from the database are waiting; the dot carries their number.</summary>
        Blue,

        /// <summary>The database can't be reached, or no longer has the model's project.</summary>
        Red,
    }

    /// <summary>What the pane's last check found, for the Database button.</summary>
    public enum LinkCondition
    {
        /// <summary>Not checked yet.</summary>
        Checking,

        NotSetUp,

        /// <summary>Nobody is signed in, so nothing is checked.</summary>
        NotSignedIn,

        UpToDate,
        Changes,
        NotReachable,

        /// <summary>The database has no project with the model's id, or the id means another project now.</summary>
        ProjectProblem,
    }

    /// <summary>
    /// What the ribbon's big Database button shows for the active model. As in the mockup, "Database" with a
    /// second line under it: the project's number once the model is linked (Yehia, 2026-11-22), or "Link
    /// model" while it isn't; only "Database" while no project model is active. The icon's dot shows the
    /// pane's state: green up to date, blue with the number of changes, red not reachable, grey while it's
    /// unknown. Immutable.
    /// </summary>
    public sealed class DatabaseButtonFace : IEquatable<DatabaseButtonFace>
    {
        private const string Name = "Database";
        private const string Click = " Click to show or hide the MH Database pane.";

        private DatabaseButtonFace(ButtonDot dot, int changes, string text, string toolTip)
        {
            Dot = dot;
            Changes = dot == ButtonDot.Blue ? changes : 0;
            Text = text;
            ToolTip = toolTip;
        }

        /// <summary>No project model is active: none is open, or the family editor is.</summary>
        public static DatabaseButtonFace NoModel { get; } = new DatabaseButtonFace(ButtonDot.None, 0, Name,
            "Shows the MH Database pane, which links a project model to its project in the MH database and lists "
            + "the database's changes. Open a project model first.");

        public ButtonDot Dot { get; }

        /// <summary>With the blue dot: how many changes are waiting.</summary>
        public int Changes { get; }

        /// <summary>The button's text: "Database", then the second line, e.g. "Database\n1761-01".</summary>
        public string Text { get; }

        public string ToolTip { get; }

        /// <param name="link">The link saved in the active project model; null when it has none.</param>
        /// <param name="timeZone">For when it was linked.</param>
        /// <param name="condition">What the pane's last check found for the model.</param>
        /// <param name="changes">With <see cref="LinkCondition.Changes"/>: how many.</param>
        public static DatabaseButtonFace For(ModelLink link, TimeZoneInfo timeZone, LinkCondition condition, int changes)
        {
            if (link == null)
            {
                return new DatabaseButtonFace(ButtonDot.None, 0, Name + "\nLink model",
                    "This model isn't linked to a project in the MH database yet. Click to show the MH Database pane "
                    + "and link it; the link is saved in the model.");
            }

            ButtonDot dot;
            string state;
            switch (condition)
            {
                case LinkCondition.UpToDate:
                    dot = ButtonDot.Green;
                    state = "The model matches the database.";
                    break;
                case LinkCondition.Changes:
                    dot = changes > 0 ? ButtonDot.Blue : ButtonDot.Green;
                    state = changes > 0
                        ? PaneTexts.ChangesTitle(changes) + (changes == 1 ? " is" : " are") + " waiting."
                        : "The model matches the database.";
                    break;
                case LinkCondition.NotReachable:
                    dot = ButtonDot.Red;
                    state = "The database isn't reachable; the model keeps its values.";
                    break;
                case LinkCondition.ProjectProblem:
                    dot = ButtonDot.Red;
                    state = "The database no longer has this model's project.";
                    break;
                case LinkCondition.NotSetUp:
                    dot = ButtonDot.Grey;
                    state = "This computer has no connection file, so nothing is checked.";
                    break;
                case LinkCondition.NotSignedIn:
                    dot = ButtonDot.Grey;
                    state = "Nobody is signed in, so nothing is checked: use Sign in… in this panel.";
                    break;
                default:
                    dot = ButtonDot.Grey;
                    state = "Checking the database…";
                    break;
            }

            string linked = link.Linked(timeZone);
            return new DatabaseButtonFace(dot, changes, Name + "\n" + ProjectCode.Format(link.Number, link.SubNumber),
                "This model is linked to " + link.Describe() + " in the MH database."
                + (linked.Length == 0 ? "" : " " + linked)
                + " " + state + Click);
        }

        public bool Equals(DatabaseButtonFace other) =>
            other != null && Dot == other.Dot && Changes == other.Changes
            && string.Equals(Text, other.Text, StringComparison.Ordinal)
            && string.Equals(ToolTip, other.ToolTip, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as DatabaseButtonFace);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text) ^ ((int)Dot << 16) ^ Changes;

        public override string ToString() => Text.Replace('\n', ' ');
    }
}
