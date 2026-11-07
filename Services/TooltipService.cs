using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FovApp.Services;

/// <summary>
/// Tooltip that appears only after the cursor rests on the target for a second.
/// Fades in, no glow. Shows the full metadata type name and its explanation.
/// </summary>
public static class TooltipService
{
    private static readonly Dictionary<FrameworkElement, DelayedTooltip> Attached = new();

    public static void Attach(FrameworkElement target, string typeName, string description)
    {
        Detach(target);
        var tip = new DelayedTooltip(target, typeName, description);
        Attached[target] = tip;
    }

    public static void Detach(FrameworkElement target)
    {
        if (Attached.Remove(target, out var tip))
            tip.Detach();
    }

    private class DelayedTooltip
    {
        private readonly FrameworkElement _target;
        private readonly string _typeName;
        private readonly string _description;
        private readonly DispatcherTimer _timer;
        private Window? _window;
        private bool _shown;

        public DelayedTooltip(FrameworkElement target, string typeName, string description)
        {
            _target = target;
            _typeName = typeName;
            _description = description;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.7) };
            _timer.Tick += (_, _) => Show();
            target.MouseEnter += OnEnter;
            target.MouseLeave += OnLeave;
            target.Unloaded += OnLeave;
        }

        private void OnEnter(object sender, MouseEventArgs e) => _timer.Start();
        private void OnLeave(object? sender, EventArgs e)
        {
            _timer.Stop();
            Hide();
        }

        public void Detach()
        {
            _timer.Stop();
            Hide();
            _target.MouseEnter -= OnEnter;
            _target.MouseLeave -= OnLeave;
        }

        private void Show()
        {
            if (_shown || _target.IsMouseOver == false) return;
            _shown = true;

            // anchor: below the label, at its left edge
            var screen = _target.PointToScreen(new Point(0, _target.ActualHeight + 2));
            var work = SystemParameters.WorkArea;

            var typeName = new TextBlock
            {
                Text = _typeName + ":",
                Foreground = Brushes.White,
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.NoWrap
            };
            var description = new TextBlock
            {
                Text = _description,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0),
                MaxWidth = 390,
                TextWrapping = TextWrapping.Wrap
            };
            var content = new StackPanel();
            content.Children.Add(typeName);
            if (!string.IsNullOrWhiteSpace(_description))
                content.Children.Add(description);
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Opacity = 0,
                Child = content
            };

            // Measure before showing so the description stays on-screen.
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = border.DesiredSize;

            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Width = Math.Ceiling(sz.Width),
                Height = Math.Ceiling(sz.Height),
                Content = border
            };
            _window.Show();
            _window.Left = Math.Min(Math.Max(2, screen.X), work.Right - sz.Width - 4);
            _window.Top = Math.Min(Math.Max(2, screen.Y), work.Bottom - sz.Height - 4);

            var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(Motion.Fade(140)));
            border.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            if (_window is null) return;
            var w = _window;
            _window = null;
            if (Motion.ReduceMotion)
            {
                w.Close();
                return;
            }
            var border = w.Content as Border;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90));
            anim.Completed += (_, _) => w.Close();
            if (border is not null)
                border.BeginAnimation(UIElement.OpacityProperty, anim);
            else
                w.Close();
        }
    }
}
