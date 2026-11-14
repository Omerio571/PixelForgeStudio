using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;
using System.ComponentModel;
using System.Reflection;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private IEnumerable<Expander> SettingsSections() => new[]
    {
        ModelSection, PaletteSection, DimensionsSection, CameraSection,
        LightingSection, OutlinesSection, AnimationSection
    };

    private void InitializePresentation()
    {
        var information = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        VersionLabel.Text = $"PixelForge Studio → v{information.Split('+')[0]}";
        BuildIdentity.Text = "Build · " + information;
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler statusChanged = (_, _) =>
        {
            DiagnosticLog.AppendText($"{DateTime.Now:HH:mm:ss} · {Status.Text}{Environment.NewLine}");
            if (DiagnosticLog.Text.Length > 64000) DiagnosticLog.Text = DiagnosticLog.Text[^48000..];
        };
        descriptor.AddValueChanged(Status, statusChanged);
        statusChanged(this, EventArgs.Empty);
        Closed += (_, _) => descriptor.RemoveValueChanged(Status, statusChanged);
        // Avoid Windows' cascading placement putting the tall workspace off screen.
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
        SetChecker();
        DpiChanged += (_, _) => UpdateZoom();
        SizeChanged += (_, _) =>
        {
            if (DetailsToggle.IsChecked == true) DiagnosticsRow.MaxHeight = Math.Min(420, Math.Max(140, ActualHeight * .45));
        };
        foreach (var section in SettingsSections())
        {
            section.Expanded += (_, _) => SavePreferences();
            section.Collapsed += (_, _) => SavePreferences();
        }
        foreach (var control in NumericControls(true))
            System.Windows.Automation.AutomationProperties.SetName(control, control.Name.Replace("Input", ""));
    }

    private void DetailsChanged(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsRow is null || DiagnosticsPanel is null) return;
        bool expanded = DetailsToggle.IsChecked == true;
        DiagnosticsRow.MaxHeight = Math.Min(420, Math.Max(140, ActualHeight * .45));
        DiagnosticsRow.MinHeight = expanded ? 140 : 0;
        DiagnosticsRow.Height = new GridLength(expanded ? Math.Min(260, DiagnosticsRow.MaxHeight) : 0);
        DiagnosticsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseDiagnostics(object sender, RoutedEventArgs e) => DetailsToggle.IsChecked = false;

    private void CopyDiagnostics(object sender, RoutedEventArgs e) => Try(() => Clipboard.SetText(
        string.Join(Environment.NewLine + Environment.NewLine,
            new[] { BuildIdentity.Text, Status.Text, OutputInfo.Text, ErrorDetails.Text, DiagnosticLog.Text }
                .Where(text => !string.IsNullOrWhiteSpace(text)))));

    private void UpdateCanvasInfo(ConversionOutput asset)
    {
        var pivot = ComparisonLayout.ReadAnchor(asset.Metadata);
        CanvasInfo.Text = pivot is { } p ? $"Ground pivot · {p.X:0.##}, {p.Y:0.##} px" : "Ground pivot unavailable";
    }

    // Integer device pixels, including fractional Windows display scaling.
    private static double PixelPreviewScale(double fit, int selection, double dpi) =>
        (selection == 0 ? Math.Max(1, Math.Floor(fit * dpi)) : Math.Pow(2, selection - 1)) / dpi;

    private void ThemeChanged(object sender, RoutedEventArgs e)
    {
        if (!workflowReady) return;
        ThemeManager.Apply(LightTheme.IsChecked == true ? "Light" : "Dark");
        SetChecker();
        ShowPalette();
        SavePreferences();
    }

    private void ShowPreview(object sender, RoutedEventArgs e) => CloseComparison();

    private void SettingsMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var save = new MenuItem { Header = "Save render settings…" };
        save.Click += SaveSettings;
        var load = new MenuItem { Header = "Load render settings…" };
        load.Click += LoadSettings;
        menu.Items.Add(save); menu.Items.Add(load);
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void ExportMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        foreach (var action in new (string Label, RoutedEventHandler Handler)[]
        {
            ("Save Aseprite…", SaveNative), ("Export PNG frames…", SaveFrames),
            ("Reload saved edits", ReloadEdits), ("Show working folder", ShowOutput),
            ("Preserve editable render", PreserveCurrent)
        })
        {
            var item = new MenuItem { Header = action.Label };
            item.Click += action.Handler;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // Application-owned prompts share the same controls and live resource palette.
    // System file dialogs keep the Windows shell's native accessibility and behavior.
    private MessageBoxResult ThemedMessage(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var result = buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };
        var dialog = new Window
        {
            Owner = this, Title = title, Width = 490, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No) },
            MessageBoxButton.OKCancel => new[] { ("Apply", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel) },
            MessageBoxButton.YesNoCancel => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel) },
            _ => new[] { ("OK", MessageBoxResult.OK) }
        };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Item1, IsDefault = choice.Item2 == result, IsCancel = choice.Item2 == result };
            button.Click += (_, _) => { result = choice.Item2; dialog.Close(); };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.ShowDialog();
        return result;
    }
}
