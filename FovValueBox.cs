using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FovApp;

/// <summary>
/// A small readout showing the current value. Clicking it opens an inline editor
/// for a custom number; pressing Enter commits, Escape cancels, focus loss commits.
/// </summary>
public class FovValueBox : Control
{
    private TextBlock? _text;
    private TextBox? _editor;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(FovValueBox),
            new PropertyMetadata(0.0, OnValueChanged));

    public event EventHandler<double>? ValueCommitted;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Whether the inline number editor is currently visible.</summary>
    public bool IsEditing => _editor?.Visibility == Visibility.Visible;

    /// <summary>Applies the pending text, if any. The window calls this before
    /// another control receives a click, so Enter is never required.</summary>
    public void CommitEdit() => Commit();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FovValueBox b)
            b._text?.SetValue(TextBlock.TextProperty, Format((double)e.NewValue));
    }

    internal static string Format(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.001
            ? ((long)Math.Round(v)).ToString()
            : v.ToString("0.#");

    static FovValueBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FovValueBox),
            new FrameworkPropertyMetadata(typeof(FovValueBox)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _text = Template.FindName("PART_Text", this) as TextBlock;
        _editor = Template.FindName("PART_Editor", this) as TextBox;
        if (_editor is not null)
        {
            _editor.Visibility = Visibility.Collapsed;
            _editor.KeyDown += Editor_KeyDown;
            _editor.LostFocus += (_, _) => Commit();
            _editor.LostKeyboardFocus += (_, _) => Commit();
        }
        _text?.SetValue(TextBlock.TextProperty, Format(Value));
        if (_text is not null)
        {
            _text.MouseLeftButtonUp += (_, e) => BeginEdit();
        }
        MouseLeftButtonUp += (_, e) => BeginEdit();
    }

    private void BeginEdit()
    {
        if (_editor is null || _editor.Visibility == Visibility.Visible) return;
        _text!.Visibility = Visibility.Collapsed;
        _editor.Visibility = Visibility.Visible;
        _editor.Text = Format(Value);
        _editor.Focus();
        _editor.SelectAll();
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _editor!.Visibility = Visibility.Collapsed;
            _text!.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void Commit()
    {
        if (_editor is null || _editor.Visibility != Visibility.Visible) return;
        _editor.Visibility = Visibility.Collapsed;
        _text!.Visibility = Visibility.Visible;
        if (double.TryParse(_editor.Text.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            Value = v;
            ValueCommitted?.Invoke(this, v);
        }
    }
}
