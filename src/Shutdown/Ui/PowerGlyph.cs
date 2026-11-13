using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shutdown.Ui
{
    /// <summary>
    /// Power symbol drawn as a vector in the inherited text colour. The icon font's glyph loses the bottom of its
    /// ring to pixel snapping at small sizes; a path stays round at every display scale.
    /// </summary>
    public sealed class PowerGlyph : Viewbox
    {
        // 16 x 16 design box: ring around (8, 9.5) open 35 degrees either side of the top, short stem into the gap.
        static readonly Geometry Outline = Freeze(Geometry.Parse("M 4.99,5.2 A 5.25,5.25 0 1 0 11.01,5.2 M 8,2.75 L 8,7.75"));

        public PowerGlyph()
        {
            Width = 15;
            Height = 15;
            VerticalAlignment = VerticalAlignment.Center;
            var path = new Path
            {
                Data = Outline,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            path.SetBinding(Shape.StrokeProperty, new Binding
            {
                Path = new PropertyPath(TextElement.ForegroundProperty),
                RelativeSource = RelativeSource.Self,
            });
            var canvas = new Canvas { Width = 16, Height = 16 };
            canvas.Children.Add(path);
            Child = canvas;
        }

        static Geometry Freeze(Geometry g)
        {
            g.Freeze();
            return g;
        }
    }
}
