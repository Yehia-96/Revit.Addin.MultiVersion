using MH.RevitTools.Database.Linking;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>What the database link's dialogs share: drawing text parts, and the mockup's colours.</summary>
    internal static class DialogText
    {
        public static readonly Brush InfoBackground = Frozen(0xF5, 0xF9, 0xFF);
        public static readonly Brush InfoBorder = Frozen(0xA9, 0xC5, 0xF7);
        public static readonly Brush InfoIcon = Frozen(0x1F, 0x6F, 0xEB);
        public static readonly Brush WarningBackground = Frozen(0xFD, 0xF3, 0xE1);
        public static readonly Brush WarningBorder = Frozen(0xEB, 0xCB, 0x8B);
        public static readonly Brush WarningText = Frozen(0x8F, 0x5B, 0x0A);
        public static readonly Brush MutedText = Frozen(0x5B, 0x65, 0x78);
        public static readonly Brush CodeText = Frozen(0x2A, 0x34, 0x47);

        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas");

        /// <summary>Fills a text block with the parts: strong ones in semibold, code in a fixed-width font.</summary>
        public static void Show(TextBlock target, IEnumerable<TextPart> parts)
        {
            target.Inlines.Clear();
            foreach (TextPart part in parts)
            {
                var run = new Run(part.Text);
                if (part.Style == TextStyle.Strong)
                {
                    run.FontWeight = FontWeights.SemiBold;
                }
                else if (part.Style == TextStyle.Code)
                {
                    run.FontFamily = CodeFont;
                    run.FontSize = 12;
                    run.Foreground = CodeText;
                }
                target.Inlines.Add(run);
            }
        }

        public static Brush Frozen(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
