using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FovApp.Services;

namespace FovApp;

public partial class MainWindow : Window
{
    private List<FovParam> _parameters = new();
    private readonly Dictionary<FovParam, RowControls> _rows = new();
    private bool _syncing;
    private readonly Stack<EditorState> _undo = new();
    private readonly Stack<EditorState> _redo = new();
    private bool _restoringHistory;
    private const int HistoryLimit = 100;
    private bool _canRollbackImportedFile;
    private string? _lastBuiltPath;
    private DateTime _doneVisibleUntil = DateTime.MinValue;
    private const int SegmentTravel = 300;
    private const int PageCrossFade = 200;
    private AppSettings? _savedSettings;


    // ---------------------------------------------------------------- absolute shift

    /// <summary>When true, Global Shift and presets SET values to the number
    /// (50 and 60 both become 100) instead of adding a delta.</summary>
    private bool _absoluteShift;

    private void AbsoluteShift_Changed(object sender, RoutedEventArgs e)
    {
        SetAbsoluteShift(AbsoluteShiftToggle.IsChecked == true, silent: false);
    }

    private void SetAbsoluteShift(bool on, bool silent)
    {
        _absoluteShift = on;
        if (!silent)
            AbsoluteShiftToggle.IsChecked = on;
    }

    /// <summary>Applies the shift to a parameter: additive or absolute.</summary>
    private void ApplyShift(FovParam p, double amount)
    {
        if (_absoluteShift)
            p.SetPersonal((float)(amount - p.BaseDefault)); // value = amount exactly
        else
            p.SetGlobal((float)amount); // additive: value = default + personal + amount
    }


