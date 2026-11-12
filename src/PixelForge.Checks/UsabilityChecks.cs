using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class UsabilityChecks
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    public static async Task Run(string mine,string animation,string outputRoot)
    {
        var root=Path.GetFullPath(Path.Combine(outputRoot,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var profile=Path.Combine(root,"profile");var historyPath=Path.Combine(profile,"render-history.json");
        var history=new RenderHistory(historyPath);var pipeline=new ConversionPipeline(Toolchain.Discover());var outputs=new List<ConversionOutput>();
        var settings=new AssetSettings{Model=Path.GetFullPath(mine),Width=64,Height=64,Outlines=true,OutlineType=OutlineStyle.VisibleComponents};
        foreach(var direction in new[]{CameraView.IsometricSE,CameraView.IsometricSW,CameraView.IsometricNE,CameraView.IsometricNW})
        {
            Console.WriteLine("RENDER "+direction);var output=await pipeline.Generate(settings with{View=direction},root);GeneratedAsset.Save(output);outputs.Add(output);history.AddCompleted(output);
            Check(history.Recent.Count==outputs.Count,"successful render enters recent list "+outputs.Count);
        }
        Check(history.Recent[0].Directory==outputs[3].Directory&&history.Recent[3].Directory==outputs[0].Directory,"history orders newest and oldest of four renders");
        history=new RenderHistory(historyPath);Check(history.Recent.Count==4,"four recent renders persist after history restart");
        var oldest=history.Recent[3];history.ToggleFavorite(oldest);
        var animated=await pipeline.Generate(settings with{Model=Path.GetFullPath(animation),Width=48,Height=64,Animation=true,StartFrame=1,EndFrame=10,FrameStep=3,OutlineType=OutlineStyle.Selective,EdgeHighlights=true,GroundShadows=true},root);GeneratedAsset.Save(animated);outputs.Add(animated);history.AddCompleted(animated);
        Check(history.Recent.Count==4&&history.Recent.All(e=>e.Directory!=oldest.Directory),"fifth successful render rotates only the recent list");
        Check(history.Favorites.Count==1&&history.Entries.Count()==5&&Directory.Exists(oldest.Directory),"favorite survives rotation and old output is never deleted");
        history=new RenderHistory(historyPath);Check(history.IsFavorite(oldest)&&history.Entries.Count()==5,"favorites persist after restart");
        string validation=Path.Combine(animated.Directory,"validation.json"),temporarilyMoved=validation+".test-moved";
        File.Move(validation,temporarilyMoved);
        try
        {
            bool rejected=false;try{history.AddCompleted(animated);}catch(IOException){rejected=true;}
            Check(rejected&&history.Recent[0].Directory==animated.Directory,"incomplete/unvalidated output cannot enter or rotate history");
        }
        finally{File.Move(temporarilyMoved,validation);}
        string png=outputs[0].Png;File.Move(png,png+".test-moved");
        try{Check(history.Entries.Single(e=>e.Directory==outputs[0].Directory).UnavailableReason is not null,"missing referenced PNG is gracefully marked unavailable");}
        finally{File.Move(png+".test-moved",png);}
        Check(history.Entries.All(e=>e.UnavailableReason is null),"restoring files restores history availability");
        string beforeFailure=File.ReadAllText(historyPath);
        try{var failed=await pipeline.Generate(settings with{Model=Path.Combine(root,"missing.blend")},root);history.AddCompleted(failed);throw new Exception("Expected missing-model failure");}
        catch(FileNotFoundException){Check(File.ReadAllText(historyPath)==beforeFailure,"failed rendering never changes persistent history");}
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{var canceled=await pipeline.Generate(settings,root,cancellation:cancellation.Token);history.AddCompleted(canceled);throw new Exception("Expected cancellation");}
            catch(OperationCanceledException){Check(File.ReadAllText(historyPath)==beforeFailure,"canceled rendering never changes persistent history");}
        }
        var otherAnimation=await pipeline.Generate(settings with{Model=Path.GetFullPath(animation),Width=48,Height=64,Animation=true,StartFrame=1,EndFrame=7,FrameStep=2,Fps=6},root);
        GeneratedAsset.Save(otherAnimation);outputs.Add(otherAnimation);history.AddCompleted(otherAnimation);
        var layout=ComparisonLayout.Create(24,42,128,96,(12,38),(70,80));
        Check(layout.A.X+12==layout.B.X+70&&layout.A.Y+38==layout.B.Y+80,"different dimensions share exact ground-anchor placement");
        Check(layout.A.Width==24&&layout.A.Height==42&&layout.B.Width==128&&layout.B.Height==96,"alignment never stretches canvas dimensions");
        var centered=ComparisonLayout.Create(24,42,64,64,null,(30,60));
        Check(!centered.GroundAligned&&centered.A.X+12==centered.B.X+32&&centered.A.Y+21==centered.B.Y+32,"missing anchor uses predictable centered alignment for both renders");
        Check(ComparisonLayout.FrameAtTime([new(1,"a",100),new(5,"b",400),new(9,"c",50)],250)==1,"time matching uses durations rather than equal frame indices");
        Check(ComparisonLayout.FrameAtTime([new(1,"a",100),new(2,"b",100)],10000)==1,"unequal clip time clamps to final frame");
        var hashes=outputs.SelectMany(o=>Directory.GetFiles(o.Directory,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var oldProfile=Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE");var oldBlender=Environment.GetEnvironmentVariable("PIXELFORGE_BLENDER");var oldAseprite=Environment.GetEnvironmentVariable("PIXELFORGE_ASEPRITE");
        Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",profile);Environment.SetEnvironmentVariable("PIXELFORGE_BLENDER",Path.Combine(root,"Blender-must-not-run.exe"));Environment.SetEnvironmentVariable("PIXELFORGE_ASEPRITE",Path.Combine(root,"Aseprite-must-not-run.exe"));
        try{Sta(()=>TestControls(profile,outputs));}
        finally{Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",oldProfile);Environment.SetEnvironmentVariable("PIXELFORGE_BLENDER",oldBlender);Environment.SetEnvironmentVariable("PIXELFORGE_ASEPRITE",oldAseprite);}
        Check(hashes.All(p=>p.Value==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))),"all rendering/native/PNG/metadata files unchanged by comparison, presets and input-mode editing");
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,profile,outputs,artworkUnchanged=true,comparisonToolsUnavailable=true},AssetSettings.JsonOptions));
        Console.WriteLine($"{count} usability checks passed. Results: {root}");
    }
    private static void TestControls(string profile,List<ConversionOutput> outputs)
    {
        var app=new PixelForge.Desktop.App();app.InitializeComponent();
        var numeric=new NumericSlider{Minimum=-360,Maximum=360,Value=65};
        numeric.Slider.Value=82;numeric.NumericMode=true;Check(numeric.Value==82&&numeric.Editor.Text=="82","slider and numeric editor share the same value");
        numeric.Editor.Text="-135.25";Check(numeric.Commit()&&numeric.Value==-135.25,"negative decimal direction commits exactly");
        numeric.NumericMode=false;Check(numeric.Slider.Value==-135.25,"switching back restores exact slider position");
        foreach(string invalid in new[]{"","abc","NaN","Infinity","361","-361"})
        {numeric.NumericMode=true;numeric.Editor.Text=invalid;Check(!numeric.Commit()&&numeric.Value==-135.25&&!string.IsNullOrEmpty(numeric.ValidationMessage),"invalid numeric edit preserves last valid value: "+invalid);numeric.CancelEdit();}
        foreach(double boundary in new[]{-360d,360d}){numeric.Editor.Text=boundary.ToString(System.Globalization.CultureInfo.InvariantCulture);Check(numeric.Commit()&&numeric.Value==boundary,"numeric range boundary "+boundary);}
        var percent=new NumericSlider{Minimum=0,Maximum=1,DisplayScale=100,Unit="%",Value=.65,NumericMode=true};Check(percent.Editor.Text=="65","percentage editor displays user-facing units");
        percent.Editor.Text="82.5";Check(percent.Commit()&&percent.Value==.825,"percentage numeric input preserves original normalized parameter");
        double remembered=percent.Value;for(int i=0;i<10;i++)percent.NumericMode=!percent.NumericMode;Check(percent.Value==remembered,"repeated mode switches never reset the value");
        percent.Editor.Text="99";percent.CancelEdit();Check(percent.Value==remembered&&percent.Editor.Text=="82.5","Escape cancellation restores last valid editor text and value");
        var window=new MainWindow();T Find<T>(string name)=>(T)window.FindName(name);
        Check(Find<TextBox>("WidthInput").Text=="128"&&Find<TextBox>("HeightInput").Text=="128","new UI defaults to 128×128");
        foreach(Button button in Find<WrapPanel>("ResolutionPresets").Children)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));string size=button.Tag.ToString()!;
            Check(button.Content.ToString()==size&&Find<TextBox>("WidthInput").Text==size&&Find<TextBox>("HeightInput").Text==size,"preset "+size+" sets both dimensions with number-only caption");
        }
        Find<TextBox>("WidthInput").Text="24";Find<TextBox>("HeightInput").Text="42";
        var read=typeof(MainWindow).GetMethod("ReadSettings",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var custom=(AssetSettings)read.Invoke(window,null)!;Check(custom.Width==24&&custom.Height==42,"arbitrary nonsquare dimensions remain valid");
        Check(Find<WrapPanel>("ResolutionPresets").Children.Cast<Button>().All(b=>b.FontWeight!=FontWeights.Bold),"custom dimensions leave all presets unselected");
        Find<TextBox>("WidthInput").Text="128";Find<TextBox>("HeightInput").Text="128";
        Check(Find<WrapPanel>("ResolutionPresets").Children.Cast<Button>().Single(b=>b.Tag.ToString()=="128").FontWeight==FontWeights.Bold,"manually entering a preset highlights it");
        Find<Button>("CompareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Find<Border>("ComparisonPanel").Visibility==Visibility.Visible,"comparison opens inside existing preview area");
        Check(Find<ComboBox>("RenderAInput").Items.Count==5,"recent renders and retained favorite appear in comparison selectors");
        var selectA=Find<ComboBox>("RenderAInput");var selectB=Find<ComboBox>("RenderBInput");
        selectA.SelectedItem=selectA.Items.Cast<RenderEntry>().Single(e=>e.Directory==outputs[4].Directory);
        selectB.SelectedItem=selectB.Items.Cast<RenderEntry>().Single(e=>e.Directory==outputs[5].Directory);
        Find<TextBox>("CompareFrameA").Text="3";Find<TextBox>("CompareFrameB").Text="2";
        var frameRead=typeof(MainWindow).GetMethod("ReadComparisonFrame",BindingFlags.NonPublic|BindingFlags.Instance)!;
        frameRead.Invoke(window,[Find<TextBox>("CompareFrameA")]);Find<TextBox>("CompareFrameB").Text="2";frameRead.Invoke(window,[Find<TextBox>("CompareFrameB")]);
        Check(Find<TextBlock>("ComparisonInfo").Text.Contains("source 7")&&Find<TextBlock>("ComparisonInfo").Text.Contains("source 3"),"two animations keep independently selected source frames");
        typeof(MainWindow).GetMethod("MatchSourceFrame",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[null,new RoutedEventArgs()]);
        Check(Find<TextBox>("CompareFrameB").Text=="4","source matching finds corresponding Blender frame despite different sampling");
        typeof(MainWindow).GetMethod("MatchTimeFrame",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[null,new RoutedEventArgs()]);
        Check(Find<TextBox>("CompareFrameB").Text=="2","time matching respects unequal playback durations");
        selectB.SelectedItem=selectB.Items.Cast<RenderEntry>().Single(e=>e.Directory==outputs[0].Directory);
        var imageA=Find<Image>("CompareImageA");var imageB=Find<Image>("CompareImageB");var originalA=imageA.Source;var originalB=imageB.Source;
        double x=Canvas.GetLeft(imageA),y=Canvas.GetTop(imageA);var divider=Find<NumericSlider>("DividerInput");
        foreach(double split in new[]{0d,50d,100d})
        {
            divider.Value=split;var clip=(RectangleGeometry)Find<Canvas>("CompareHostA").Clip;
            Check(Math.Abs(clip.Rect.Width-Find<Canvas>("CompareHostA").Width*split/100)<.001&&Canvas.GetLeft(imageA)==x&&Canvas.GetTop(imageA)==y,"divider "+split+"% changes clipping without shifting artwork");
        }
        divider.NumericMode=true;divider.Editor.Text="37.5";Check(divider.Commit()&&divider.Value==37.5,"comparison divider accepts exact numeric position");
        Check(ReferenceEquals(originalA,imageA.Source)&&ReferenceEquals(originalB,imageB.Source),"divider never decodes or converts frames again");
        Find<ComboBox>("CompareModeInput").SelectedIndex=1;Check(Find<Canvas>("CompareHostA").Clip is null&&Find<Canvas>("CompareHostB").Clip is null&&Canvas.GetLeft(Find<Canvas>("CompareHostB"))>0,"side-by-side shares zoom and presents two unclipped canvases");
        Check(RenderOptions.GetBitmapScalingMode(imageA)==BitmapScalingMode.NearestNeighbor&&RenderOptions.GetBitmapScalingMode(imageB)==BitmapScalingMode.NearestNeighbor,"comparison keeps nearest-neighbor scaling");
        var azimuth=Find<NumericSlider>("LightAzimuthInput");azimuth.NumericMode=true;azimuth.Editor.Text="-82.25";Check(azimuth.Commit(),"pending lighting edit commits without generating");
        foreach(string name in new[]{"LightElevationInput","LightIntensityInput","AmbientInput","ShadowInput","GroundStrengthInput","GroundSoftnessInput","EdgeStrengthInput"})
        {
            var control=Find<NumericSlider>(name);double saved=control.Value;control.NumericMode=true;
            control.Editor.Text=(control.Minimum*control.DisplayScale).ToString(System.Globalization.CultureInfo.InvariantCulture);bool minimum=control.Commit();
            control.Editor.Text=(control.Maximum*control.DisplayScale).ToString(System.Globalization.CultureInfo.InvariantCulture);bool maximum=control.Commit();
            Check(minimum&&maximum,name+" accepts its original range endpoints in numeric units");control.Value=saved;
        }
        Find<TextBox>("WidthInput").Text="24";Find<TextBox>("HeightInput").Text="42";window.Close();
        var reopened=new MainWindow();
        Check(((NumericSlider)reopened.FindName("LightAzimuthInput")).NumericMode&&((NumericSlider)reopened.FindName("DividerInput")).NumericMode,"per-control input modes persist in UI preferences after window restart");
        Check(((NumericSlider)reopened.FindName("LightAzimuthInput")).Value==-82.25&&((TextBox)reopened.FindName("WidthInput")).Text=="24"&&((TextBox)reopened.FindName("HeightInput")).Text=="42","restart preserves pending parameters and custom dimensions");
        using var prefs=JsonDocument.Parse(File.ReadAllText(Path.Combine(profile,"preferences.json")));
        Check(prefs.RootElement.GetProperty("NumericModes").TryGetProperty("DividerInput",out _)&&!prefs.RootElement.GetProperty("LastSettings").TryGetProperty("NumericModes",out _),"input presentation preferences stay separate from artwork settings");
        reopened.Close();app.Shutdown();
    }
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw new Exception("WPF control checks failed",error);
    }
}
