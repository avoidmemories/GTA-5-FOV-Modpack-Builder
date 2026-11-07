using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FovApp.Services;

namespace FovApp;

/// <summary>
/// Monochrome slider with a round knob.
///
/// The single source of truth is the FRACTION (0..1 of the usable track), not
/// pixels: the value, the knob position and the fill width are all derived from
/// it. Every movement goes through <see cref="SetFraction"/> which updates the
/// value, the model event and the visuals together, so they can never disagree —
/// which is what caused knobs to settle away from the pointer and values to
/// desync after fast drags.
///
/// Rendering: the knob moves via TranslateTransform and the fill via Width —
/// no bitmap effects, no parent layout invalidation, no animation while
/// dragging. Animations run only for programmatic changes and the double-click
/// reset, and are always cleared before a drag starts.
/// </summary>
public class FovSlider : Control
{
    private Border? _fill;
    private Ellipse? _knob;
    private TranslateTransform? _knobTransform;

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(FovSlider),
            new PropertyMetadata(0.0, OnRangeChanged));
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(FovSlider),
            new PropertyMetadata(100.0, OnRangeChanged));
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(FovSlider),
            new PropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(FovSlider),
            new PropertyMetadata(Brushes.White));

    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Colour of the filled track and knob. Yellow flags imported values.</summary>
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <summary>Value the slider resets to on a double click.</summary>
    public double DefaultValue { get; set; }

    /// <summary>Raised when the user double-clicks the slider to reset it.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>Raised once when a user starts a pointer edit.</summary>
    public event EventHandler? EditStarted;

    private bool _dragging;
    private bool _suppress; // programmatic update guard
    private double _fraction; // 0..1, single source of visual truth

    static FovSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FovSlider),
            new FrameworkPropertyMetadata(typeof(FovSlider)));
        FocusableProperty.OverrideMetadata(typeof(FovSlider), new FrameworkPropertyMetadata(false));
    }

    // ---------------------------------------------------------------- fraction core

    private double ValueToFraction(double v)
    {
        var range = Maximum - Minimum;
        if (range <= 0) return 0;
        return Math.Max(0, Math.Min(1, (v - Minimum) / range));
    }

    private double FractionToValue(double f) => Minimum + f * (Maximum - Minimum);

    /// <summary>The one place where value + visuals change together.</summary>
    private void SetFraction(double f, bool animate, bool raiseEvent)
    {
        f = Math.Max(0, Math.Min(1, f));
        _fraction = f;
        var newValue = FractionToValue(f);

        _suppress = true;
        var old = Value;
        try { SetValue(ValueProperty, newValue); }
        finally { _suppress = false; }

        RenderFraction(animate);

        if (raiseEvent && Math.Abs(old - newValue) > 0.0001)
            ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(old, newValue));
    }

    // ---------------------------------------------------------------- rendering

    private double KnobDiameter => _knob?.ActualWidth > 0 ? _knob!.ActualWidth : 14;
    private const double Pad = 0;

    private double UsableWidth => Math.Max(0, ActualWidth - KnobDiameter - 2 * Pad);

    private void EnsureTransforms()
    {
        if (_knob is null) return;
        if (_knob.RenderTransform is not TranslateTransform kt)
        {
            kt = new TranslateTransform();
            _knob.RenderTransform = kt;
        }
        _knobTransform = kt;
    }

    /// <summary>Places knob and fill for the current fraction.</summary>
    private void RenderFraction(bool animate)
    {
        EnsureTransforms();
        if (_knobTransform is null || _fill is null || ActualWidth <= 0) return;

        var x = Pad + KnobDiameter / 2 + _fraction * UsableWidth; // knob centre
        var knobX = x - KnobDiameter / 2;
        var fillWidth = Math.Max(0, x);

        if (animate && !Motion.ReduceMotion)
        {
            // animate from CURRENT visual state to the target
            _knobTransform.BeginAnimation(TranslateTransform.XProperty, null);
            var fromX = _knobTransform.X;
            var anim = new DoubleAnimation(fromX, knobX, TimeSpan.FromMilliseconds(Motion.Move(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, _) => _knobTransform.X = knobX;
            _knobTransform.BeginAnimation(TranslateTransform.XProperty, anim);

            _fill.BeginAnimation(WidthProperty, null);
            var fromW = _fill.Width;
            var fillAnim = new DoubleAnimation(fromW, fillWidth, TimeSpan.FromMilliseconds(Motion.Move(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fillAnim.Completed += (_, _) => _fill.Width = fillWidth;
            _fill.BeginAnimation(WidthProperty, fillAnim);
        }
        else
        {
            // instant: clear any running animation first, then set
            _knobTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _knobTransform.X = knobX;
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = fillWidth;
        }
    }

    // ---------------------------------------------------------------- property changes

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FovSlider s && !s._dragging)
            s.SetFraction(s.ValueToFraction(s.Value), animate: false, raiseEvent: false);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FovSlider s && !s._suppress)
            s.SetFraction(s.ValueToFraction((double)e.NewValue), animate: !s._dragging, raiseEvent: false);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RenderFraction(animate: false); // positions recompute from the fraction
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fill = Template.FindName("PART_Fill", this) as Border;
        _knob = Template.FindName("PART_Knob", this) as Ellipse;
        EnsureTransforms();
        _fraction = ValueToFraction(Value);
        RenderFraction(animate: false);
        Loaded += (_, _) => RenderFraction(animate: false);
    }

    // ---------------------------------------------------------------- pointer

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        EditStarted?.Invoke(this, EventArgs.Empty);
        if (e.ClickCount == 2)
        {
            // double click: glide back to the default value
            SetFraction(ValueToFraction(DefaultValue), animate: true, raiseEvent: true);
            ResetRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        CaptureMouse();
        _dragging = true;
        SetFraction(PointerFraction(e), animate: false, raiseEvent: true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            SetFraction(PointerFraction(e), animate: false, raiseEvent: true);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            // already exactly at the pointer: nothing to settle
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    /// <summary>Track fraction under the pointer, with whole-number value snapping.</summary>
    private double PointerFraction(MouseEventArgs e)
    {
        var pos = e.GetPosition(this).X;
        var f = UsableWidth > 0 ? (pos - KnobDiameter / 2 - Pad) / UsableWidth : 0;
        f = Math.Max(0, Math.Min(1, f));

        // snap the VALUE to a whole number, then convert back to a fraction so
        // the knob rests exactly where the snapped value puts it
        var v = FractionToValue(f);
        var snapped = Math.Round(v);
        return ValueToFraction(snapped);
    }

    /// <summary>Repositions the knob instantly — call after changing Value from
    /// code while suppressed.</summary>
    public void RefreshKnob() => RenderFraction(animate: false);

    /// <summary>Programmatic value change that never animates and never starts
    /// event cascades. Used for bulk updates (global shift, presets): a 150 ms
    /// animation per row per mouse event was the source of the visible shaking.</summary>
    public void SetValueInstant(double v)
    {
        v = Math.Max(Minimum, Math.Min(Maximum, v));
        _suppress = true;
        try { SetValue(ValueProperty, v); }
        finally { _suppress = false; }
        SetFraction(ValueToFraction(v), animate: false, raiseEvent: false);
    }
}
