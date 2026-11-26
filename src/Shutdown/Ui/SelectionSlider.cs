using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Shutdown.Ui
{
    /// <summary>
    /// Draws the selection of a row of toggles as one accent shape that slides to the newly checked item.
    /// The toggles inside keep a transparent background while checked; their template part "Bd" gives the shape's
    /// bounds and corner radius. <see cref="ItemFill"/> paints the resting background of each item underneath the
    /// accent shape, for items that are separate pills rather than parts of one segmented bar.
    /// </summary>
    public sealed class SelectionSlider : Decorator
    {
        public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
            nameof(IndicatorBrush), typeof(Brush), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ItemFillProperty = DependencyProperty.Register(
            nameof(ItemFill), typeof(Brush), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        static readonly DependencyProperty IndicatorRectProperty = DependencyProperty.Register(
            "IndicatorRect", typeof(Rect), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        static readonly DependencyProperty IndicatorOpacityProperty = DependencyProperty.Register(
            "IndicatorOpacity", typeof(double), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        static readonly Duration SlideTime = new Duration(TimeSpan.FromMilliseconds(180));
        static readonly IEasingFunction Ease = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

        /// <summary>Off in the window tests, so the shape is at its target at once.</summary>
        internal static bool Animations = true;

        readonly List<Rect> _itemRects = new List<Rect>();
        double _radius;
        Rect _target = Rect.Empty;

        public SelectionSlider()
        {
            SetResourceReference(IndicatorBrushProperty, "Accent");
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnSelectionChanged));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnSelectionChanged));
            LayoutUpdated += (s, e) => { if (!_selectionPending) Update(false); };
        }

        bool _selectionPending;

        // One change can uncheck the old item before checking the new one (preset chips, radio groups):
        // wait until both have happened, so the shape slides instead of fading out and back in.
        void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_selectionPending) return;
            _selectionPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _selectionPending = false;
                Update(true);
            }));
        }

        public Brush IndicatorBrush
        {
            get => (Brush)GetValue(IndicatorBrushProperty);
            set => SetValue(IndicatorBrushProperty, value);
        }

        public Brush ItemFill
        {
            get => (Brush)GetValue(ItemFillProperty);
            set => SetValue(ItemFillProperty, value);
        }

        /// <summary>Where the accent shape is drawn right now (mid-slide while it moves).</summary>
        internal Rect IndicatorRect => (Rect)GetValue(IndicatorRectProperty);

        internal bool IndicatorShown => (double)GetValue(IndicatorOpacityProperty) > 0;

        /// <summary>Where the accent shape is heading: the checked item's bounds, or empty when nothing is checked.</summary>
        internal Rect IndicatorTarget => _target;

        void Update(bool selectionChanged)
        {
            Rect target = Rect.Empty;
            var rects = new List<Rect>();
            if (Child is Panel panel && Visibility == Visibility.Visible)
            {
                foreach (UIElement element in panel.Children)
                {
                    var toggle = FindToggle(element);
                    if (toggle == null || toggle.Visibility != Visibility.Visible) continue;
                    var part = toggle.Template?.FindName("Bd", toggle) as FrameworkElement ?? toggle;
                    if (part.ActualWidth <= 0 || !IsAncestorOf(part)) continue;
                    var bounds = part.TransformToAncestor(this).TransformBounds(new Rect(part.RenderSize));
                    rects.Add(bounds);
                    if (part is Border border) _radius = border.CornerRadius.TopLeft;
                    if (toggle.IsChecked == true) target = bounds;
                }
            }

            if (!SameRects(rects))
            {
                _itemRects.Clear();
                _itemRects.AddRange(rects);
                InvalidateVisual();
            }
            if (Near(target, _target)) return;

            bool wasShown = !_target.IsEmpty;
            _target = target;
            // A move caused by layout (language, window, mode) jumps; only a new selection slides.
            bool animate = selectionChanged && Animations && SystemParameters.ClientAreaAnimation && IsLoaded;
            if (target.IsEmpty)
            {
                Animate(IndicatorOpacityProperty, animate ? new DoubleAnimation(0, SlideTime) : null, 0.0);
                return;
            }
            if (!wasShown || !animate)
            {
                Animate(IndicatorRectProperty, null, target);
                Animate(IndicatorOpacityProperty, animate ? new DoubleAnimation(1, SlideTime) : null, 1.0);
                return;
            }
            Animate(IndicatorOpacityProperty, null, 1.0);
            Animate(IndicatorRectProperty, new RectAnimation(target, SlideTime) { EasingFunction = Ease }, target);
        }

        void Animate(DependencyProperty property, AnimationTimeline animation, object end)
        {
            if (animation == null)
            {
                BeginAnimation(property, null);
                SetValue(property, end);
                return;
            }
            // Start from what is drawn now, mid-slide included.
            object now = GetValue(property);
            if (animation is DoubleAnimation fade)
            {
                fade.From = (double)now;
                fade.EasingFunction = Ease;
            }
            else if (animation is RectAnimation slide) slide.From = (Rect)now;
            // The base value is the end state, so clearing the animation later keeps the shape where it went.
            SetValue(property, end);
            BeginAnimation(property, animation);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var fill = ItemFill;
            if (fill != null)
                foreach (var rect in _itemRects)
                    dc.DrawRoundedRectangle(fill, null, rect, _radius, _radius);

            double opacity = (double)GetValue(IndicatorOpacityProperty);
            var indicator = IndicatorRect;
            if (opacity <= 0 || indicator.IsEmpty || IndicatorBrush == null) return;
            dc.PushOpacity(opacity);
            dc.DrawRoundedRectangle(IndicatorBrush, null, indicator, _radius, _radius);
            dc.Pop();
        }

        // A toggle can sit in a small wrapper (an info button beside it), so look one level down too.
        static ToggleButton FindToggle(UIElement element)
        {
            if (element is ToggleButton toggle) return toggle;
            if (element is Panel panel)
                foreach (UIElement child in panel.Children)
                    if (child is ToggleButton inner) return inner;
            return null;
        }

        bool SameRects(List<Rect> rects)
        {
            if (rects.Count != _itemRects.Count) return false;
            for (int i = 0; i < rects.Count; i++)
                if (!Near(rects[i], _itemRects[i])) return false;
            return true;
        }

        static bool Near(Rect a, Rect b)
        {
            if (a.IsEmpty || b.IsEmpty) return a.IsEmpty == b.IsEmpty;
            return Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5
                && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;
        }

        static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
