using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class PolishChecks
{
    private static int count;
    private static void Check(bool value,string label)
    {if(!value)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS: "+label);}
    public static void Run(string asset,string directory)
    {
        string root=Path.GetFullPath(Path.Combine(directory,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        ParserChecks();
        OnDesktop(async app=>await Ui(app,GeneratedAsset.Open(asset),root));
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,root,profile=Path.Combine(root,"profile")}));
        Console.WriteLine($"{count} UI polish checks passed: {root}");
    }
    public static void Restore(string profile)
    {
        Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.GetFullPath(profile));
        OnDesktop(app=>
        {
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(profile,"preferences.json")));
            var window=new MainWindow();window.Show();
            var directories=(DialogDirectories)typeof(MainWindow).GetField("directories",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
            foreach(var path in json.RootElement.GetProperty("DialogPaths").EnumerateObject())
                Check(directories.Initial(path.Name)==Path.GetFullPath(path.Value.GetString()!),"fresh process restores "+path.Name);
            Check(ThemeManager.Current==json.RootElement.GetProperty("Theme").GetString(),"fresh process restores theme with directories");
            window.Close();return Task.CompletedTask;
        });
        Console.WriteLine($"{count} fresh-process preference checks passed.");
    }
    private static void OnDesktop(Func<App,Task> verify)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Startup+=async(_,_)=>{try{await verify(app);}catch(Exception ex){failure=ex;}finally{app.Shutdown();}};
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw new Exception("UI polish verification",failure);
    }
    private static readonly string[] Six=["#171923","#303442","#505563","#747D86","#A5AFB0","#DFE4D8"];
    public static void ComparePixels(string baseline,string current)
    {
        using var old=JsonDocument.Parse(File.ReadAllText(baseline));using var next=JsonDocument.Parse(File.ReadAllText(current));
        foreach(string kind in new[]{"still","animation"})
        {
            string oldRoot=old.RootElement.GetProperty(kind).GetString()!,newRoot=next.RootElement.GetProperty(kind).GetString()!;
            var files=Directory.GetFiles(oldRoot,"*.png",SearchOption.AllDirectories);
            foreach(string file in files)
            {
                string relative=Path.GetRelativePath(oldRoot,file);var a=Raster.Read(file);var b=Raster.Read(Path.Combine(newRoot,relative));
                if(a.Width!=b.Width||a.Height!=b.Height||!a.Pixels.SequenceEqual(b.Pixels))throw new Exception("Pixel regression: "+relative);
            }
            Check(true,$"{kind}: all {files.Length} PNG pixel arrays match the pre-polish generation, including render passes and editable layers");
        }
    }
    private static void ParserChecks()
    {
        foreach(string separator in new[]{"; ","\n"," \t ",", "})
            Check(Palette.ParseList(string.Join(separator,Six)).Select(c=>c.Hex).SequenceEqual(Six),"six HEX colors retain order with "+JsonSerializer.Serialize(separator));
        Check(Palette.ParseList("255,0,1;rgb(2, 3, 4)").SequenceEqual(new[]{new Rgb(255,0,1),new Rgb(2,3,4)}),"RGB rows still parse");
        Check(Palette.ParseList("[[255,0,1],[2,3,4]]").Length==2&&Palette.ParseList("[1,2,3]").Length==1,"JSON RGB formats retained");
        Check(Palette.ParseList("[\"#112233\",\"#112233\",\"#445566\"]",out int repeated).Length==2&&repeated==1,"JSON duplicates counted without reordering");
        bool rejected=false;try{Palette.ParseList(string.Join(';',Enumerable.Range(0,4097).Select(n=>$"#{n:X6}")));}catch(InvalidDataException){rejected=true;}
        Check(rejected,"palette maximum is enforced before accepting a batch");
        try{Palette.SelectIndices(Palette.Default,"#171923;");throw new Exception("HEX was accepted as indices");}
        catch(ArgumentException ex){Check(ex.Message.Contains("Add Colors (HEX / RGB)")&&!ex.Message.Contains("outside 0"),"HEX in indices receives actionable color-input guidance");}
    }
    private static async Task Ui(App app,ConversionOutput asset,string root)
    {
        Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"profile"));
        var window=new MainWindow();window.Show();
        T Find<T>(string name)=>(T)window.FindName(name);
        object? Call(string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
        object? Field(string name)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window);
        void SetField(string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,value);
        async Task Layout(){window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
        void SetPalette(Rgb[] palette){SetField("sourcePalette",palette);SetField("selectedPalette",palette.ToArray());Call("ShowPalette");}
        void Add(string text){Find<TextBox>("DirectColors").Text=text;Call("AddColors",window,new RoutedEventArgs());}
        void DismissError()=>window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>app.Windows.OfType<Window>().Single(w=>w.Owner==window&&w.IsVisible).Close()));
        var version=typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Check(window.Title=="PixelForge Studio"&&Find<TextBlock>("VersionLabel").Text=="PixelForge Studio → v"+version,"clean title and automatic status-bar version");
        Check(!Find<Expander>("SourceIndicesSection").IsExpanded&&Find<TextBox>("Indices").Parent!=Find<TextBox>("DirectColors").Parent,"source indices start collapsed and separate from color entry");
        Find<Expander>("ManageColors").IsExpanded=true;
        foreach(string separator in new[]{"; ","\n"," \t "})
        {
            SetPalette([]);Find<TextBox>("DirectColors").Focus();Clipboard.SetText(string.Join(separator,Six));
            System.Windows.Input.ApplicationCommands.Paste.Execute(null,Find<TextBox>("DirectColors"));
            Call("AddColors",window,new RoutedEventArgs());
            Check(((Rgb[])Field("selectedPalette")!).Select(c=>c.Hex).SequenceEqual(Six),"actual WPF paste/Add accepts "+JsonSerializer.Serialize(separator));
        }
        Add("#171923; #010203; #010203");
        Check(((Rgb[])Field("selectedPalette")!).Length==7&&Find<TextBlock>("PaletteEditFeedback").Text.Contains("2 duplicate"),"existing and within-batch duplicates have useful feedback");
        foreach(string invalid in new[]{"#112233; INVALID", "[[256,0,0]]", "#AABBCC;#12"})
        {
            var selected=(Rgb[])Field("selectedPalette")!;var source=Field("sourcePalette");var name=Field("paletteName");
            DismissError();Add(invalid);
            Check(ReferenceEquals(source,Field("sourcePalette"))&&selected.SequenceEqual((Rgb[])Field("selectedPalette")!)&&Equals(name,Field("paletteName")),"invalid batch changes no palette state: "+invalid);
            Check(Find<TextBox>("DirectColors").Text==invalid&&Find<TextBlock>("PaletteEditFeedback").Text.StartsWith("No colors added"),"invalid input remains available for correction");
        }
        SetPalette(Enumerable.Range(0,4096).Select(n=>new Rgb((byte)(n>>8),(byte)n,0)).ToArray());
        var limit=Field("sourcePalette");DismissError();Add("#FFFFFF");
        Check(ReferenceEquals(limit,Field("sourcePalette")),"over-limit addition is atomic");
        SetPalette(Palette.Default.ToArray());Find<TextBox>("DirectColors").Clear();Find<TextBox>("Indices").Text="#171923;";
        DismissError();Call("ApplyIndices",window,new RoutedEventArgs());
        Check(Find<TextBox>("ErrorDetails").Text.Contains("Add Colors (HEX / RGB)"),"actual source-index error points to Add Colors");Find<TextBox>("Indices").Clear();
        var settings=AssetSettings.Load(Path.Combine(asset.Directory,"asset.settings.json"));Call("ApplySettings",settings);SetField("output",asset);Call("LoadPlayback",asset,settings.Fps);
        Find<TextBlock>("EmptyPreview").Visibility=Visibility.Collapsed;Find<StackPanel>("ExportPanel").IsEnabled=true;
        foreach(var section in Find<StackPanel>("SettingsPanel").Children.OfType<Expander>())section.IsExpanded=section.Name=="LightingSection";
        Find<NumericSlider>("ShadowInput").Value=.2;Find<NumericSlider>("LightIntensityInput").Value=5.123456;
        var workspace=((Grid)window.Content).Children.OfType<Grid>().Single();
        foreach(string theme in new[]{"Dark","Light"})foreach(double width in new[]{310d,336d,460d})
        {
            Find<RadioButton>(theme+"Theme").IsChecked=true;workspace.ColumnDefinitions[0].Width=new GridLength(width);await Layout();
            var cards=Find<ResponsivePanel>("LightCards");
            var bounds=cards.Children.OfType<FrameworkElement>().Select(c=>new Rect(c.TranslatePoint(new Point(),cards),c.RenderSize)).ToArray();
            Check(bounds.All(b=>b.Left>=-1&&b.Right<=cards.ActualWidth+1)&&!bounds.SelectMany((a,i)=>bounds.Skip(i+1).Select(b=>Rect.Intersect(a,b))).Any(r=>!r.IsEmpty&&r.Width>1&&r.Height>1),$"lighting cards reflow without overlap: {theme}/{width}");
            foreach(string name in new[]{"SurfaceShadowsLabel","KeyLightLabel"})
            {
                var label=Find<TextBlock>(name);var text=new FormattedText(label.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(label).PixelsPerDip){MaxTextWidth=Math.Max(1,label.ActualWidth)};
                Check(text.Height<=label.ActualHeight+2,$"complete {name} is readable: {theme}/{width}");
            }
        }
        Check(Find<TextBlock>("SurfaceShadowsLabel").Text.Contains("20")&&Find<TextBlock>("SurfaceShadowsLabel").Text.Contains('%'),"surface shadow percentage includes its full value");
        Check(Find<TextBlock>("KeyLightLabel").ToolTip.ToString()!.Contains("Light.energy")&&Find<NumericSlider>("LightIntensityInput").Value==5.123456,"key-light meaning is accurate and internal precision is untouched");
        Find<Expander>("CameraSection").IsExpanded=true;Find<Expander>("OutlinesSection").IsExpanded=true;Find<CheckBox>("OutlineInput").IsChecked=true;
        foreach(double width in new[]{310d,336d,460d})
        {
            workspace.ColumnDefinitions[0].Width=new GridLength(width);await Layout();
            Check(Find<ComboBox>("ViewInput").ActualWidth>=168&&Find<ComboBox>("OutlineTypeInput").ActualWidth>=168,$"camera and outline controls also reflow at sidebar {width}");
        }
        Find<Expander>("CameraSection").IsExpanded=false;Find<Expander>("OutlinesSection").IsExpanded=false;Find<CheckBox>("OutlineInput").IsChecked=settings.Outlines;
        foreach(double scale in new[]{1.25,1.5,2})
        {
            Find<StackPanel>("SettingsPanel").LayoutTransform=new ScaleTransform(scale,scale);await Layout();
            Check(Find<TextBlock>("KeyLightLabel").ActualHeight>0&&Find<NumericSlider>("ShadowInput").ActualWidth>70,$"settings remain usable under WPF layout scaling {scale}");
        }
        Find<StackPanel>("SettingsPanel").LayoutTransform=Transform.Identity;
        window.WindowState=WindowState.Maximized;await Layout();Check(Find<ResponsivePanel>("LightCards").ActualWidth>300,"maximized workspace retains expanded readable cards");window.WindowState=WindowState.Normal;
        Find<TextBox>("ErrorDetails").Text=string.Join('\n',Enumerable.Range(0,100).Select(n=>$"Long diagnostic {n}: exception details and context remain readable across the full workspace width."));
        Find<TextBlock>("OutputInfo").Text="Warning: sample warning remains visible in Diagnostics.";Find<TextBlock>("Status").Text="Render progress: validation completed.";
        await Layout();double previous=Find<Border>("PreviewBorder").ActualHeight;
        Find<ToggleButton>("DetailsToggle").IsChecked=true;await Layout();
        Check(Find<Border>("DiagnosticsPanel").ActualWidth>=window.ActualWidth*.9&&Find<TextBox>("ErrorDetails").ActualWidth>window.ActualWidth*.8,"diagnostics expands across the workspace");
        var scroll=Find<ScrollViewer>("DiagnosticsScroll");scroll.ScrollToEnd();await Layout();Check(scroll.VerticalOffset>0,"long diagnostics scroll");Call("CopyDiagnostics",window,new RoutedEventArgs());
        Check(Clipboard.GetText().Contains("Long diagnostic 99")&&Clipboard.GetText().Contains("sample warning")&&Clipboard.GetText().Contains("validation completed"),"Copy details includes full errors, warnings and progress");
        window.Width=1000;window.Height=740;await Layout();Check(Find<Button>("GenerateButton").IsVisible&&Find<TextBlock>("VersionLabel").TranslatePoint(new Point(),window).Y<window.ActualHeight,"expanded diagnostics preserves status/actions in a small window");
        window.Width=1440;window.Height=1000;Find<ToggleButton>("DetailsToggle").IsChecked=false;await Layout();Check(Math.Abs(Find<Border>("PreviewBorder").ActualHeight-previous)<2,"collapsing diagnostics restores preview space");
        foreach(string category in new[]{"models","settings","palettes","png","sheets","frames","assets","native"})
        {string folder=Path.Combine(root,"dialogs",category);Directory.CreateDirectory(folder);Call("RememberFile",category,Path.Combine(folder,"test.json"));}
        var dirs=(DialogDirectories)Field("directories")!;
        Check(dirs.Locations.Values.Distinct().Count()==8,"all eight dialog categories remain independent");
        var before=JsonSerializer.Serialize(dirs.Locations);var dialog=new OpenFileDialog();Call("Prepare",dialog,"png");Check(JsonSerializer.Serialize(dirs.Locations)==before,"preparing or abandoning a dialog does not change remembered paths");
        dirs.Locations["missing"]=Path.Combine(root,"does-not-exist");Check(Directory.Exists(dirs.Initial("missing",root))&&dirs.Locations["missing"].EndsWith("does-not-exist"),"missing directory falls back without overwriting its preference");dirs.Locations.Remove("missing");
        dirs.Locations["unusable"]=Path.Combine(root,"not-a-directory.txt");File.WriteAllText(dirs.Locations["unusable"],"fixture");Check(dirs.Initial("unusable",root)==root,"an unusable stored directory falls back safely");dirs.Locations.Remove("unusable");
        var relative=Path.GetRelativePath(Environment.CurrentDirectory,dirs.Locations["png"]);dirs.SelectedFolder("png",relative);Check(Path.IsPathFullyQualified(dirs.Locations["png"]),"relative selections normalize to absolute paths");
        foreach(string theme in new[]{"Dark","Light"})
        {
            Find<RadioButton>(theme+"Theme").IsChecked=true;Find<ToggleButton>("DetailsToggle").IsChecked=true;scroll.ScrollToTop();await Layout();Capture(window,Path.Combine(root,"polished-"+theme.ToLowerInvariant()+".png"));
        }
        Find<ToggleButton>("DetailsToggle").IsChecked=false;Find<Expander>("LightingSection").IsExpanded=false;Find<Expander>("PaletteSection").IsExpanded=true;Find<Expander>("ManageColors").IsExpanded=true;
        Find<TextBox>("DirectColors").Text=string.Join("; ",Six);Find<TextBlock>("PaletteEditFeedback").Text="";
        foreach(string theme in new[]{"Dark","Light"})
        {Find<RadioButton>(theme+"Theme").IsChecked=true;await Layout();Capture(window,Path.Combine(root,"palette-"+theme.ToLowerInvariant()+".png"));}
        Find<ToggleButton>("DetailsToggle").IsChecked=false;Call("SavePreferences");window.Close();
        window=new MainWindow();window.Show();
        var restored=(DialogDirectories)Field("directories")!;Check(restored.Locations.Count==8&&restored.Locations["png"]!=restored.Locations["sheets"],"application restart reloads independent directories from disk");window.Close();
    }
    private static void Capture(MainWindow window,string path)
    {
        window.UpdateLayout();var surface=(FrameworkElement)window.Content;var dpi=VisualTreeHelper.GetDpi(surface);
        var image=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(surface.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);
    }
}
