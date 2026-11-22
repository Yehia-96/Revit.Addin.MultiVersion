using MH.RevitTools.Database.Parameters;
using System.Windows;
using System.Windows.Media;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>One line of the Check parameters table, ready to bind: texts and the status pill's colours.</summary>
    public sealed class ParameterRowView
    {
        private static readonly Brush GreenBackground = DialogText.Frozen(0xE7, 0xF5, 0xEC);
        private static readonly Brush GreenText = DialogText.Frozen(0x1A, 0x7A, 0x43);
        private static readonly Brush AmberBackground = DialogText.Frozen(0xFD, 0xF3, 0xE1);
        private static readonly Brush AmberText = DialogText.Frozen(0x8F, 0x5B, 0x0A);
        private static readonly Brush RedBackground = DialogText.Frozen(0xFC, 0xEB, 0xE9);
        private static readonly Brush RedText = DialogText.Frozen(0xA3, 0x30, 0x22);
        private static readonly Brush GreyBackground = DialogText.Frozen(0xEC, 0xEE, 0xF2);
        private static readonly Brush GreyText = DialogText.Frozen(0x5B, 0x65, 0x78);

        public ParameterRowView(ParameterCheckRow row, string categoryLabel)
        {
            Name = row.NameInRevit;
            Note = row.Note;
            Key = row.Key;
            Category = categoryLabel;
            State = ParameterCheckTexts.State(row.State);

            switch (row.State)
            {
                case ParameterState.Missing:
                    PillBackground = AmberBackground;
                    PillForeground = AmberText;
                    break;
                case ParameterState.OtherGuid:
                    PillBackground = RedBackground;
                    PillForeground = RedText;
                    break;
                case ParameterState.CantAdd:
                    PillBackground = GreyBackground;
                    PillForeground = GreyText;
                    break;
                default:
                    PillBackground = GreenBackground;
                    PillForeground = GreenText;
                    break;
            }
        }

        public string Name { get; }

        public string Note { get; }

        public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;

        public string Key { get; }

        public string Category { get; }

        public string State { get; }

        public Brush PillBackground { get; }

        public Brush PillForeground { get; }
    }
}
