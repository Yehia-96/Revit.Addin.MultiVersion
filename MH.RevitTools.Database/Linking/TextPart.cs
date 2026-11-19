namespace MH.RevitTools.Database.Linking
{
    /// <summary>How a piece of dialog text is shown.</summary>
    public enum TextStyle
    {
        Plain,

        /// <summary>Bold, e.g. a project or a number.</summary>
        Strong,

        /// <summary>In a fixed-width font, e.g. a file name.</summary>
        Code,
    }

    /// <summary>A piece of a dialog sentence, so the dialog can draw some words in bold without parsing text.</summary>
    public sealed class TextPart
    {
        private TextPart(string text, TextStyle style)
        {
            Text = text ?? "";
            Style = style;
        }

        public string Text { get; }

        public TextStyle Style { get; }

        public static TextPart Plain(string text) => new TextPart(text, TextStyle.Plain);

        public static TextPart Strong(string text) => new TextPart(text, TextStyle.Strong);

        public static TextPart Code(string text) => new TextPart(text, TextStyle.Code);

        public override string ToString() => Text;
    }
}
