using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Shutdown.Ui
{
    /// <summary>
    /// An icon-font glyph drawn as a vector shape. WPF smooths text only horizontally, so at small sizes the
    /// round bottoms of icons (stopwatch, restart arrow) snap to a flat, jagged pixel row; a shape is smoothed
    /// in both directions. Decorative: hidden from screen readers, the owning control carries the name.
    /// </summary>
    public sealed class IconGlyph : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(IconGlyph),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(IconGlyph),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(IconGlyph),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(IconGlyph),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
        public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        FormattedText Format() => new FormattedText(Text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), FontSize, Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // The same box a TextBlock would take, so layouts keep their spacing.
        protected override Size MeasureOverride(Size availableSize)
        {
            if (string.IsNullOrEmpty(Text)) return new Size(0, 0);
            var ft = Format();
            return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(Text)) return;
            dc.DrawGeometry(Foreground, null, Format().BuildGeometry(new Point(0, 0)));
        }

        protected override AutomationPeer OnCreateAutomationPeer() => null;
    }
}
