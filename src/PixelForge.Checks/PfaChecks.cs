using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelForge.Core;
using PixelForge.Core.IndexedLab;
using PixelForge.Desktop;

internal static class PfaChecks
{
    public static void Run(string phase,string root,string source)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"profile"));
        string assets=Path.Combine(root,"Assets");Directory.CreateDirectory(Path.Combine(assets,"Palettes"));
        string? webpTools=Environment.GetEnvironmentVariable("PIXELFORGE_WEBP_TEST");
        string name=phase=="first"?"Mine":"PowerStrip";
        if(phase=="first")
        {
            void PaletteFile(string path,IEnumerable<Rgb> colors)=>File.WriteAllLines(path,
                new[]{"GIMP Palette","Name: acceptance example"}.Concat(colors.Select(c=>$"{c.R} {c.G} {c.B}")));
            PaletteFile(Path.Combine(assets,"Base32.gpl"),
                new[]{new Rgb(0,0,0)}.Concat(Palette.Default).Concat(Enumerable.Range(1,7).Select(n=>new Rgb((byte)n,0,0))));
            File.Copy(Path.Combine(assets,"Base32.gpl"),Path.Combine(assets,"Alternate.gpl"));
            PaletteFile(Path.Combine(assets,"Palettes","Emission256.gpl"),Enumerable.Range(0,256).Select(n=>new Rgb((byte)n,(byte)n,(byte)n)));
        }
        Exception? failure=null;
        var thread=new Thread(()=>{
            var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            app.Startup+=async (_,_)=>{
                try
                {
                    var main=new MainWindow();main.Show();
                    var rendered=GeneratedAsset.Open(source);
                    var create=typeof(MainWindow).GetMethod("CreatePfaExporter",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    var exporter=(Window)create.Invoke(main,[rendered,Array.Empty<ConversionOutput>()])!;exporter.Show();
                    object? Field(object obj,string key)=>obj.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj);
                    void Text(string key,string value)
                    {
                        var obj=Field(exporter,key);
                        if(obj is TextBox textbox)textbox.Text=value;else ((ComboBox)obj!).Text=value;
                    }
                    string ReadText(string key)=>Field(exporter,key) is TextBox textbox?textbox.Text:((ComboBox)Field(exporter,key)!).Text;
                    var advanced=((ScrollViewer)exporter.Content).Content as StackPanel;
                    advanced!.Children.OfType<Expander>().Single().IsExpanded=true;
                    void VerifyPicker(string expected)
                    {
                        exporter.UpdateLayout();
                        var combo=(ComboBox)Field(exporter,"color")!;
                        if(combo.Template.FindName("PART_EditableTextBox",combo) is not TextBox text||
                           !text.IsVisible||text.Text!=expected)
                            throw new InvalidDataException("Palette selection is not rendered in the closed ComboBox.");
                    }
                    if(phase=="first")
                    {
                        ((CheckBox)Field(exporter,"pfaFormat")!).IsChecked=true;
                        ((CheckBox)Field(exporter,"webpFormat")!).IsChecked=false;
                        Text("root",assets);Text("color","Base32.gpl");Text("emission","Palettes/Emission256.gpl");
                        if(webpTools is not null)
                        {
                            ((CheckBox)Field(exporter,"pngFormat")!).IsChecked=true;
                            ((CheckBox)Field(exporter,"webpFormat")!).IsChecked=true;
                            ((CheckBox)Field(exporter,"reports")!).IsChecked=true;
                            ((CheckBox)Field(exporter,"gameMetadata")!).IsChecked=true;
                            Text("cwebp",Path.Combine(webpTools,"cwebp.exe"));Text("dwebp",Path.Combine(webpTools,"dwebp.exe"));
                        }
                        VerifyPicker("Base32.gpl");
                        var saveMethod=exporter.GetType().GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!;
                        saveMethod.Invoke(exporter,[]);
                        Text("profileName","Alternate");Text("color","Alternate.gpl");saveMethod.Invoke(exporter,[]);
                        var picker=(ComboBox)Field(exporter,"profiles")!;
                        picker.SelectedItem="Hollow Core Default";VerifyPicker("Base32.gpl");
                        ThemeManager.Apply("Light");
                        picker.SelectedItem="Alternate";VerifyPicker("Alternate.gpl");
                        ThemeManager.Apply("Dark");
                        picker.SelectedItem="Hollow Core Default";VerifyPicker("Base32.gpl");
                        Console.WriteLine("PASS: closed palette display responds immediately to profile switches in Light and Dark.");
                    }
                    else if(ReadText("root")!=assets||ReadText("color")!="Base32.gpl"||
                            ReadText("emission")!="Palettes/Emission256.gpl"||
                            new[]{"objects","normals","emissions","all"}.Any(k=>((CheckBox)Field(exporter,k)!).IsChecked!=true)||
                            ((ComboBox)Field(exporter,"compression")!).SelectedItem is not PfaCompression.Auto)
                        throw new InvalidDataException("Saved profile did not restore on a new application process.");
                    VerifyPicker("Base32.gpl");
                    if(webpTools is not null&&new[]{"pfaFormat","pngFormat","webpFormat"}.Any(k=>((CheckBox)Field(exporter,k)!).IsChecked!=true))
                        throw new InvalidDataException("Export formats did not persist.");
                    Text("asset",name);
                    await Task.Delay(120);
                    Capture(exporter,Path.Combine(root,phase+"-exporter.png"));
                    exporter.GetType().GetMethod("Export",BindingFlags.Instance|BindingFlags.NonPublic)!
                        .Invoke(exporter,[new Button(),new RoutedEventArgs()]);
                    var deadline=DateTime.UtcNow.AddMinutes(2);
                    while(Field(exporter,"operation") is not null&&DateTime.UtcNow<deadline)await Task.Delay(50);
                    var status=(TextBlock)Field(exporter,"status")!;
                    if(!status.Text.StartsWith("PASS"))throw new InvalidDataException(status.Text);
                    string folder=Path.Combine(assets,"Machines",name);
                    var paths=Directory.GetFiles(folder,"*.pfa",SearchOption.AllDirectories).Order().ToArray();
                    if(webpTools is not null)
                    {
                        var webps=Directory.GetFiles(folder,"*.webp",SearchOption.AllDirectories);
                        var pngs=Directory.GetFiles(folder,"*.png",SearchOption.AllDirectories);
                        if(webps.Length!=3||pngs.Length!=3||File.Exists(Path.Combine(folder,"compression-report.txt")))
                            throw new InvalidDataException("Missing multi-format export files or report.");
                        using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(folder,"*.asset.json",SearchOption.AllDirectories).Single()));
                        var timing=manifest.RootElement.GetProperty("directions")[0].GetProperty("frames").EnumerateArray().Select(f=>f.GetProperty("durationMs").GetInt32());
                        if(!timing.SequenceEqual(rendered.Frames.Select(f=>f.DurationMs)))throw new InvalidDataException("Atlas manifest timing differs.");
                        Console.WriteLine("PASS: exported exact verified WebP and PNG atlases with authored animation timing.");
                    }
                    if(paths.Length!=3)throw new InvalidDataException("Expected three independent layers.");
                    foreach(var path in paths)
                    {
                        var decoded=Pfa.Read(path);
                        for(int n=0;n<decoded.Frames.Length;n++)
                        {
                            var original=rendered.Frames[n];
                            string png=decoded.Info.Layer switch {PfaLayer.Base=>original.Png,PfaLayer.Normal=>original.Normal!,_=>original.Emission!};
                            if(IndexedBenchmark.Verify(IndexedBenchmark.Load(png),decoded.Frames[n])!="PASS"||
                               decoded.Info.Durations[n]!=original.DurationMs)throw new InvalidDataException("Wrong current render pixels or timing.");
                        }
                    }
                    if(phase=="second")
                    {
                        var profile=new AssetExportProfile(Root:assets,BasePalette:"Base32.gpl",EmissionPalette:"Palettes/Emission256.gpl");
                        var repeat=PfaExport.Save(profile with{NormalPng=rendered.NormalSheet!},name,[rendered]);
                        if(repeat.Directory!=folder+"-v2"||paths.Any(path=>!File.Exists(path)))
                            throw new InvalidDataException("Safe versioned re-export failed.");
                        using var canceled=new CancellationTokenSource();canceled.Cancel();
                        Reject(()=>PfaExport.Save(profile,"Canceled",[rendered],canceled.Token));
                        Reject(()=>PfaExport.Save(profile with{NormalPng=Path.Combine(root,"missing.png")},"Missing",[rendered]));
                        if(Directory.Exists(Path.Combine(assets,"Machines","Missing"))||
                           Directory.EnumerateDirectories(Path.Combine(assets,"Machines"),".pfa-stage-*").Any())
                            throw new InvalidDataException("Incomplete export leaked into the asset library.");
                        var original=paths.Single(path=>Pfa.Read(path).Info.Layer==PfaLayer.Base);
                        string moved=Path.Combine(root,"Relocated");
                        var movedDir=Path.Combine(moved,"Machines","PowerStrip","South");Directory.CreateDirectory(movedDir);
                        File.Copy(Path.Combine(assets,".pixelforge-assets.json"),Path.Combine(moved,".pixelforge-assets.json"));
                        var relocated=Path.Combine(movedDir,Path.GetFileName(original));File.Copy(original,relocated);
                        Reject(()=>Pfa.Read(relocated));
                        string movedPalette=Path.Combine(moved,"Base32.gpl");
                        File.Copy(Path.Combine(assets,"Base32.gpl"),movedPalette);
                        if(Pfa.Read(relocated).Storage is not ("Index5" or "Index8"))throw new InvalidDataException("The real base did not use packed five-bit indexing.");
                        string[] gpl=File.ReadAllLines(movedPalette);gpl[3]="2 3 4";File.WriteAllLines(movedPalette,gpl);
                        Reject(()=>Pfa.Read(relocated));
                        var bytes=File.ReadAllBytes(original);bytes[4]=99;
                        string corrupt=Path.Combine(root,"unsupported-version.pfa");File.WriteAllBytes(corrupt,bytes);
                        Reject(()=>Pfa.Read(corrupt,assets));
                        string raw=Path.Combine(root,"raw-base.pfa");
                        var decoded=Pfa.Read(original);
                        var verified=Pfa.Write(raw,assets,decoded.Info,decoded.Frames,PfaCompression.Raw);
                        if(verified.Codec!="Raw v1"||verified.Storage!="Index5")throw new InvalidDataException("Raw five-bit round trip failed.");
                        Console.WriteLine("PASS: ordered GPL hash, relocated root, malformed header, raw five-bit storage, explicit external atlas, versioned re-export and cancellation.");

                        static void Reject(Action action)
                        {
                            try{action();}catch(Exception e) when(e is IOException or InvalidDataException or OperationCanceledException){return;}
                            throw new InvalidDataException("Expected PFA failure.");
                        }
                    }
                    var show=typeof(MainWindow).GetMethod("ShowPfa",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    if(phase=="first"&&Environment.GetEnvironmentVariable("PIXELFORGE_LEGACY_PFA") is {} oldFile)
                    {
                        show.Invoke(main,[oldFile]);
                        var oldViewer=main.OwnedWindows.Cast<Window>().Single(w=>w.Title.StartsWith("Asset Viewer"));
                        await WaitViewer(oldViewer);oldViewer.Close();
                        Console.WriteLine("PASS: opened an existing pre-optimization PFA in the WPF viewer.");
                    }
                    show.Invoke(main,[paths[0]]);
                    var viewer=main.OwnedWindows.Cast<Window>().Single(w=>w.Title.StartsWith("Asset Viewer"));
                    await WaitViewer(viewer);
                    viewer.Close();
                    show.Invoke(main,[folder]);
                    viewer=main.OwnedWindows.Cast<Window>().Single(w=>w.Title.StartsWith("Asset Viewer"));
                    var list=(ListBox)Field(viewer,"files")!;
                    for(int i=0;i<list.Items.Count;i++){list.SelectedIndex=i;await WaitViewer(viewer);}
                    var slider=(Slider)Field(viewer,"frame")!;
                    slider.Value=slider.Maximum;
                    Capture(viewer,Path.Combine(root,phase+"-viewer.png"));
                    var selection=(PfaImage)Field(viewer,"selected")!;
                    selection.Frames[(int)slider.Value].Save(Path.Combine(root,phase+"-decoded.png"));
                    viewer.Close();exporter.Close();main.Close();
                    Console.WriteLine($"PASS: {phase} process exported {name}, verified all three layers and opened a single PFA and its folder.");
                    if(phase!="first")Console.WriteLine("PASS: no profile, palette, or layer controls were changed in the second process.");

                    async Task WaitViewer(Window window)
                    {
                        var until=DateTime.UtcNow.AddSeconds(10);
                        while(Field(window,"selected") is null&&DateTime.UtcNow<until)await Task.Delay(40);
                        if(Field(window,"selected") is null)throw new InvalidDataException(((TextBlock)Field(window,"details")!).Text);
                    }
                }
                catch(Exception e){failure=e;}
                finally{app.Shutdown();}
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw new Exception("PFA acceptance",failure);
    }
    public static async Task Optimize(string root,string reference,string[] directional)
    {
        var original=GeneratedAsset.Open(reference);
        var settings=AssetSettings.Load(Path.Combine(original.Directory,"asset.settings.json")) with {StartFrame=1,EndFrame=12,FrameStep=1,Animation=true};
        var current=original.Frames.Length==12?original:await new ConversionPipeline(Toolchain.Discover()).Generate(settings,Path.Combine(root,"renders"));
        if(current!=original)GeneratedAsset.Save(current);
        if(current.Frames.Length!=12)throw new InvalidDataException("Expected all twelve Mine10 frames.");
        var defaultTools=Environment.GetEnvironmentVariable("PIXELFORGE_WEBP_TEST")!;
        var profile=new AssetExportProfile(Root:Path.Combine(Path.GetFullPath(root),"Assets"),BasePalette:"Base32.gpl",
            ExportWebp:true,ExportPng:true,DetailedReport:true,Cwebp:Path.Combine(defaultTools,"cwebp.exe"),Dwebp:Path.Combine(defaultTools,"dwebp.exe"));
        var mine=PfaExport.Save(profile,"Mine10Full",[current]);
        Console.WriteLine(mine.Report);
        var all=PfaExport.Save(profile,"CardinalWebp",directional.Select(GeneratedAsset.Open).ToArray());
        if(all.Directions!=4||Directory.GetFiles(all.Directory,"*.webp",SearchOption.AllDirectories).Length!=12)
            throw new InvalidDataException("Wrong directional WebP file set.");
        var webpOnly=PfaExport.Save(profile with {ExportPfa=false,ExportPng=false,DetailedReport=false,Normal=false,Emission=false},"WebpOnly",[current]);
        if(Directory.GetFiles(webpOnly.Directory,"*.pfa",SearchOption.AllDirectories).Length!=0)
            throw new InvalidDataException("WebP-only export wrote a PFA.");
        if(Environment.GetEnvironmentVariable("PIXELFORGE_STATIC_TEST") is {} stillPath)
        {
            var still=GeneratedAsset.Open(stillPath);
            var image=IndexedBenchmark.Load(still.Frames[0].Png);
            int transparent=Enumerable.Range(0,image.Width*image.Height).First(i=>image.Pixels[i*4+3]==0)*4;
            image.Pixels[transparent]=7;image.Pixels[transparent+1]=31;image.Pixels[transparent+2]=133;
            string external=Path.Combine(root,"hidden-rgb.png");image.Save(external);
            if(IndexedBenchmark.Verify(image,IndexedBenchmark.Load(external))!="PASS")throw new InvalidDataException("Static PNG did not preserve hidden RGB.");
            var staticResult=PfaExport.Save(profile with {ExportPfa=false,ExportPng=false,DetailedReport=false,Normal=false,Emission=false,
                BasePng=Path.GetFullPath(external)},"StaticExact",[still]);
            if(!Directory.GetFiles(staticResult.Directory,"*Base.webp",SearchOption.AllDirectories).Any())
                throw new InvalidDataException("Static WebP did not export.");
            Console.WriteLine("PASS: static WebP preserved nonzero RGB behind alpha zero.");
        }
        Console.WriteLine("PASS: twelve-frame real Mine10; all four directional maps and WebP-only export.");
    }

    public static void Directions(string root,string[] sources)
    {
        var profile=new AssetExportProfile(Root:Path.GetFullPath(root),BasePalette:"Base32.gpl",EmissionPalette:"Palettes/Emission256.gpl");
        var result=PfaExport.Save(profile,"DirectionalMachine",sources.Select(GeneratedAsset.Open).ToArray());
        var files=Directory.GetFiles(result.Directory,"*.pfa",SearchOption.AllDirectories).Select(path=>Pfa.Read(path)).ToArray();
        if(result.Directions!=4||result.Layers!=12||
            !files.Select(file=>file.Info.Direction).ToHashSet().SetEquals(new[]{"North","East","South","West"}))
            throw new InvalidDataException("Incorrect multi-direction export layout.");
        Console.WriteLine("PASS: four generated directions exported as twelve independently verified PFA files.");
    }
    private static void Capture(Window window,string path)
    {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
}