    private void DisarmPresetVisuals()
    {
        _presetPlaced = false;
        _presetShift = null;
        PresetX15.IsChecked = false;
        PresetX30.IsChecked = false;
        PresetX60.IsChecked = false;
        PresetBacking.BeginAnimation(WidthProperty, null);
        PresetBacking.BeginAnimation(MarginProperty, null);
        PresetBacking.Width = 0;
        PresetX15.Foreground = (Brush)FindResource("SubTextBrush");
        PresetX30.Foreground = (Brush)FindResource("SubTextBrush");
        PresetX60.Foreground = (Brush)FindResource("SubTextBrush");
        _p15Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);
        _p30Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);
        _p60Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);
    }
    // ---------------------------------------------------------------- simple / advanced mode

    private bool _simpleMode;
    private bool _modePlaced;
    private double _modeLeft;
    private double _modeWidth;
    private readonly SolidColorBrush _simpleBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private readonly SolidColorBrush _advancedBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private void ModeSimple_Click(object sender, RoutedEventArgs e) => SetSimpleMode(true);
    private void ModeAdvanced_Click(object sender, RoutedEventArgs e) => SetSimpleMode(false);

    /// <summary>SIMPLE hides every tab and the x-presets and shows one page with
    /// two master sliders; ADVANCED restores the full interface.</summary>
    private void SetSimpleMode(bool simple)
    {
        if (_simpleMode == simple && _tabs.Count > 0) { UpdateModeBacking(true); return; }
        _simpleMode = simple;
        SaveSettings();

        // the Set-exact toggle belongs to the ADVANCED footer only
        AbsoluteShiftPanel.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;

        if (simple)
        {
            FadeThenCollapse(NavScroll);
            FadeThenCollapse(PresetBlock);
            foreach (var tab in _tabs)
                HideElement(tab.Page);
            BuildSimplePage();
        }
        else
        {
            if (_simplePage is not null)
            {
                PagesHost.Children.Remove(_simplePage);
                _simplePage = null;
            }
            foreach (var tab in _tabs)
            {
                tab.Page.BeginAnimation(OpacityProperty, null);
                tab.Page.Opacity = 1;
                tab.Page.Visibility = Visibility.Collapsed;
            }
            NavScroll.BeginAnimation(OpacityProperty, null);
            NavScroll.Opacity = 1;
            NavScroll.Visibility = Visibility.Visible;
            PresetBlock.BeginAnimation(OpacityProperty, null);
            PresetBlock.Opacity = 1;
            PresetBlock.Visibility = Visibility.Visible;
            _activeTab?.NavButton.Tag = null;
            _activeTab = _tabs.FirstOrDefault();
            if (_activeTab is not null) _activeTab.NavButton.Tag = "active";
            if (_activeTab is not null)
            {
                _activeTab.Page.Visibility = Visibility.Visible;
                FadeIn(_activeTab.Page);
            }
        }

        Dispatcher.BeginInvoke(() => UpdateModeBacking(_modePlaced), DispatcherPriority.Render);
        AnimateModeLabels();
    }

    private void FadeThenCollapse(FrameworkElement el)
    {
        if (Motion.ReduceMotion)
        {
            el.Visibility = Visibility.Collapsed;
            return;
        }
        var anim = Motion.Value(0, 180, EasingMode.EaseOut);
        anim.Completed += (_, _) => el.Visibility = Visibility.Collapsed;
        el.BeginAnimation(OpacityProperty, anim);
    }

    private static void FadeIn(FrameworkElement el)
    {
        el.BeginAnimation(OpacityProperty, null);
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty, Motion.Value(1, 200, EasingMode.EaseOut));
    }

    private static void HideElement(UIElement el)
    {
        el.BeginAnimation(OpacityProperty, null);
        el.Opacity = 1;
        el.Visibility = Visibility.Collapsed;
    }

    /// <summary>SIMPLE-mode shifts, kept apart from the ADVANCED personal
    /// offsets: editing in SIMPLE never marks the ADVANCED presets custom.</summary>
    private readonly Dictionary<(string type, string field), float> _simpleShifts = new();
    private readonly Dictionary<string, List<FovParam>> _simpleSelections = new();

    private Border? _simplePage;

    /// <summary>The SIMPLE page: two master sliders over the same model.</summary>
    private void BuildSimplePage()
    {
        if (_simplePage is not null) return;
        _simpleSelections.Clear();

        var makeSlider = (string label, Func<IEnumerable<FovParam>> selection, float current) =>
        {
            var baseDefault = selection().FirstOrDefault()?.BaseDefault ?? 0;
            var slider = new FovSlider
            {
                Minimum = -FovModel.MaxSlider,
                Maximum = FovModel.MaxSlider,
                Value = baseDefault + current,
                DefaultValue = baseDefault
            };
            var valueBox = new FovValueBox { Value = baseDefault + current };
            var lbl = new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("TitleText"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(valueBox, 2);
            grid.Children.Add(lbl);
            grid.Children.Add(slider);
            grid.Children.Add(valueBox);

            var panel = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)FindResource("BorderBrushSoft"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };

            // SIMPLE edits live in their own store; the ADVANCED model and
            // its presets are never touched from here
            slider.ValueChanged += (_, e) =>
            {
                if (_syncing) return;
                _syncing = true;
                try
                {
                    foreach (var p in selection())
                        _simpleShifts[(p.Type, p.Field)] = (float)(e.NewValue - p.BaseDefault);
                    valueBox.Value = e.NewValue;
                    SaveSettings();
                }
                finally { _syncing = false; }
            };
            slider.EditStarted += (_, _) => CaptureHistory();
            valueBox.ValueCommitted += (_, v) =>
            {
                if (_syncing) return;
                CaptureHistory();
                _syncing = true;
                try
                {
                    foreach (var p in selection())
                        _simpleShifts[(p.Type, p.Field)] = (float)(v - p.BaseDefault);
                    slider.SetValueInstant(Math.Min(Math.Max(v, slider.Minimum), slider.Maximum));
                    valueBox.Value = v;
                    SaveSettings();
                }
                finally { _syncing = false; }
            };

            return panel;
        };

        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "MASTER FOV",
            Style = (Style)FindResource("SectionLabel"),
            Margin = new Thickness(4, 0, 0, 10)
        });
        // note: the "Set exact" toggle lives in the footer and only shows in ADVANCED

        // All first-person views (except aiming): on foot plus the interior and mounted-part
        // vehicle cameras.  Keeping them together makes the SIMPLE "First
        // person" value apply consistently no matter whether the player is
        // walking or inside a vehicle.
        {
            var sel = _parameters.Where(p =>
                (p.Type == "camFirstPersonShooterCameraMetadata" && p.Field == "BaseFov")
                || (p.Type == "camFirstPersonHeadTrackingAimCameraMetadata" && p.Field == "BaseFov")
                || (p.Type == "camCinematicFirstPersonIdleCameraMetadata"
                    && (p.Field == "PlayerHorizontalFov" || p.Field == "PlayerVerticalFov"))
                || (p.Type == "camCinematicMountedCameraMetadata" && p.Field == "BaseFov")
                || (p.Type == "camCinematicMountedPartCameraMetadata" && p.Field == "BaseFov")).ToList();
            var cur = sel.Select(p => _simpleShifts.GetValueOrDefault((p.Type, p.Field))).FirstOrDefault();
            _simpleSelections["First person"] = sel;
            stack.Children.Add(makeSlider("First person", () => sel, cur));
        }
        // In vehicle: only the external follow camera behind the vehicle.
        {
            var sel = _parameters.Where(p =>
                p.Type == "camFollowVehicleCameraMetadata" && p.Field == "BaseFov").ToList();
            var cur = sel.Select(p => _simpleShifts.GetValueOrDefault((p.Type, p.Field))).FirstOrDefault();
            _simpleSelections["In vehicle"] = sel;
            stack.Children.Add(makeSlider("In vehicle", () => sel, cur));
        }
        // Third person on foot: follow-ped, aim, and parachute/freefall
        // follow cameras share the one SIMPLE master value.
        {
            var sel = _parameters.Where(p =>
                (p.Type == "camFollowPedCameraMetadata" && p.Field == "BaseFov")
                || (p.Type == "camThirdPersonPedAimCameraMetadata" && p.Field == "BaseFov")
                || (p.Type == "camFollowParachuteCameraMetadata" && p.Field == "BaseFov")).ToList();
            var cur = sel.Select(p => _simpleShifts.GetValueOrDefault((p.Type, p.Field))).FirstOrDefault();
            _simpleSelections["Third person"] = sel;
            stack.Children.Add(makeSlider("Third person", () => sel, cur));
        }



        _simplePage = new Border { Child = stack };
        PagesHost.Children.Add(_simplePage);
        FadeIn(_simplePage);
    }

    /// <summary>Updates SIMPLE in place. Recreating its page for Ctrl+Z/Ctrl+Shift+Z
    /// made the whole screen flash even though only a number had changed.</summary>
    private void RefreshSimplePage()
    {
        if (_simplePage?.Child is not StackPanel stack) return;
        foreach (var border in stack.Children.OfType<Border>())
        {
            if (border.Child is not Grid grid) continue;
            var label = grid.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            var slider = grid.Children.OfType<FovSlider>().FirstOrDefault();
            var box = grid.Children.OfType<FovValueBox>().FirstOrDefault();
            if (label is null || slider is null || box is null || !_simpleSelections.TryGetValue(label, out var selection))
                continue;
            var baseDefault = selection.FirstOrDefault()?.BaseDefault ?? 0;
            var shift = selection.Select(p => _simpleShifts.GetValueOrDefault((p.Type, p.Field))).FirstOrDefault();
            var value = baseDefault + shift;
            slider.DefaultValue = baseDefault;
            slider.SetValueInstant(Math.Min(Math.Max(value, slider.Minimum), slider.Maximum));
            box.Value = value;
        }
    }

    private void UpdateModeBacking(bool animate)
    {
        var target = ModeSimple.IsChecked == true ? ModeSimple : ModeAdvanced;
        if (target.ActualWidth <= 0 || ModeBacking.Parent is not UIElement parent) return;
        _modePlaced = true;
        var width = target.ActualWidth;
        var left = target.TranslatePoint(new Point(0, 0), parent).X;
        var settled = new Thickness(left, 0, 0, 0);
        var fromLeft = _modeLeft;
        var fromWidth = _modeWidth > 0 ? _modeWidth : width;

        ModeBacking.BeginAnimation(WidthProperty, null);
        ModeBacking.BeginAnimation(MarginProperty, null);
        if (!animate || Motion.ReduceMotion)
        {
            ModeBacking.Width = width;
            ModeBacking.Margin = settled;
            _modeLeft = left;
            _modeWidth = width;
            return;
        }
        _modeLeft = left;
        _modeWidth = width;
        ModeBacking.BeginAnimation(WidthProperty,
            Motion.Value(fromWidth, width, SegmentTravel, EasingMode.EaseInOut));
        ModeBacking.BeginAnimation(MarginProperty,
            new ThicknessAnimation(new Thickness(fromLeft, 0, 0, 0), settled,
                TimeSpan.FromMilliseconds(Motion.Move(SegmentTravel)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void AnimateModeLabels()
    {
        var resting = Color.FromRgb(0x8A, 0x8A, 0x8A);
        var active = Color.FromRgb(0x00, 0x00, 0x00);
        foreach (var (seg, brush, isActive) in new[]
                 {
                     (ModeSimple, _simpleBrush, _simpleMode),
                     (ModeAdvanced, _advancedBrush, !_simpleMode)
                 })
        {
            var target = isActive ? active : resting;
            seg.Foreground = brush;
            var previous = brush.Color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = target;
            if (Motion.ReduceMotion) continue;
            var anim = new ColorAnimation(previous, target,
                TimeSpan.FromMilliseconds(Motion.Move(SegmentTravel)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    // ---------------------------------------------------------------- presets

    /// <summary>Global personal offset added by the active preset (+15/+30/+60).</summary>
    private float? _presetShift;
    private bool _presetPlaced;
    private double _presetLeft;
    private double _presetWidth;
    private readonly SolidColorBrush _p15Brush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private readonly SolidColorBrush _p30Brush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private readonly SolidColorBrush _p60Brush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private DispatcherTimer? _presetTimer;
    private DispatcherTimer? _defaultsTimer;
    private bool _defaultsArmed;

    private void DisarmDefaults()
    {
        _defaultsArmed = false;
        _defaultsTimer?.Stop();
        _defaultsTimer = null;
        DefaultsLabel.Text = "Set all tabs default";
        DefaultsLabel.Foreground = (Brush)FindResource("TextBrush");
        BtnSetDefaults.BorderBrush = (Brush)FindResource("BorderBrushSoft");
    }

    private void PresetX15_Click(object sender, RoutedEventArgs e) => ApplyPreset(15);
    private void PresetX30_Click(object sender, RoutedEventArgs e) => ApplyPreset(30);
    private void PresetX60_Click(object sender, RoutedEventArgs e) => ApplyPreset(60);

    /// <summary>Applies a preset: a global shift of +15/+30/+60 on top of the
    /// defaults, applied through every tab's Global Shift so the sliders reflect it.</summary>
    private void ApplyPreset(int factor)
    {
        CaptureHistory();
        _presetShift = factor;
        PresetOverlay.Visibility = Visibility.Collapsed;

        _syncing = true;
        try
        {
            foreach (var tab in _tabs)
            {
                if (tab.Locked) continue;
                foreach (var p in tab.Params)
                {
                    if (_absoluteShift)
                    {
                        p.SetPersonal((float)(factor - p.BaseDefault));
                        p.SetGlobal(0);
                    }
                    else
                    {
                        p.SetPersonal(0);
                        p.SetGlobal(factor);
                    }
                }
            }
            foreach (var tab in _tabs)
            {
                if (tab.Locked) continue;
                tab.GlobalSlider.SetValueInstant(factor);
                tab.GlobalValue.Value = factor;
                foreach (var rc in tab.Rows)
                {
                    if (rc.Locked) continue;
                    var p = tab.Params[tab.Rows.IndexOf(rc)];
                    var shown = p.BaseDefault + p.Personal + factor;
                    rc.Slider.SetValueInstant(Math.Min(Math.Max(shown, rc.Slider.Minimum), rc.Slider.Maximum));
                    rc.ValueBox.Value = p.Values.FirstOrDefault();
                }
            }
        }
        finally { _syncing = false; }

        _presetTimer?.Stop();
        _presetTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background,
            (_, _) => { _presetTimer = null; }, Dispatcher);
        _presetTimer.Start();

        Dispatcher.BeginInvoke(() => UpdatePresetBacking(_presetPlaced), DispatcherPriority.Render);
        AnimatePresetLabels(factor);
    }

    /// <summary>Called whenever the user edits something manually: the preset
    /// overlay dims and shows CUSTOM.</summary>
    private void MarkCustom()
    {
        if (_presetShift is not null)
        {
            _presetShift = null;
            PresetOverlay.BeginAnimation(OpacityProperty, null);
            PresetOverlay.Visibility = Visibility.Visible;
            PresetOverlay.Opacity = 0;
            PresetOverlay.BeginAnimation(OpacityProperty,
                Motion.Value(1, 160, EasingMode.EaseOut));
        }
        else if (PresetOverlay.Visibility != Visibility.Visible)
        {
            PresetOverlay.Visibility = Visibility.Visible;
        }
        // persist edits immediately, not only on exit
        SaveSettings();
    }

    /// <summary>Slides the preset backing (same motion as the version segment).</summary>
    private void UpdatePresetBacking(bool animate)
    {
        var target = _presetShift switch
        {
            15 => PresetX15,
            30 => PresetX30,
            60 => PresetX60,
            _ => null
        };
        if (target is null) return;
        if (target.ActualWidth <= 0 || PresetBacking.Parent is not UIElement parent) return;

        _presetPlaced = true;
        var width = target.ActualWidth;
        var left = target.TranslatePoint(new Point(0, 0), parent).X;
        var settled = new Thickness(left, 0, 0, 0);
        var fromLeft = _presetLeft;
        var fromWidth = _presetWidth > 0 ? _presetWidth : width;

        PresetBacking.BeginAnimation(WidthProperty, null);
        PresetBacking.BeginAnimation(MarginProperty, null);

        if (!animate || Motion.ReduceMotion)
        {
            PresetBacking.Width = width;
            PresetBacking.Margin = settled;
            _presetLeft = left;
            _presetWidth = width;
            return;
        }
        _presetLeft = left;
        _presetWidth = width;
        PresetBacking.BeginAnimation(WidthProperty,
            Motion.Value(fromWidth, width, SegmentTravel, EasingMode.EaseInOut));
        PresetBacking.BeginAnimation(MarginProperty,
            new ThicknessAnimation(new Thickness(fromLeft, 0, 0, 0), settled,
                TimeSpan.FromMilliseconds(Motion.Move(SegmentTravel)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void AnimatePresetLabels(int factor)
    {
        var resting = Color.FromRgb(0x8A, 0x8A, 0x8A);
        var active = Color.FromRgb(0x00, 0x00, 0x00);
        foreach (var (seg, brush, isActive) in new[]
                 {
                     (PresetX15, _p15Brush, factor == 15),
                     (PresetX30, _p30Brush, factor == 30),
                     (PresetX60, _p60Brush, factor == 60)
                 })
        {
            var target = isActive ? active : resting;
            seg.Foreground = brush;
            var previous = brush.Color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = target;
            if (Motion.ReduceMotion) continue;
            var anim = new ColorAnimation(previous, target,
                TimeSpan.FromMilliseconds(Motion.Move(SegmentTravel)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    // ---------------------------------------------------------------- tabs

    /// <summary>One sidebar tab: its page, its own Global Shift row and its rows.</summary>
    private class Tab
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required Border Page { get; init; }
        public required FovSlider GlobalSlider { get; init; }
        public required FovValueBox GlobalValue { get; init; }
        public required List<RowControls> Rows { get; init; }
        public required List<FovParam> Params { get; init; }
        public required Button NavButton { get; init; }
        /// <summary>A locked tab is dimmed and ignored by presets.</summary>
        public bool Locked { get; set; }
    }

    private readonly List<Tab> _tabs = new();
    private Tab? _activeTab;

    private class RowControls
    {
        public required FovSlider Slider { get; init; }
        public required FovValueBox ValueBox { get; init; }
        public required TextBlock Label { get; init; }
        public required Border Row { get; init; }
        /// <summary>Locked rows are dimmed and ignore the tab's global shift.</summary>
        public bool Locked { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Initialize();
        Closed += (_, _) => SaveSettings();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
    }

    // ---------------------------------------------------------------- startup

    private void Initialize()
    {
        // restore persisted state
        var settings = LoadSettings();
        _savedSettings = settings;

        ReloadParameters(animateSwitch: false);
        RestoreSettings(settings);

        // mode: default ADVANCED; restore persisted choice
        ModeAdvanced.IsChecked = !settings.SimpleMode;
        if (settings.SimpleMode)
        {
            ModeSimple.IsChecked = true;
            SetSimpleMode(true);
        }
        Dispatcher.BeginInvoke(() => UpdateModeBacking(false), System.Windows.Threading.DispatcherPriority.Render);
        AnimateModeLabels();

        if (!_simpleMode && _tabs.Count > 0)
            SelectTab(_tabs[0], animate: false);
    }

    // ---------------------------------------------------------------- model + tabs

    /// <summary>Rebuilds every tab page from the single pristine base YMT.</summary>
    private void ReloadParameters(bool animateSwitch)
    {
        List<FovParam> loaded;
        try
        {
            loaded = FovModel.Load(BaseYmtFileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to load " + BaseYmtFileName + ":\n" + ex.Message,
                "FOV Modpack Builder", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        // keep user's personal edits across version switches (fields are identical)
        if (_tabs.Count > 0)
        {
            var old = _parameters.ToDictionary(p => (p.Type, p.Field));
            var sameShape = old.Count == loaded.Count && loaded.All(p => old.ContainsKey((p.Type, p.Field)));
            if (sameShape)
            {
                // Rebase the existing model instead of recreating the visual
                // tree. Switching editions then leaves the visible page intact.
                _syncing = true;
                try
                {
                    foreach (var replacement in loaded)
                    {
                        var p = old[(replacement.Type, replacement.Field)];
                        p.Rebase(replacement);
                        var row = _rows[p];
                        row.Slider.DefaultValue = p.BaseDefault;
                        var shown = p.BaseDefault + p.Personal + p.PersonalGlobal();
                        row.Slider.SetValueInstant(Math.Min(Math.Max(shown, row.Slider.Minimum), row.Slider.Maximum));
                        row.ValueBox.Value = shown;
                    }
                    foreach (var tab in _tabs)
                    {
                        var global = tab.Params.FirstOrDefault()?.PersonalGlobal() ?? 0;
                        tab.GlobalSlider.SetValueInstant(global);
                        tab.GlobalValue.Value = global;
                    }
                }
                finally { _syncing = false; }
                return;
            }

            foreach (var p in loaded)
                if (old.TryGetValue((p.Type, p.Field), out var prev))
                    p.SetPersonal(prev.Personal);
        }

        _parameters = loaded;

        // rebuild UI
        _tabs.Clear();
        _rows.Clear();
        NavHost.Children.Clear();
        PagesHost.Children.Clear();
        _simplePage = null; // rebuilt below if SIMPLE is active

        foreach (var (id, title) in FovModel.Groups)
        {
            var tabParams = _parameters.Where(p => p.Group == id).ToList();
            if (tabParams.Count == 0) continue;
            var tab = BuildTab(id, title, tabParams);
            _tabs.Add(tab);
        }

        if (_simpleMode)
        {
            // SIMPLE: hide the fresh tab pages, rebuild the simple page
            foreach (var tab in _tabs)
            {
                tab.Page.Visibility = Visibility.Collapsed;
                tab.Page.Opacity = 1;
            }
            BuildSimplePage();
        }
        else
        {
            // reselect the tab that was open, if it still exists
            var toSelect = _tabs.FirstOrDefault(t => t.Id == _activeTab?.Id) ?? _tabs.FirstOrDefault();
            if (toSelect is not null)
                SelectTab(toSelect, animate: animateSwitch);
        }
    }

    private Tab BuildTab(string id, string title, List<FovParam> tabParams)
    {
        // ---- page: global shift + rows in a scroller
        var globalSlider = new FovSlider
        {
            Minimum = -100, Maximum = 100, Value = 0, DefaultValue = 0
        };
        var globalValue = new FovValueBox { Value = 0 };

        var globalRow = new Grid();
        globalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        globalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        globalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var gl = new TextBlock
        {
            Text = "Global Shift",
            Style = (Style)FindResource("SectionLabel"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(gl, 0);
        Grid.SetColumn(globalSlider, 1);
        Grid.SetColumn(globalValue, 2);
        globalRow.Children.Add(gl);
        globalRow.Children.Add(globalSlider);
        globalRow.Children.Add(globalValue);

        var globalPanel = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            CornerRadius = new CornerRadius(8),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = globalRow
        };

        var rowsHost = new StackPanel();
        var scroller = new ScrollViewer
        {
            Style = (Style)FindResource("Viewer"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rowsHost
        };
        scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;

        var rows = new List<RowControls>();
        foreach (var p in tabParams)
        {
            var (rc, row) = BuildRow(p);
            rows.Add(rc);
            rowsHost.Children.Add(row);
        }

        var page = new Border
        {
            Child = new DockPanel { Children = { globalPanel, scroller } },
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        DockPanel.SetDock(globalPanel, Dock.Top);
        PagesHost.Children.Add(page);

        // ---- nav button
        var navButton = new Button
        {
            Style = (Style)FindResource("NavButton"),
            Content = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextBrush")
            },
            Height = 36
        };
        navButton.Click += (_, _) => SelectTab(_tabs.First(t => t.NavButton == navButton), animate: true);
        // right click on the tab: lock/unlock � the tab dims and presets skip it
        navButton.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var t = _tabs.First(x => x.NavButton == navButton);
            t.Locked = !t.Locked;
            var target = t.Locked ? 0.35 : 1.0;
            if (Motion.ReduceMotion)
                navButton.Opacity = target;
            else
                navButton.BeginAnimation(OpacityProperty,
                    Motion.Value(target, 160, EasingMode.EaseOut));
        };
        NavHost.Children.Add(navButton);

        var tab = new Tab
        {
            Id = id, Title = title, Page = page,
            GlobalSlider = globalSlider, GlobalValue = globalValue,
            Rows = rows, Params = tabParams, NavButton = navButton
        };

        globalSlider.ValueChanged += (_, e) =>
        {
            if (_syncing || _activeTab != tab) return;
            ApplyTabGlobal(tab, e.NewValue);
            MarkCustom();
        };
        globalSlider.EditStarted += (_, _) => CaptureHistory();
        globalValue.ValueCommitted += (_, v) =>
        {
            if (_syncing || _activeTab != tab) return;
            CaptureHistory();
            globalSlider.SetValueInstant(Math.Min(Math.Max(v, globalSlider.Minimum), globalSlider.Maximum));
            ApplyTabGlobal(tab, v);
        };
        globalSlider.ResetRequested += (_, _) =>
        {
            if (_activeTab != tab) return;
            ApplyTabGlobal(tab, 0);
        };

        return tab;
    }

    private (RowControls, Border) BuildRow(FovParam p)
    {
        // symmetric track: 0 sits exactly at the middle on every slider
        var slider = new FovSlider
        {
            Minimum = -FovModel.MaxSlider,
            Maximum = FovModel.MaxSlider,
            Value = p.BaseDefault,
            DefaultValue = p.BaseDefault
        };
        var valueBox = new FovValueBox { Value = p.BaseDefault };
        var label = new TextBlock
        {
            Text = p.Label,
            Style = (Style)FindResource("BodyText"),
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        TooltipService.Attach(label, p.Type, p.Description);
        // left click: copy the full metadata type name
        label.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                CopyTypeName(label, p.Type);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueBox, 2);
        grid.Margin = new Thickness(6, 3, 6, 3);
        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(valueBox);

        var row = new Border
        {
            Child = grid,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0, 4, 0)
        };

        var rc = new RowControls { Slider = slider, ValueBox = valueBox, Label = label, Row = row };
        _rows[p] = rc;

        // right click on the label: lock/unlock � the row dims; the tab's
        // global shift and presets no longer move this parameter
        label.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            rc.Locked = !rc.Locked;
            var target = rc.Locked ? 0.35 : 1.0;
            if (Motion.ReduceMotion)
                row.Opacity = target;
            else
                row.BeginAnimation(OpacityProperty,
                    Motion.Value(target, 160, EasingMode.EaseOut));
        };

        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                p.SetPersonal((float)(e.NewValue - p.BaseDefault));
                valueBox.Value = e.NewValue + p.PersonalGlobal();
                MarkCustom();
            }
            finally { _syncing = false; }
        };
        slider.EditStarted += (_, _) => CaptureHistory();

        valueBox.ValueCommitted += (_, v) =>
        {
            if (_syncing) return;
            CaptureHistory();
            _syncing = true;
            try
            {
                p.SetPersonal((float)(v - p.BaseDefault - p.PersonalGlobal()));
                valueBox.Value = v;
                slider.SetValueInstant(Math.Min(Math.Max(v - p.PersonalGlobal(), slider.Minimum), slider.Maximum));
                MarkCustom();
            }
            finally { _syncing = false; }
        };

        return (rc, row);
    }

    // ---------------------------------------------------------------- tab switching

    private void SelectTab(Tab tab, bool animate)
    {
        if (_activeTab == tab) return;
        _activeTab = tab;

        // hide all other pages at once; the incoming page fades in on top
        foreach (var t in _tabs)
        {
            if (t == tab) continue;
            t.Page.BeginAnimation(OpacityProperty, null);
            t.Page.Opacity = 1;
            t.Page.Visibility = Visibility.Collapsed;
            t.NavButton.Tag = null;
        }

        tab.NavButton.Tag = "active";
        tab.Page.Visibility = Visibility.Visible;

        if (!animate || Motion.ReduceMotion)
        {
            tab.Page.BeginAnimation(OpacityProperty, null);
            tab.Page.Opacity = 1;
        }
        else
        {
            tab.Page.Opacity = 0;
            tab.Page.BeginAnimation(OpacityProperty,
                Motion.Value(1, PageCrossFade, EasingMode.EaseOut));
        }
    }

    /// <summary>Global shift applies only to this tab's parameters.</summary>
    private void ApplyTabGlobal(Tab tab, double global)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var rounded = Math.Round(global);
            foreach (var p in tab.Params)
            {
                if (_absoluteShift)
                {
                    p.SetPersonal((float)(rounded - p.BaseDefault));
                    p.SetGlobal(0);
                }
                else
                {
                    p.SetGlobal((float)rounded);
                }
            }
            tab.GlobalValue.Value = rounded;
            foreach (var rc in tab.Rows)
            {
                var p = tab.Params[tab.Rows.IndexOf(rc)];
                if (rc.Locked)
                {
                    p.SetGlobal(0); // locked rows are not shifted
                    rc.ValueBox.Value = p.Values.FirstOrDefault();
                    continue;
                }
                p.SetGlobal((float)rounded);
                rc.ValueBox.Value = p.Values.FirstOrDefault();
                var shown = p.BaseDefault + p.Personal + rounded;
                rc.Slider.SetValueInstant(Math.Min(Math.Max(shown, rc.Slider.Minimum), rc.Slider.Maximum));
            }
        }
        finally { _syncing = false; }
    }

    // ---------------------------------------------------------------- build / export

    private void BtnBuild_Click(object sender, RoutedEventArgs e)
    {
        if (_lastBuiltPath is not null && DateTime.UtcNow < _doneVisibleUntil && File.Exists(_lastBuiltPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastBuiltPath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        try
        {
            var path = ModpackBuilder.Build(EffectiveBuildParams(), BaseYmtFileName, out var firstFp,
                ".oiv", SimpleDescription(), SimpleNameSuffix());
            MoveToExportPath(ref path);
            BuildDoneEffect(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Build failed:\n" + ex.Message, "Custom FOV",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private DateTime _popupClosedAt = DateTime.MinValue;

    private void ExportPopup_Closed(object? sender, EventArgs e)
    {
        _popupClosedAt = DateTime.UtcNow;
    }

    private void BtnBuildExtra_Click(object sender, RoutedEventArgs e)
    {
        // StaysOpen=False closes the popup on the chevron mouse-down, just
        // before this click handler runs. If it closed very recently, the
        // click was the toggle-off: do not reopen.
        if ((DateTime.UtcNow - _popupClosedAt).TotalMilliseconds < 200 && !ExportPopup.IsOpen)
            return;
        ExportPopup.IsOpen = true;
    }

    /// <summary>Builds the same modpack, only as a .zip instead of .oiv.</summary>
    private void BtnBuildZip_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        try
        {
            var path = ModpackBuilder.Build(EffectiveBuildParams(), BaseYmtFileName, out var firstFp,
                ".zip", SimpleDescription(), SimpleNameSuffix());
            MoveToExportPath(ref path);
            BuildDoneEffect(path);
                    }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Build failed:\n" + ex.Message, "Custom FOV",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Builds the untouched reference cameras.ymt as a named OIV.
    /// It deliberately ignores all editor, import, preset, and SIMPLE values.</summary>
    private void BtnBuildOriginal_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        try
        {
            var path = ModpackBuilder.BuildOriginal(BaseYmtFileName);
            MoveToExportPath(ref path);
            BuildDoneEffect(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Build failed:\n" + ex.Message, "FOV Modpack Builder",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPopup_Opened(object? sender, EventArgs e)
    {
        // fade the flyout in once positioned
        ExportFlyoutBorder.BeginAnimation(OpacityProperty, null);
        ExportFlyoutBorder.Opacity = 0;
        ExportFlyoutBorder.BeginAnimation(OpacityProperty,
            Motion.Value(1, 160, EasingMode.EaseOut));
    }

    /// <summary>Exports only the patched cameras.ymt, next to the executable.
    /// Existing files are never overwritten: duplicates get " (n)" like Windows.</summary>
    private void BtnExportYmt_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var data = File.ReadAllBytes(Path.Combine(appDir, BaseYmtFileName));
            foreach (var p in EffectiveBuildParams())
            {
                var vals = p.Values.ToList();
                for (int i = 0; i < p.Offsets.Count && i < vals.Count; i++)
                    FovModel.WriteBeF32(data, p.Offsets[i], vals[i]);
            }
            PsoChecksum.Update(data);

            var path = Path.Combine(appDir, UniquePath(Path.Combine(appDir, "cameras.ymt")));
            File.WriteAllBytes(path, data);
                    }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed:\n" + ex.Message, "Custom FOV",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Returns a path that does not exist yet, appending " (n)" before the
    /// extension the way Windows Explorer does.</summary>
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }


    /// <summary>The effective parameters for building: in SIMPLE mode the
    /// personal offsets come from the simple store, so a build reflects what
    /// the SIMPLE sliders show. ADVANCED builds use the model as is.</summary>
    private List<FovParam> EffectiveBuildParams()
    {
        if (!_simpleMode) return _parameters;

        // The three visible SIMPLE master values are the contract with the
        // user. Previously the pack was built from _simpleShifts instead;
        // stale per-parameter entries could make the OIV disagree with the
        // values printed in its own description (for example, vehicle 50 but
        // interior cameras 75). Derive every simple shift from the controls
        // that are actually on screen.
        var clone = _parameters.Select(p => new FovParam
        {
            Type = p.Type, Field = p.Field, Label = p.Label, Description = p.Description, Group = p.Group,
            Offsets = p.Offsets, Defaults = p.Defaults
        }).ToList();
        foreach (var (label, selection) in _simpleSelections)
        {
            var master = SimpleMasterValue(label);
            foreach (var source in selection)
            {
                var p = clone.First(x => x.Type == source.Type && x.Field == source.Field);
                p.SetPersonal(master - p.BaseDefault);
            }
        }
        return clone;
    }



    /// <summary>Description for SIMPLE builds: the three master values.</summary>
    private string? SimpleDescription()
    {
        if (!_simpleMode) return null;
        var fp = SimpleMasterValue("First person");
        var veh = SimpleMasterValue("In vehicle");
        var tp = SimpleMasterValue("Third person");
        return $"First Person {fp:0.#} | In Vehicle {veh:0.#} | Third Person {tp:0.#}";
    }

    /// <summary>Filename suffix for SIMPLE builds: the same three values that
    /// appear in the modpack description, without the explanatory labels.</summary>
    private string? SimpleNameSuffix()
    {
        if (!_simpleMode) return null;
        var fp = SimpleMasterValue("First person");
        var veh = SimpleMasterValue("In vehicle");
        var tp = SimpleMasterValue("Third person");
        return $"{fp:0.#} {veh:0.#} {tp:0.#}";
    }

    private float SimpleMasterValue(string label)
    {
        if (_simplePage is null) return 0;
        foreach (var child in ((StackPanel)_simplePage.Child).Children.OfType<Border>())
        {
            if (child.Child is not Grid g) continue;
            var lbl = g.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == label);
            if (lbl is null) continue;
            var box = g.Children.OfType<FovValueBox>().FirstOrDefault();
            if (box is not null) return (float)box.Value;
        }
        return 0;
    }

    /// <summary>Moves the built file to the chosen export folder, if any.</summary>
    private void MoveToExportPath(ref string path)
    {
        if (string.IsNullOrEmpty(_exportPath) || !Directory.Exists(_exportPath)) return;
        var dest = UniquePath(Path.Combine(_exportPath, Path.GetFileName(path)));
        File.Move(path, dest);
        path = dest;
    }

    /// <summary>Build feedback: the label flips to "Open folder", the button body
    /// turns green for 3.5 s. Clicking it while shown reveals the created file.</summary>
    private async void BuildDoneEffect(string path)
    {
        if (BuildRoot is null) return;
        _lastBuiltPath = path;
        _doneVisibleUntil = DateTime.UtcNow.AddMilliseconds(3500);
        // lock the button's width so "Open folder" does not resize the body
        BtnBuild.MinWidth = BtnBuild.ActualWidth;
        BuildRoot.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4E));
        var oldContent = BtnBuild.Content;
        BtnBuild.Content = "Open folder";
        BtnBuild.Foreground = Brushes.White;
        BtnBuildExtra.Foreground = Brushes.White;
        await Task.Delay(3500);
        if (DateTime.UtcNow < _doneVisibleUntil) return;
        BuildRoot.Background = Brushes.White;
        BtnBuild.Content = oldContent;
        BtnBuild.Foreground = Brushes.Black;
        BtnBuildExtra.Foreground = Brushes.Black;
        BtnBuild.MinWidth = 0;
        _lastBuiltPath = null;
    }

    // ---------------------------------------------------------------- import & export path

    /// <summary>Imports FOV values from a cameras.ymt or a modpack archive.
    /// The pristine base file is never replaced: imported non-FOV settings
    /// cannot leak into newly built modpacks.</summary>
    private void BtnImportYmt_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import cameras.ymt or modpack",
            Filter = "Supported files (*.ymt;*.oiv;*.zip)|*.ymt;*.oiv;*.zip|YMT files (*.ymt)|*.ymt|OIV modpacks (*.oiv)|*.oiv|ZIP archives (*.zip)|*.zip",
            FileName = "cameras.ymt"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var data = CameraFileImporter.ReadCamerasYmt(dlg.FileName);
            // Import only FOV values. The base YMT remains pristine.
            _canRollbackImportedFile = true;

            var imported = FovModel.LoadValuesOnly(data, _parameters);
            ApplyImported(imported);
            // Imported values are most useful when all affected fields are
            // immediately visible, rather than hidden behind SIMPLE's three
            // master controls.
            ModeAdvanced.IsChecked = true;
            SetSimpleMode(false);
            FlashStatusQuiet("Imported cameras.ymt");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed:\n" + ex.Message,
                "FOV Modpack Builder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Discards imported values and restores the original defaults.</summary>
    private void StatusText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_canRollbackImportedFile) return;
        try
        {
            ResetImportedValuesToDefaults();
            _canRollbackImportedFile = false;
            ResetStatusAppearance();
            FlashStatus("Import undone", 3000);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not undo the import:\n" + ex.Message,
                "FOV Modpack Builder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StatusText_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_canRollbackImportedFile) return;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5E, 0x70));
    }

    private void StatusText_MouseLeave(object sender, MouseEventArgs e) => ResetStatusAppearance();

    private void ResetStatusAppearance() => StatusText.Foreground = (Brush)FindResource("TextBrush");

    private void ResetImportedValuesToDefaults()
    {
        _syncing = true;
        try
        {
            _simpleShifts.Clear();
            foreach (var p in _parameters) p.Reset();
            foreach (var row in _rows.Values) row.Slider.AccentBrush = Brushes.White;
            DisarmPresetVisuals();
            _presetShift = null;
            PresetOverlay.Visibility = Visibility.Collapsed;
            foreach (var tab in _tabs)
            {
                tab.GlobalSlider.SetValueInstant(0);
                tab.GlobalValue.Value = 0;
                foreach (var row in tab.Rows)
                {
                    var p = tab.Params[tab.Rows.IndexOf(row)];
                    row.Slider.SetValueInstant(p.BaseDefault);
                    row.ValueBox.Value = p.BaseDefault;
                }
            }
            if (_simpleMode) RefreshSimplePage();
        }
        finally { _syncing = false; }
        SaveSettings();
    }

    /// <summary>Applies imported (type,field)->value data as personal offsets.
    /// The import is a fresh start: global shifts zero, presets cleared, no
    /// CUSTOM — nothing survives from before the import.</summary>
    private void ApplyImported(Dictionary<(string type, string field), float> imported)
    {
        _syncing = true;
        try
        {
            // wipe everything from the previous state
            _simpleShifts.Clear();
            foreach (var p in _parameters)
                p.Reset();
            foreach (var row in _rows.Values)
                row.Slider.AccentBrush = Brushes.White;
            DisarmPresetVisuals();
            PresetOverlay.Visibility = Visibility.Collapsed;
            foreach (var tab in _tabs)
            {
                tab.GlobalSlider.SetValueInstant(0);
                tab.GlobalValue.Value = 0;
            }
            foreach (var p in _parameters)
            {
                if (imported.TryGetValue((p.Type, p.Field), out var v))
                {
                    p.SetPersonal(v - p.BaseDefault);
                    p.SetGlobal(0);
                    if (_rows.TryGetValue(p, out var row))
                        row.Slider.AccentBrush = Math.Abs(v - p.BaseDefault) > 0.001f
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x3D))
                            : Brushes.White;
                }
            }
            // refresh every visible slider
            foreach (var tab in _tabs)
            {
                foreach (var rc in tab.Rows)
                {
                    var p = tab.Params[tab.Rows.IndexOf(rc)];
                    var shown = p.BaseDefault + p.Personal + p.PersonalGlobal();
                    rc.Slider.SetValueInstant(Math.Min(Math.Max(shown, rc.Slider.Minimum), rc.Slider.Maximum));
                    rc.ValueBox.Value = shown;
                }
            }
            // SIMPLE page: values derive from the model base, rebuilt
            if (_simpleMode) BuildSimplePage();
        }
        finally { _syncing = false; }
    }

    /// <summary>Chooses where modpacks are exported; empty keeps the app folder.</summary>
    private void BtnExportPath_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the export folder"
        };
        if (dlg.ShowDialog(this) != true) return;
        _exportPath = dlg.FolderName;
        SaveSettings();
    }

    private string? _exportPath;

    // ---------------------------------------------------------------- reset all

    /// <summary>Resets every parameter of every tab to its original value.
    /// First click arms the button: the label turns red "Are you sure?"; the
    /// second click within 3 seconds performs the reset, otherwise it disarms.</summary>
    private void BtnSetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (!_defaultsArmed)
        {
            _defaultsArmed = true;
            DefaultsLabel.Text = "Are you sure?";
            DefaultsLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5E, 0x70));
            BtnSetDefaults.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5E, 0x70));
            _defaultsTimer?.Stop();
            _defaultsTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
                (_, _) => DisarmDefaults(), Dispatcher);
            _defaultsTimer.Start();
            return;
        }
        DisarmDefaults();
        CaptureHistory();

        // SIMPLE mode: reset through the master sliders so the simple page
        // reflects the reset as well
        if (_simpleMode && _simplePage is not null)
        {
            _syncing = true;
            try
            {
                // SIMPLE reset: only the simple store; ADVANCED personal edits
                // and presets are untouched
                _simpleShifts.Clear();
                foreach (var child in ((StackPanel)_simplePage.Child).Children.OfType<Border>())
                {
                    var grid = child.Child as Grid;
                    if (grid is null) continue;
                    var slider = grid.Children.OfType<FovSlider>().FirstOrDefault();
                    var box = grid.Children.OfType<FovValueBox>().FirstOrDefault();
                    if (slider is not null)
                    {
                        var baseDefault = slider.DefaultValue;
                        slider.SetValueInstant(baseDefault);
                        if (box is not null) box.Value = baseDefault;
                    }
                }
            }
            finally { _syncing = false; }
            SaveSettings();
            return;
        }

        _presetShift = null;
        _presetPlaced = false;
        _presetShift = null;
        PresetOverlay.Visibility = Visibility.Collapsed;
        // uncheck the radio so it can be clicked again; keep the labels at
        // resting grey; only the white backing disappears
        PresetX15.IsChecked = false;
        PresetX30.IsChecked = false;
        PresetX60.IsChecked = false;
        PresetBacking.BeginAnimation(WidthProperty, null);
        PresetBacking.BeginAnimation(MarginProperty, null);
        PresetBacking.Width = 0;
        PresetX15.Foreground = (Brush)FindResource("SubTextBrush");
        PresetX30.Foreground = (Brush)FindResource("SubTextBrush");
        PresetX60.Foreground = (Brush)FindResource("SubTextBrush");
        _p15Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);
        _p30Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);
        _p60Brush.Color = Color.FromRgb(0x8A, 0x8A, 0x8A);

        _syncing = true;
        try
        {
            foreach (var p in _parameters)
                p.Reset();
            foreach (var row in _rows.Values)
                row.Slider.AccentBrush = Brushes.White;
            foreach (var tab in _tabs)
            {
                // global reset: locked tabs come back to life too
                if (tab.Locked)
                {
                    tab.Locked = false;
                    tab.NavButton.BeginAnimation(OpacityProperty, null);
                    tab.NavButton.Opacity = 1.0;
                }
                tab.GlobalSlider.SetValueInstant(0);
                tab.GlobalValue.Value = 0;
                foreach (var rc in tab.Rows)
                {
                    var p = tab.Params[tab.Rows.IndexOf(rc)];
                    // global reset: locked rows come back to life too
                    if (rc.Locked)
                    {
                        rc.Locked = false;
                        tab.Rows.First(x => x == rc).Row.Opacity = 1.0;
                    }
                    rc.Slider.SetValueInstant(p.BaseDefault);
                    rc.ValueBox.Value = p.BaseDefault;
                }
            }
        }
        finally { _syncing = false; }
            }

    // ---------------------------------------------------------------- pristine base camera file

    private const string BaseYmtFileName = "cameras_original.ymt";

    // ---------------------------------------------------------------- settings persistence

    private class AppSettings
    {
        public bool SimpleMode { get; set; }
        public string Preset { get; set; } = "";
        public List<ParamState>? Params { get; set; }
        public List<string>? LockedTabs { get; set; }
        public bool AbsoluteShift { get; set; }
        public Dictionary<string, float>? SimpleShifts { get; set; }
        public string? ExportPath { get; set; }
    }

    private class ParamState
    {
        public string Type { get; set; } = "";
        public string Field { get; set; } = "";
        public double Personal { get; set; }
        public double Global { get; set; }
        public bool Locked { get; set; }
    }

    private sealed class EditorState
    {
        public required List<ParamState> Params { get; init; }
        public required Dictionary<string, float> SimpleShifts { get; init; }
        public float? Preset { get; init; }
        public bool IsCustom { get; init; }
        public bool AbsoluteShift { get; init; }
    }

    // ---------------------------------------------------------------- undo / redo

    private EditorState CaptureEditorState() => new()
    {
        Params = _parameters.Select(p => new ParamState
        {
            Type = p.Type,
            Field = p.Field,
            Personal = p.Personal,
            Global = p.PersonalGlobal(),
            Locked = _rows.TryGetValue(p, out var row) && row.Locked
        }).ToList(),
        SimpleShifts = _simpleShifts.ToDictionary(kv => kv.Key.type + "|" + kv.Key.field, kv => kv.Value),
        Preset = _presetShift,
        IsCustom = PresetOverlay.Visibility == Visibility.Visible,
        AbsoluteShift = _absoluteShift
    };

    private void CaptureHistory()
    {
        if (_syncing || _restoringHistory || _parameters.Count == 0) return;
        if (_undo.Count >= HistoryLimit) _undo.Clear();
        _undo.Push(CaptureEditorState());
        _redo.Clear();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control && Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        if (e.Key == Key.Z)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Redo();
            else Undo();
            e.Handled = true;
        }
    }

    /// <summary>FovSlider deliberately does not take keyboard focus while it
    /// drags. Commit an active number box before any other control is clicked,
    /// rather than requiring Enter or a click into a focusable element.</summary>
    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var clicked = e.OriginalSource as DependencyObject;
        var boxes = _rows.Values.Select(r => r.ValueBox)
            .Concat(_tabs.Select(t => t.GlobalValue))
            .Concat(GetSimpleValueBoxes());
        foreach (var box in boxes)
        {
            if (box.IsEditing && !IsDescendantOf(clicked, box))
                box.CommitEdit();
        }
    }

    private IEnumerable<FovValueBox> GetSimpleValueBoxes()
    {
        if (_simplePage?.Child is not StackPanel stack) yield break;
        foreach (var border in stack.Children.OfType<Border>())
            if (border.Child is Grid grid)
                foreach (var box in grid.Children.OfType<FovValueBox>())
                    yield return box;
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(CaptureEditorState());
        RestoreEditorState(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(CaptureEditorState());
        RestoreEditorState(_redo.Pop());
    }

    private void RestoreEditorState(EditorState state)
    {
        _restoringHistory = true;
        _syncing = true;
        try
        {
            var saved = state.Params.ToDictionary(s => (s.Type, s.Field));
            foreach (var p in _parameters)
            {
                if (!saved.TryGetValue((p.Type, p.Field), out var value)) continue;
                p.SetPersonal((float)value.Personal);
                p.SetGlobal((float)value.Global);
                if (_rows.TryGetValue(p, out var row))
                {
                    row.Locked = value.Locked;
                    row.Row.Opacity = value.Locked ? 0.35 : 1;
                    var shown = p.BaseDefault + p.Personal + p.PersonalGlobal();
                    row.Slider.SetValueInstant(Math.Min(Math.Max(shown, row.Slider.Minimum), row.Slider.Maximum));
                    row.ValueBox.Value = shown;
                }
            }
            _simpleShifts.Clear();
            foreach (var (key, value) in state.SimpleShifts)
            {
                var parts = key.Split('|', 2);
                if (parts.Length == 2) _simpleShifts[(parts[0], parts[1])] = value;
            }
            SetAbsoluteShift(state.AbsoluteShift, silent: false);
            foreach (var tab in _tabs)
            {
                var global = tab.Params.FirstOrDefault()?.PersonalGlobal() ?? 0;
                tab.GlobalSlider.SetValueInstant(global);
                tab.GlobalValue.Value = global;
            }
            DisarmPresetVisuals();
            _presetShift = state.Preset;
            PresetOverlay.Visibility = state.IsCustom ? Visibility.Visible : Visibility.Collapsed;
            if (state.Preset is 15 or 30 or 60)
            {
                (state.Preset.Value switch { 15 => PresetX15, 30 => PresetX30, _ => PresetX60 }).IsChecked = true;
                UpdatePresetBacking(animate: false);
                AnimatePresetLabels((int)state.Preset.Value);
            }
        }
        finally
        {
            _syncing = false;
            _restoringHistory = false;
        }

        if (_simpleMode) RefreshSimplePage();
        SaveSettings();
    }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FovApp", "settings.json");

    private static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath));
                if (s is not null) return s;
            }
        }
        catch { /* corrupt or missing settings fall back to the default */ }
        return new AppSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var state = new AppSettings
            {
                SimpleMode = _simpleMode,
                Preset = _presetShift?.ToString() ?? "",
                AbsoluteShift = _absoluteShift,
                Params = _parameters.Select(p => new ParamState
                {
                    Type = p.Type, Field = p.Field,
                    Personal = p.Personal, Global = p.PersonalGlobal(),
                    Locked = _rows.TryGetValue(p, out var rc) && rc.Locked
                }).ToList(),
                SimpleShifts = _simpleShifts.ToDictionary(
                    kv => kv.Key.type + "|" + kv.Key.field, kv => kv.Value),
                ExportPath = _exportPath,
                LockedTabs = _tabs.Where(t => t.Locked).Select(t => t.Id).ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(state));
        }
        catch { /* persistence is best effort */ }
    }

    /// <summary>Restores personal/global/locked state saved earlier.</summary>
    private void RestoreSettings(AppSettings s)
    {
        // absolute shift toggle
        SetAbsoluteShift(s.AbsoluteShift, silent: true);

        // locked tabs
        if (s.LockedTabs is not null)
            foreach (var tab in _tabs)
                if (s.LockedTabs.Contains(tab.Id))
                {
                    tab.Locked = true;
                    tab.NavButton.Opacity = 0.35;
                }

        // parameter state (guard _syncing: programmatic slider updates must
        // not be mistaken for user edits and flip the presets to CUSTOM)
        if (s.Params is null) return;
        var byKey = s.Params.ToDictionary(x => (x.Type, x.Field));
        _syncing = true;
        try
        {
            foreach (var p in _parameters)
            {
                if (!byKey.TryGetValue((p.Type, p.Field), out var st)) continue;
                p.SetPersonal((float)st.Personal);
                p.SetGlobal((float)st.Global);
                var rc = _rows[p];
                rc.Locked = st.Locked;
                if (rc.Locked) rc.Row.Opacity = 0.35;
                var shown = p.BaseDefault + p.Personal + p.PersonalGlobal();
                rc.Slider.SetValueInstant(Math.Min(Math.Max(shown, rc.Slider.Minimum), rc.Slider.Maximum));
                rc.ValueBox.Value = shown;
            }
        }
        finally { _syncing = false; }

        // locked tabs' global sliders show their stored shift
        foreach (var tab in _tabs)
        {
            var g = tab.Params.FirstOrDefault()?.PersonalGlobal() ?? 0;
            tab.GlobalSlider.SetValueInstant(g);
            tab.GlobalValue.Value = g;
        }

        _exportPath = s.ExportPath;

        // simple-mode shifts
        if (s.SimpleShifts is not null)
            foreach (var kv in s.SimpleShifts)
            {
                var parts = kv.Key.Split('|', 2);
                if (parts.Length == 2)
                    _simpleShifts[(parts[0], parts[1])] = kv.Value;
            }

        // preset selection
        if (int.TryParse(s.Preset, out var preset) && preset is 15 or 30 or 60)
        {
            (preset switch { 15 => PresetX15, 30 => PresetX30, _ => PresetX60 }).IsChecked = true;
            _presetShift = preset;
            Dispatcher.BeginInvoke(() => UpdatePresetBacking(false), DispatcherPriority.Render);
        }
        else if (s.Params?.Any(x => x.Personal != 0 || x.Global != 0) == true)
        {
            MarkCustom();
        }
    }

    // ---------------------------------------------------------------- misc UI

    private void FlashStatusQuiet(string message)
    {
        StatusText.Text = message;
    }

    private async void FlashStatus(string message, int durationMilliseconds = 2500)
    {
        StatusText.Text = message;
        await Task.Delay(durationMilliseconds);
        if (StatusText.Text == message)
            StatusText.Text = "";
    }

    private void CopyTypeName(TextBlock label, string typeName)
    {
        try { Clipboard.SetText(typeName); }
        catch { /* clipboard can be busy; the fade still runs */ }

        if (Motion.ReduceMotion) return;
        var fade = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var back = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => label.BeginAnimation(OpacityProperty, back);
        label.BeginAnimation(OpacityProperty, fade);
    }

    private double _wheelAccum;
    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = (ScrollViewer)sender;
        _wheelAccum -= e.Delta;
        e.Handled = true;
        if (_wheelAccum > 52) { scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 52); _wheelAccum = 0; }
        else if (_wheelAccum < -52) { scroller.ScrollToVerticalOffset(scroller.VerticalOffset - 52); _wheelAccum = 0; }
    }

    // ---------------------------------------------------------------- chrome

    // Windows 11 Mica backdrop: the system title bar goes transparent and the
    // window material shows through, like Explorer.
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // Mica: DWMWA_SYSTEMBACKDROP_TYPE = 38, value 2 = Mica
        var mica = 2;
        DwmSetWindowAttribute(handle, 38, ref mica, sizeof(int));
        // dark mode title bar: DWMWA_USE_IMMERSIVE_DARK_MODE = 20
        var dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
