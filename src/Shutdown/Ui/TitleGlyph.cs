using System;
using System.Windows;
using System.Windows.Media;

namespace Shutdown.Ui
{
    /// <summary>
    /// Title bar text centred on its capital letters instead of its line box, so it sits level with the logo beside it.
    /// The line box holds room for descenders, which pushes the visible letters about a pixel below the centre.
    /// </summary>
    public sealed class TitleGlyph : Glyph
    {
        // The font comes from the style, which is applied by the time the element is initialized.
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            CentreOnCapitals();
        }

        void CentreOnCapitals()
        {
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            if (!typeface.TryGetGlyphTypeface(out var glyphs)) return;
            double line = FontFamily.LineSpacing * FontSize;
            double capCentre = (FontFamily.Baseline - glyphs.CapsHeight / 2) * FontSize;
            // With VerticalAlignment="Center", a bottom margin of twice the offset moves the text up by the offset.
            Margin = new Thickness(0, 0, 0, 2 * (capCentre - line / 2));
        }
    }
}
