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
    /// bounds and corner radius. <see cref="ItemFill"/> and <see cref="ItemBorder"/> paint the resting look of each item
    /// underneath the accent shape, for items that are separate pills rather than parts of one segmented bar. Drawn
    /// here, an outline never sits on top of the sliding shape and never jumps to the new item ahead of it.
    /// </summary>
    /// <remarks>
    /// The shape is drawn once with animation clocks attached to its rectangle and opacity, so a slide only sends
    /// new values to the renderer each frame: no OnRender, layout or property change runs while it moves.
    /// </remarks>
    public sealed class SelectionSlider : Decorator
    {
        public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
            nameof(IndicatorBrush), typeof(Brush), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ItemFillProperty = DependencyProperty.Register(
            nameof(ItemFill), typeof(Brush), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ItemBorderProperty = DependencyProperty.Register(
            nameof(ItemBorder), typeof(Brush), typeof(SelectionSlider),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((SelectionSlider)d)._itemPen = null));

        static readonly Duration SlideTime = new Duration(TimeSpan.FromMilliseconds(200));
        static readonly IEasingFunction Ease = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

        /// <summary>Off in tests that need the shape at its target at once.</summary>
        internal static bool Animations = true;

        readonly List<Rect> _itemRects = new List<Rect>();
        readonly List<Rect> _scan = new List<Rect>();
        double _radius;
        Pen _itemPen;
        Rect _target = Rect.Empty;
        bool _selectionPending;

        // Each value is the end state plus an optional running clock that moves from a start value towards it.
        Rect _rect = Rect.Empty, _rectFrom;
        AnimationClock _rectClock;
        double _opacity, _opacityFrom;
        AnimationClock _opacityClock;

        public SelectionSlider()
        {
            SetResourceReference(IndicatorBrushProperty, "Accent");
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnSelectionChanged));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnSelectionChanged));
            LayoutUpdated += (s, e) => { if (!_selectionPending) Update(false); };
        }

        // One change can uncheck the old item before checking the new one (preset chips, radio groups):
        // wait until both have happened, so the shape slides instead of fading out and back in. Normal priority
        // runs before the next frame is drawn, so the slide starts in the same frame as the click.
        void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_selectionPending) return;
            _selectionPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
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

        /// <summary>A 1 px outline around each item, drawn inside its bounds.</summary>
        public Brush ItemBorder
        {
            get => (Brush)GetValue(ItemBorderProperty);
            set => SetValue(ItemBorderProperty, value);
        }

        /// <summary>Where the accent shape is drawn right now (mid-slide while it moves).</summary>
        internal Rect IndicatorRect => CurrentRect();

        internal bool IndicatorShown => CurrentOpacity() > 0;

        /// <summary>Where the accent shape is heading: the checked item's bounds, or empty when nothing is checked.</summary>
        internal Rect IndicatorTarget => _target;

        void Update(bool selectionChanged)
        {
            Rect target = Rect.Empty;
            _scan.Clear();
            if (Child is Panel panel && Visibility == Visibility.Visible)
            {
                foreach (UIElement element in panel.Children)
                {
                    var toggle = FindToggle(element);
                    if (toggle == null || toggle.Visibility != Visibility.Visible) continue;
                    var part = toggle.Template?.FindName("Bd", toggle) as FrameworkElement ?? toggle;
                    if (part.ActualWidth <= 0 || !IsAncestorOf(part)) continue;
                    var bounds = part.TransformToAncestor(this).TransformBounds(new Rect(part.RenderSize));
                    _scan.Add(bounds);
                    if (part is Border border) _radius = border.CornerRadius.TopLeft;
                    if (toggle.IsChecked == true) target = bounds;
                }
            }

            bool redraw = false;
            if (!SameRects(_scan))
            {
                _itemRects.Clear();
                _itemRects.AddRange(_scan);
                redraw = true;
            }
            if (!Near(target, _target))
            {
                bool wasShown = !_target.IsEmpty;
                _target = target;
                // A move caused by layout (language, window, mode) jumps; only a new selection slides.
                bool animate = selectionChanged && Animations && SystemParameters.ClientAreaAnimation && IsLoaded;
                if (target.IsEmpty) SetOpacity(0, animate);
                else if (!wasShown || !animate)
                {
                    SetRect(target, false);
                    SetOpacity(1, animate);
                }
                else
                {
                    SetOpacity(1, false);
                    SetRect(target, true);
                }
                redraw = true;
            }
            if (redraw) InvalidateVisual();
        }

        void SetRect(Rect end, bool animate)
        {
            if (animate && !_rect.IsEmpty)
            {
                _rectFrom = CurrentRect();
                var slide = new RectAnimation(_rectFrom, end, SlideTime) { EasingFunction = Ease };
                slide.Freeze();
                _rectClock = slide.CreateClock();
            }
            else _rectClock = null;
            _rect = end;
        }

        void SetOpacity(double end, bool animate)
        {
            if (animate)
            {
                _opacityFrom = CurrentOpacity();
                var fade = new DoubleAnimation(_opacityFrom, end, SlideTime) { EasingFunction = Ease };
                fade.Freeze();
                _opacityClock = fade.CreateClock();
            }
            else _opacityClock = null;
            _opacity = end;
        }

        // The clock's eased progress between the stored start and end; a clock that has not ticked yet is at its start.
        static double Progress(AnimationClock clock)
        {
            if (clock == null) return 1;
            double? p = clock.CurrentProgress;
            if (p == null) return clock.CurrentState == ClockState.Active ? 0 : 1;
            return Ease.Ease(p.Value);
        }

        Rect CurrentRect()
        {
            if (_rectClock == null || _rect.IsEmpty || _rectFrom.IsEmpty) return _rect;
            double t = Progress(_rectClock);
            return new Rect(
                _rectFrom.X + (_rect.X - _rectFrom.X) * t,
                _rectFrom.Y + (_rect.Y - _rectFrom.Y) * t,
                _rectFrom.Width + (_rect.Width - _rectFrom.Width) * t,
                _rectFrom.Height + (_rect.Height - _rectFrom.Height) * t);
        }

        double CurrentOpacity() =>
            _opacityClock == null ? _opacity : _opacityFrom + (_opacity - _opacityFrom) * Progress(_opacityClock);

        protected override void OnRender(DrawingContext dc)
        {
            var fill = ItemFill;
            var border = ItemBorder;
            if (border != null && _itemPen == null) _itemPen = Freeze(new Pen(border, 1));
            var pen = border == null ? null : _itemPen;
            if (fill != null || pen != null)
                foreach (var rect in _itemRects)
                {
                    if (pen == null)
                    {
                        dc.DrawRoundedRectangle(fill, null, rect, _radius, _radius);
                        continue;
                    }
                    // Snap the outline to device pixels like the Border it replaces, so it stays a sharp 1 px line.
                    var guidelines = new GuidelineSet(new[] { rect.Left, rect.Right }, new[] { rect.Top, rect.Bottom });
                    guidelines.Freeze();
                    dc.PushGuidelineSet(guidelines);
                    var inner = new Rect(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
                    double r = Math.Max(0, _radius - 0.5);
                    dc.DrawRoundedRectangle(fill, pen, inner, r, r);
                    dc.Pop();
                }

            var brush = IndicatorBrush;
            if (brush == null || _rect.IsEmpty || (_opacityClock == null && _opacity <= 0)) return;
            dc.PushOpacity(_opacity, _opacityClock);
            dc.DrawRoundedRectangle(brush, null, _rect, _rectClock, _radius, null, _radius, null);
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
