using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class ModernUiChecks
{
    public static void Run(string model, string animated, string directory,string? emissionAsset=null)
    {
        directory = Path.GetFullPath(Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new App(); app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Startup += async (_, _) =>
            {
                try { await Verify(app, Path.GetFullPath(model), Path.GetFullPath(animated), directory,emissionAsset); }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Modern WPF verification failed", failure);
    }

    private static async Task Verify(App app, string model, string animated, string root,string? emissionAsset)
    {
        Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE", Path.Combine(root, "profile"));
        var window = new MainWindow(); window.Show();
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
        T Find<T>(string name) => (T)window.FindName(name);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        object? Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        var events = new RoutedEventArgs();
        async Task Layout() { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        void Capture(string name)
        {
            window.UpdateLayout();
            var surface = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(surface);
            var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(root, name + ".png")); encoder.Save(stream);
        }
        string Settings() => JsonSerializer.Serialize((AssetSettings)Call("ReadSettings")!);
        await Layout();
        var area = SystemParameters.WorkArea;
        Check(window.Left >= area.Left && window.Top >= area.Top && window.Top + window.ActualHeight <= area.Bottom + 1,
            "startup centers the complete workspace within the available work area");
        Check(Find<StackPanel>("SettingsPanel").Children.OfType<Expander>().Count() == 7, "all seven settings sections retained");
        var widthColumn=Find<ColumnDefinition>("SettingsWidth");
        Check(widthColumn.ActualWidth>336 && widthColumn.ActualWidth<=400,"slightly wider initial sidebar keeps preview space");
        Find<Expander>("ManageColors").IsExpanded=true;
        foreach(bool light in new[]{false,true})
        {
            Find<RadioButton>(light?"LightTheme":"DarkTheme").IsChecked=true;await Layout();
            var actions=Find<WrapPanel>("PaletteActions");
            var buttons=actions.Children.OfType<Button>().ToArray();
            Check(buttons.Length==4&&buttons.Select(b=>b.TranslatePoint(new Point(),actions).Y).Distinct().Count()==1,
                "all palette actions share one row in "+(light?"Light":"Dark"));
        }
        Find<RadioButton>("DarkTheme").IsChecked=true;
        Find<Expander>("ManageColors").IsExpanded=false;
        var lighting=(StackPanel)Find<Expander>("LightingSection").Content;
        Check(lighting.Children[0]==Find<CheckBox>("DynamicLightingInput"),"Dynamic Lighting is the first setting in Lighting and Shadows");
        Find<CheckBox>("StudioLightingInput").IsChecked=false;
        Check(((StackPanel)Find<ResponsivePanel>("LightCards").Parent).Visibility==Visibility.Collapsed,
            "disabling studio hides dependent settings");
        Find<CheckBox>("GroundShadowsInput").IsChecked=true;
        Check(((StackPanel)Find<NumericSlider>("GroundStrengthInput").Parent).Visibility==Visibility.Visible,
            "ground shadow controls remain independently available");
        Find<CheckBox>("StudioLightingInput").IsChecked=true;
        Find<CheckBox>("GroundShadowsInput").IsChecked=false;
        widthColumn.Width=new GridLength(394);
        Find<TextBox>("ModelPath").Text = model;
        var before = Settings();
        Find<RadioButton>("LightTheme").IsChecked = true; await Layout();
        Check(ThemeManager.Current == "Light" && Settings() == before, "live theme switch preserves every rendering parameter");
        Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(248, 250, 249), "live light window resource resolved");
        Find<Expander>("LightingSection").IsExpanded = true;
        Find<NumericSlider>("LightIntensityInput").Value = 2.234567;
        Find<NumericSlider>("LightIntensityInput").NumericMode = true;
        Find<NumericSlider>("LightIntensityInput").NumericMode = false;
        Check(Find<NumericSlider>("LightIntensityInput").Value == 2.234567, "numeric mode switching retains full internal precision");
        Find<CheckBox>("GroundShadowsInput").IsChecked = false; await Layout();
        Check(((FrameworkElement)Find<NumericSlider>("GroundStrengthInput").Parent).Visibility == Visibility.Collapsed, "dependent shadow controls hide without clearing state");
        Find<Expander>("LightingSection").IsExpanded = false;
        Call("SavePreferences"); window.Close(); window = new MainWindow(); window.Show(); await Layout();
        Check(ThemeManager.Current == "Light" && Find<TextBox>("ModelPath").Text == model, "theme and model persist across actual window restart");
        Check(Find<ColumnDefinition>("SettingsWidth").ActualWidth==394,"sidebar width survives actual window restart");
        Check(!Find<Expander>("LightingSection").IsExpanded, "section expansion persists outside asset settings");
        Find<RadioButton>("DarkTheme").IsChecked = true;
        await (Task)Call("InspectSelectedModel")!;
        Check(Find<TextBlock>("ModelInfo").Text.Contains("frames"), "actual Blender model inspection remains connected");
        Find<TextBox>("WidthInput").Text = "128"; Find<TextBox>("HeightInput").Text = "128";
        async Task Generate()
        {
            Call("Generate", window, events);
            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (Field("cancellation") is not null && DateTime.UtcNow < deadline) await Task.Delay(100);
            Check(Field("cancellation") is null && Field("output") is ConversionOutput, "actual WPF Generate completes successfully");
            Call("PreserveCurrentCore"); await Layout();
        }
        await Generate();
        var still = (ConversionOutput)Field("output")!;
        Check(still.Frames.Length == 1 && File.Exists(still.Aseprite), "static generation validates native document and preview");
        Capture("implemented-dark");
        Find<RadioButton>("LightTheme").IsChecked = true; await Layout(); Capture("implemented-light");
        var image = Find<Image>("PreviewImage").Source;
        Find<ComboBox>("ViewInput").IsDropDownOpen = true; await Layout();
        Check(Find<ComboBox>("ViewInput").Template.FindName("PART_Popup", Find<ComboBox>("ViewInput")) is System.Windows.Controls.Primitives.Popup { IsOpen: true }, "themed dropdown opens in actual WPF");
        Find<ComboBox>("ViewInput").IsDropDownOpen = false;
        foreach (var size in new[] { (1000d, 740d), (1280d, 900d), (1600d, 1080d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; await Layout();
            Check(Find<Border>("PreviewBorder").ActualWidth > 400 && Find<Button>("GenerateButton").ActualWidth > 200, $"usable preview and generation at {size}");
        }
        window.Width = 1440; window.Height = 1000;
        Check(ReferenceEquals(image, Find<Image>("PreviewImage").Source), "theme changes and resizing retain cached artwork");
        var scaleMethod = typeof(MainWindow).GetMethod("PixelPreviewScale", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
        {
            double zoom = (double)scaleMethod.Invoke(null, new object[] { 4.7, 0, dpi })!;
            Check(Math.Abs(zoom * dpi - Math.Round(zoom * dpi)) < 1e-8, $"Fit uses integer device pixels at {dpi:P0} display scaling");
        }
        var workspace = ((Grid)window.Content).Children.OfType<Grid>().Single();
        workspace.ColumnDefinitions[0].Width = new GridLength(408);
        foreach (var section in Find<StackPanel>("SettingsPanel").Children.OfType<Expander>())
            section.IsExpanded = section.Name is "PaletteSection" or "LightingSection" or "OutlinesSection";
        Find<Expander>("ManageColors").IsExpanded = true;
        Find<CheckBox>("OutlineInput").IsChecked = true;
        await Layout(); Capture("implemented-expanded");
        Find<CheckBox>("OutlineInput").IsChecked = false;
        foreach (var section in Find<StackPanel>("SettingsPanel").Children.OfType<Expander>())
            section.IsExpanded = section.Name is "ModelSection" or "PaletteSection" or "DimensionsSection" or "CameraSection";
        Find<Expander>("ManageColors").IsExpanded = false;
        workspace.ColumnDefinitions[0].Width = new GridLength(336);
        Find<TextBox>("ModelPath").Text = animated;
        await (Task)Call("InspectSelectedModel")!;
        Find<CheckBox>("AnimationInput").IsChecked = true;
        Find<TextBox>("StartFrameInput").Text = "1"; Find<TextBox>("EndFrameInput").Text = "12";
        Find<TextBox>("FpsInput").Text = "12";
        await Generate();
        var animation = (ConversionOutput)Field("output")!;
        Check(animation.Frames.Length == 12, "twelve-frame animation generated through actual WPF handler");
        Call("PlayPause", window, events); await Task.Delay(220);
        Check(Find<Slider>("TimelineInput").Value > 0, "actual dispatcher playback advances the timeline");
        var timer = (DispatcherTimer)Field("playbackTimer")!;
        Find<RadioButton>("DarkTheme").IsChecked = true;
        Check(timer.IsEnabled, "live theme change leaves active animation playback running");
        Call("PausePlayback");
        var hashes = animation.Frames.ToDictionary(f => f.Png, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Png))));
        var cache = Field("playbackFrames");
        foreach (int fps in new[] { 6, 24, 7 })
        {
            Find<TextBox>("PreviewFpsInput").Text = fps.ToString();
            Check(ReferenceEquals(cache, Field("playbackFrames")) && ((ConversionOutput)Field("output")!).Frames.Length == 12, $"FPS {fps} reuses twelve cached frame images");
            Check(Find<TextBlock>("DurationInfo").Text.StartsWith(fps == 6 ? "2 s" : fps == 24 ? "0.5 s" : "1.71"), "playback duration updates immediately");
        }
        Find<TextBox>("PreviewFpsInput").Text = "12";
        // The real dialog's safe cancellation must leave all native bytes untouched.
        string nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(animation.Aseprite)));
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = app.Windows.OfType<Window>().Single(w => w.Owner == window && w.Title == "Apply animation timing");
            Buttons(dialog).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check(!(bool)Call("EnsureTimingApplied")! && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(animation.Aseprite))) == nativeHash, "Cancel timing prompt never writes native artwork");
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = app.Windows.OfType<Window>().Single(w => w.Owner == window && w.Title == "Apply animation timing");
            Buttons(dialog).Single(b => Equals(b.Content, "Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check((bool)Call("EnsureTimingApplied")!, "themed Apply timing dialog saves durations");
        animation = (ConversionOutput)Field("output")!;
        Check(hashes.All(h => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(h.Key))) == h.Value), "FPS experiment and saving preserve every PNG pixel file");
        string exports = Path.Combine(root, "exports"); Directory.CreateDirectory(exports);
        AssetExport.SaveFrame(animation, 3, Path.Combine(exports, "current.png"));
        AssetExport.SaveSpritesheet(animation, Path.Combine(exports, "sheet.png"));
        AssetExport.SaveNative(animation, Path.Combine(exports, "animation.aseprite"));
        Check(File.Exists(Path.Combine(exports, "sheet.json")), "PNG, native and spritesheet metadata export successfully");
        Find<RadioButton>("DarkTheme").IsChecked = true; await Layout(); Capture("implemented-animation");
        Call("ToggleCompare", window, events); await Layout();
        Find<ComboBox>("RenderAInput").SelectedIndex = 0;
        Find<ComboBox>("RenderBInput").SelectedIndex = 1;
        Check(Find<Border>("ComparisonPanel").IsVisible && !Find<StackPanel>("PrimaryExports").IsVisible, "comparison is read-only and hides export actions");
        Find<ComboBox>("CompareModeInput").SelectedIndex = 1; await Layout();
        Check(Find<Grid>("DividerPanel").Visibility == Visibility.Collapsed, "side-by-side comparison retains synchronized zoom");
        Find<ComboBox>("CompareModeInput").SelectedIndex = 0;
        Find<NumericSlider>("DividerInput").Value = 65; await Layout(); Capture("implemented-comparison");
        Check(hashes.All(h => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(h.Key))) == h.Value), "comparison uses saved pixels without changing image files");
        Call("CloseComparison");
        Find<ComboBox>("CameraFamilyInput").SelectedItem=CameraFamily.Top;
        Check(Find<ComboBox>("ViewInput").Items.Count==4 && Find<ComboBox>("ProjectionInput").Items.Count==3,
            "Top offers four cardinal directions and three applicable perspectives");
        Find<ComboBox>("ProjectionInput").SelectedValue=CameraPerspective.TopDownThreeQuarter;
        Find<NumericSlider>("CameraElevationInput").Value=65;
        Find<CheckBox>("AllDirectionsInput").IsChecked=true;
        Find<CheckBox>("AnimationInput").IsChecked=true;
        Find<TextBox>("EndFrameInput").Text="4";
        Find<TextBox>("FrameStepInput").Text="3";
        Find<Expander>("CameraSection").IsExpanded=true;
        await Layout();
        Check(!Find<ComboBox>("ViewInput").IsEnabled && Find<StackPanel>("CameraElevationPanel").IsVisible,
            "batch disables direction selection and tilted Top exposes elevation");
        Find<CheckBox>("DynamicLightingInput").IsChecked=true;
        Check(Find<NumericSlider>("CameraElevationInput").Unit=="°" &&
              Find<NumericSlider>("CameraElevationInput").Value==65,
            "camera elevation shows degrees while retaining its value");
        var cameraSettings=(AssetSettings)Call("ReadSettings")!;
        Check(cameraSettings.Camera?.Elevation==65 && cameraSettings.RenderAllDirections,"camera inputs reach asset settings");
        Call("Generate",window,events);
        var batchDeadline=DateTime.UtcNow.AddMinutes(4);
        while(Field("cancellation") is not null && DateTime.UtcNow<batchDeadline)await Task.Delay(100);
        var directionInput=Find<ComboBox>("RenderedDirectionInput");
        Check(directionInput.Items.Count==4,"real WPF generation produces four selectable renders");
        directionInput.SelectedItem=CameraDirection.W;
        var west=(ConversionOutput)Field("output")!;
        Check(AssetSettings.Load(Path.Combine(west.Directory,"asset.settings.json")).Camera?.Direction==CameraDirection.W,
            "direction switching uses the existing West render");
        var batch=(ConversionOutput[])Field("directionalBatch")!;
        var batchExport=AssetExport.SaveDirections(batch,exports);
        Check(Directory.GetFiles(batchExport,"*.aseprite").Length==4 &&
              Directory.GetFiles(batchExport,"*.sheet.png").Length==4 &&
              Directory.GetFiles(batchExport,"*.sheet.json").Length==4 &&
              Directory.GetFiles(batchExport,"frame-*.png",SearchOption.AllDirectories).Length==8 &&
              File.Exists(Path.Combine(batchExport,"directions.json")),
            "directional animation exports contain named native documents, sheets, metadata and every frame");
        Check(west.NormalSheet is not null && Find<WrapPanel>("LightingMapTools").Visibility==Visibility.Visible,
            "dynamic animation shows lighting map commands");
        Find<Slider>("TimelineInput").Value=1;
        var colorMap=Find<Image>("PreviewImage").Source;
        Find<ComboBox>("LightingMapInput").SelectedIndex=1;
        Check(!ReferenceEquals(colorMap,Find<Image>("PreviewImage").Source) && Find<Slider>("TimelineInput").Value==1,
            "normal map switching keeps the selected animation frame");
        Call("CompareLightingMaps",window,events);
        Check(Find<Border>("ComparisonPanel").IsVisible &&
            Find<ComboBox>("CompareModeInput").SelectedIndex==1 &&
            Find<CheckBox>("CompareNormalB").IsChecked==true &&
            Find<TextBox>("CompareFrameA").Text=="2"&&Find<TextBox>("CompareFrameB").Text=="2" &&
            !ReferenceEquals(Find<Image>("CompareImageA").Source,Find<Image>("CompareImageB").Source),
            "Compare maps opens the same frame side by side with separate color and normal textures");
        Call("StepComparison",new Button {Tag="B,-1"},events);
        Check(Find<TextBox>("CompareFrameA").Text=="1"&&Find<TextBox>("CompareFrameB").Text=="1",
            "stepping either map keeps both maps on the same pose");
        await Layout();Capture("lighting-maps");
        Call("CloseComparison");
        await Layout();Capture("camera-dark");
        Find<RadioButton>("LightTheme").IsChecked=true;await Layout();Capture("camera-light");
        Call("SavePreferences");
        foreach(CameraDirection direction in CameraSelection.Directions(CameraFamily.Top))
        {
            directionInput.SelectedItem=direction;
            Call("PreserveCurrentCore");
        }
        window.Close();
        window=new MainWindow();window.Show();await Layout();
        var restored=(AssetSettings)Call("ReadSettings")!;
        Check(restored.Camera==cameraSettings.Camera && restored.RenderAllDirections && restored.DynamicLighting,
            "camera family, perspective, elevation and batch mode persist across restart");
        if(emissionAsset is not null)
        {
            var sample=GeneratedAsset.Open(emissionAsset);
            typeof(MainWindow).GetField("output",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,sample);
            Call("LoadPlayback",sample,24);await Layout();
            string sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sample.Png)));
            var selector=Find<ComboBox>("LightingMapInput");
            var preview=Find<Image>("PreviewImage");
            var baseImage=preview.Source;
            selector.SelectedIndex=2;
            Check(!ReferenceEquals(baseImage,preview.Source),"emission mask is one selection away");
            selector.SelectedIndex=3;
            var warm=preview.Source;
            Find<TextBox>("EmissionTint").Text="#33AAFF";
            Check(!ReferenceEquals(warm,preview.Source),"fresh tint updates emission preview immediately");
            Capture("emission-preview");
            Find<CheckBox>("EmissionEnabled").IsChecked=false;
            Check(ReferenceEquals(baseImage,preview.Source),"glow off shows the cached base art");
            Check(sourceHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sample.Png))),
                "emission preview never modifies exported base pixels");
            selector.SelectedIndex=2;
            Call("CompareLightingMaps",window,events);
            Check(Find<CheckBox>("CompareEmissionB").IsChecked==true,"Compare maps opens the emission mask against base color");
            await Layout();Capture("emission-comparison");
            Call("CloseComparison");
        }
        window.Close();
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { checks, still = still.Directory, animation = animation.Directory, dpi = VisualTreeHelper.GetDpi(window).DpiScaleX }));
        Console.WriteLine($"{checks} modern UI checks passed: {root}");
    }
    private static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(parent); n++)
        {
            var child = VisualTreeHelper.GetChild(parent, n);
            if (child is Button button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}
