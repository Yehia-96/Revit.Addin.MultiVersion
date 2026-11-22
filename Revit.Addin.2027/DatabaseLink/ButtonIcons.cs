using MH.RevitTools.Database.Linking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// The Database button's icons with the state dot, drawn once each from the plain icon, as the mockup's
    /// ribbon has them: a dot with a clear ring cut out of the glyph, so it stands off it on any ribbon
    /// colour. Green up to date, blue with the number of changes, red not reachable, grey unknown. The dot
    /// sits where the green dot of 2026-11-22 sat. Main thread only.
    /// </summary>
    internal sealed class ButtonIcons
    {
        private static readonly Color GreenDot = Color.FromRgb(0x2D, 0xA4, 0x4E);
        private static readonly Color BlueDot = Color.FromRgb(0x1F, 0x6F, 0xEB);
        private static readonly Color RedDot = Color.FromRgb(0xD1, 0x24, 0x2F);
        private static readonly Color GreyDot = Color.FromRgb(0x8C, 0x95, 0xA5);

        private readonly ImageSource _small;
        private readonly ImageSource _large;
        private readonly Dictionary<string, ImageSource> _drawn = new Dictionary<string, ImageSource>(StringComparer.Ordinal);

        /// <param name="small">The plain 16 px icon.</param>
        /// <param name="large">The plain 32 px icon.</param>
        public ButtonIcons(ImageSource small, ImageSource large)
        {
            _small = small;
            _large = large;
        }

        public ImageSource Small(ButtonDot dot) =>
            dot == ButtonDot.None || _small == null ? _small : Drawn("16-" + dot, () => Draw(_small, 16, new Point(12.75, 3.25), 2.75, 1, ColorOf(dot), null));

        /// <param name="changes">With the blue dot: the number it carries; more than 9 shows as 9+.</param>
        public ImageSource Large(ButtonDot dot, int changes)
        {
            if (dot == ButtonDot.None || _large == null)
                return _large;
            if (dot != ButtonDot.Blue || changes <= 0)
                return Drawn("32-" + dot, () => Draw(_large, 32, new Point(26.5, 5.5), 4.5, 1.5, ColorOf(dot), null));

            // A number needs a bigger dot than the plain one.
            string number = changes > 9 ? "9+" : changes.ToString(CultureInfo.InvariantCulture);
            return Drawn("32-Blue-" + number, () => Draw(_large, 32, new Point(25.5, 6.5), 6.5, 1.5, BlueDot, number));
        }

        private ImageSource Drawn(string key, Func<ImageSource> draw)
        {
            ImageSource image;
            if (!_drawn.TryGetValue(key, out image))
            {
                image = draw();
                _drawn.Add(key, image);
            }
            return image;
        }

        private static Color ColorOf(ButtonDot dot)
        {
            switch (dot)
            {
                case ButtonDot.Green:
                    return GreenDot;
                case ButtonDot.Blue:
                    return BlueDot;
                case ButtonDot.Red:
                    return RedDot;
                default:
                    return GreyDot;
            }
        }

        private static ImageSource Draw(ImageSource plain, int size, Point centre, double radius, double ring, Color color, string label)
        {
            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                var all = new RectangleGeometry(new Rect(0, 0, size, size));
                var hole = new EllipseGeometry(centre, radius + ring, radius + ring);
                context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, all, hole));
                context.DrawImage(plain, new Rect(0, 0, size, size));
                context.Pop();

                var fill = new SolidColorBrush(color);
                fill.Freeze();
                context.DrawEllipse(fill, null, centre, radius, radius);
                if (label != null)
                {
                    var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        label.Length > 1 ? 7.5 : 9, Brushes.White, 1.0);
                    context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
