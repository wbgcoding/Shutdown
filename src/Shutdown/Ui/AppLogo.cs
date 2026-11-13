using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shutdown.Ui
{
    /// <summary>
    /// The app icon drawn as a vector (same shape as tools/make-icon.py at large sizes), so the title bar logo
    /// stays sharp at every display scale.
    /// </summary>
    public sealed class AppLogo : Viewbox
    {
        // 32 x 32 design box: tile radius 0.2 s, ring outer radius 0.34 s, stroke 0.095 s, opening 42 degrees
        // either side of the top, stem from 1.2 to 0.3 ring radii above the ring centre.
        static readonly Geometry Symbol = Freeze(Geometry.Parse(
            "M 9.737,10.132 A 9.36,9.36 0 1 0 22.263,10.132 M 16,5.552 L 16,12.304"));

        static readonly Brush Tile = Freeze(new LinearGradientBrush(
            Color.FromRgb(0x1A, 0x94, 0xFF), Color.FromRgb(0x00, 0x5F, 0xB8), new Point(0, 0), new Point(0, 1)));

        public AppLogo()
        {
            var canvas = new Canvas { Width = 32, Height = 32 };
            canvas.Children.Add(new Rectangle { Width = 32, Height = 32, RadiusX = 6.4, RadiusY = 6.4, Fill = Tile });
            canvas.Children.Add(new Path
            {
                Data = Symbol,
                Stroke = Brushes.White,
                StrokeThickness = 3.04,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
            Child = canvas;
        }

        static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }
}
