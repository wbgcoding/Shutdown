using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shutdown.Ui
{
    /// <summary>
    /// Filled "i" badge in the accent colour, drawn as a vector so it stays sharp where the icon font's
    /// outline glyph turns soft at small sizes.
    /// </summary>
    public sealed class InfoBadge : Viewbox
    {
        // 16 x 16 design box: full circle, dot above a short stem.
        static readonly Geometry Mark = Freeze(Geometry.Parse("M 8,7.2 L 8,11.6"));
        static readonly Geometry Dot = Freeze(new EllipseGeometry(new Point(8, 4.7), 1.05, 1.05));

        public InfoBadge()
        {
            Width = 16;
            Height = 16;
            VerticalAlignment = VerticalAlignment.Center;
            var circle = new Ellipse { Width = 16, Height = 16 };
            circle.SetResourceReference(Shape.FillProperty, "Accent");
            var stem = new Path { Data = Mark, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            stem.SetResourceReference(Shape.StrokeProperty, "AccentText");
            var dot = new Path { Data = Dot };
            dot.SetResourceReference(Shape.FillProperty, "AccentText");
            var canvas = new Canvas { Width = 16, Height = 16 };
            canvas.Children.Add(circle);
            canvas.Children.Add(stem);
            canvas.Children.Add(dot);
            Child = canvas;
        }

        static Geometry Freeze(Geometry g)
        {
            g.Freeze();
            return g;
        }
    }
}
