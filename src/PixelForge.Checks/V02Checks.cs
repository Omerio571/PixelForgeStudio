using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using PixelForge.Core;

internal static class V02Checks
{
    private static int passed;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
    private static void Reject(Action action,string name){try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or FormatException or IOException){Check(true,name);return;}throw new Exception("FAIL expected rejection: "+name);}
    public static async Task Run(string model,string root)
    {
        root=Path.GetFullPath(root);model=Path.GetFullPath(model);Directory.CreateDirectory(root);
        Check(Palette.ParseList("#242735\n#373B4B, #707784").Length==3,"multiline and comma HEX list");
        Check(Palette.ParseList("255, 170, 0\n112,119,132").SequenceEqual(new[]{new Rgb(255,170,0),new Rgb(112,119,132)}),"multiline RGB list");
        Check(Palette.ParseList("[[255,170,0],[112,119,132]]").Length==2,"JSON RGB list");
        Check(Palette.ParseList("[\"#242735\",\"#242735\"]").Length==1,"JSON HEX and duplicate removal");
        Reject(()=>Palette.ParseList("[[256,0,0]]"),"invalid RGB list");
        Reject(()=>(new AssetSettings{LightIntensity=double.NaN}).Validate(),"invalid lighting");
        Reject(()=>(new AssetSettings{Outlines=true,OutlineColor="#FFFFFF"}).Validate(),"unapproved outline color");
        Reject(()=>(new AssetSettings{Outlines=true,Colors=["#FFFFFF"],OutlineColor="#FFFFFF"}).Validate(),"palette without dark outline color");
        var paletteFile=Path.Combine(root,"palette-test.png");
        var swatches=new Raster(3,2,new byte[]{0,0,255,255,0,255,0,255,0,0,255,255,255,0,0,0,255,0,0,255,0,255,255,128});swatches.Save(paletteFile);
        Check(Palette.LoadPng(paletteFile).SequenceEqual(new[]{new Rgb(255,0,0),new Rgb(0,255,0),new Rgb(0,0,255)}),"PNG row-major unique opaque colors, all rows, transparent/partial ignored");
        var gpl=Path.Combine(root,"test.gpl");File.WriteAllText(gpl,"GIMP Palette\n255 0 0\n0 255 0\n0 0 255\n");
        Check(Palette.Load(gpl).SequenceEqual(Palette.LoadPng(paletteFile)),"GPL and PNG routes agree");
        var appearance=new AssetSettings{Model=model,Width=96,Height=96,LightAzimuth=210,LightElevation=25,LightIntensity=1.3,AmbientIntensity=.2,ShadowIntensity=.4,CastShadows=false,Outlines=true,OutlineType=OutlineStyle.VisibleComponents,OutlineThickness=2};
        var settingsPath=Path.Combine(root,"settings-roundtrip.json");appearance.Save(settingsPath);var loaded=AssetSettings.Load(settingsPath);
        Check(loaded.LightAzimuth==210&&loaded.LightElevation==25&&loaded.LightIntensity==1.3&&loaded.AmbientIntensity==.2&&loaded.ShadowIntensity==.4&&!loaded.CastShadows&&loaded.Outlines&&loaded.OutlineThickness==2&&loaded.OutlineType==OutlineStyle.VisibleComponents,"all v0.2 settings round trip");
        var before=SHA256.HashData(File.ReadAllBytes(model));
        var pipeline=new ConversionPipeline(Toolchain.Discover());var results=new Dictionary<string,ConversionOutput>();
        var basis=new AssetSettings{Model=model,Width=96,Height=96};
        async Task<ConversionOutput> Generate(string name,AssetSettings s)
        {
            Console.WriteLine("INTEGRATION: "+name);var r=await pipeline.Generate(s,root,new Progress<string>(Console.WriteLine));results[name]=r;
            Check(File.Exists(r.Aseprite)&&File.Exists(Path.Combine(r.Directory,"validation.json")),name+" native composite and pixel validation");return r;
        }
        var se=await Generate("A · SE / fixed studio",basis);
        var ne=await Generate("B · NE / same studio",basis with{View=CameraView.IsometricNE});
        using(var a=JsonDocument.Parse(File.ReadAllText(Path.Combine(se.Directory,"model.json"))))
        using(var b=JsonDocument.Parse(File.ReadAllText(Path.Combine(ne.Directory,"model.json"))))
        {
            Check(a.RootElement.GetProperty("camera_rotation").ToString()==b.RootElement.GetProperty("camera_rotation").ToString(),"fixed isometric camera orientation");
            var aLocation=a.RootElement.GetProperty("camera_location").EnumerateArray().Select(v=>v.GetDouble()).ToArray();var bLocation=b.RootElement.GetProperty("camera_location").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            Check(aLocation.Zip(bLocation).All(p=>Math.Abs(p.First-p.Second)<.0001),"stationary isometric camera location");
            Check(a.RootElement.GetProperty("light_rotation").ToString()==b.RootElement.GetProperty("light_rotation").ToString(),"fixed studio light orientation");
            Check(a.RootElement.GetProperty("model_rotation_degrees").GetDouble()!=b.RootElement.GetProperty("model_rotation_degrees").GetDouble(),"model actually rotates");
        }
        Check(!Raster.Read(se.Png).Pixels.SequenceEqual(Raster.Read(ne.Png).Pixels),"asymmetric directions differ");
        var low=await Generate("C · world / 1 px per unit",basis with{WorldScale=true,PixelsPerUnit=1});
        var high=await Generate("C · world / 2 px per unit",basis with{WorldScale=true,PixelsPerUnit=2});
        var lowBounds=Bounds(Raster.Read(low.Png));var highBounds=Bounds(Raster.Read(high.Png));
        Check(Math.Abs(highBounds.W-2*lowBounds.W)<=2&&Math.Abs(highBounds.H-2*lowBounds.H)<=2,"doubling density doubles actual pixel dimensions");
        Check(Math.Abs(highBounds.Bottom-lowBounds.Bottom)<=1,"density retains lower ground anchor");
        using(var f=JsonDocument.Parse(File.ReadAllText(Path.Combine(high.Directory,"framing.json"))))
        {
            var size=f.RootElement.GetProperty("projected_world_size");Check(Math.Abs(highBounds.W-size[0].GetDouble()*2)<=2&&Math.Abs(highBounds.H-size[1].GetDouble()*2)<=2,"projected world dimensions agree with actual pixel density");
        }
        var badRoot=Path.Combine(root,"clipping");
        try{await pipeline.Generate(basis with{WorldScale=true,PixelsPerUnit=16},badRoot);throw new Exception("Clipping was not rejected");}
        catch(InvalidDataException e){Check(e.Message.Contains("needs approximately")&&e.Message.Contains("Fit to Canvas")&&!e.Message.Contains("Traceback"),"D · actionable scale warning");}
        Check(!Directory.GetFiles(badRoot,"render.png",SearchOption.AllDirectories).Any(),"D · no rendering for invalid scale");
        var clean=await Generate("E · outlines off",basis with{Width=64,Height=64});
        var outer=await Generate("E · outer / 1 pixel",basis with{Width=64,Height=64,Outlines=true});
        var component=await Generate("E · components / 1 pixel",basis with{Width=64,Height=64,Outlines=true,OutlineType=OutlineStyle.VisibleComponents});
        var thick=await Generate("E · outer / 2 pixels",basis with{Width=64,Height=64,Outlines=true,OutlineThickness=2});
        Check(!Raster.Read(clean.Png).Pixels.SequenceEqual(Raster.Read(outer.Png).Pixels),"outer outlines affect final pixels");
        Check(!Raster.Read(outer.Png).Pixels.SequenceEqual(Raster.Read(component.Png).Pixels),"component contours distinguish visible objects");
        Check(Bounds(Raster.Read(clean.Png))==Bounds(Raster.Read(thick.Png)),"inward outlines preserve silhouette bounds");
        Check(File.Exists(Path.Combine(component.Directory,"outlines.png")),"separate editable outline layer");
        var lit=await Generate("Light · lower right",basis with{LightAzimuth=315,LightElevation=30,LightIntensity=1.4,AmbientIntensity=.15});
        Check(!Raster.Read(se.Png).Pixels.SequenceEqual(Raster.Read(lit.Png).Pixels),"lighting changes actual palette-constrained pixels");
        var fill=await Generate("Light · lifted shadows",basis with{ShadowIntensity=0});
        Check(!Raster.Read(se.Png).Pixels.SequenceEqual(Raster.Read(fill.Png).Pixels),"shadow intensity changes actual pixels");
        var noCast=await Generate("Light · cast shadows off",basis with{CastShadows=false});
        Check(!Raster.Read(se.Png).Pixels.SequenceEqual(Raster.Read(noCast.Png).Pixels),"cast shadow toggle changes model self-shadowing");
        var portrait1=await Generate("H · 24×42 / 0.5 px per unit",basis with{Width=24,Height=42,WorldScale=true,PixelsPerUnit=.5});
        var portrait2=await Generate("H · 24×42 / 0.7 px per unit",basis with{Width=24,Height=42,WorldScale=true,PixelsPerUnit=.7});
        Check(Bounds(Raster.Read(portrait2.Png)).W>Bounds(Raster.Read(portrait1.Png)).W,"portrait density visibly changes size");
        using(var f=JsonDocument.Parse(File.ReadAllText(Path.Combine(portrait2.Directory,"framing.json"))))
        {
            var size=f.RootElement.GetProperty("projected_world_size");var measured=Bounds(Raster.Read(portrait2.Png));
            Check(Math.Abs(measured.W-size[0].GetDouble()*.7)<=2&&Math.Abs(measured.H-size[1].GetDouble()*.7)<=2,"portrait density agrees with geometry and aspect ratio");
        }
        var again=await Generate("Repeat · SE deterministic",basis);
        Check(Raster.Read(se.Png).Pixels.SequenceEqual(Raster.Read(again.Png).Pixels),"deterministic rendering preserved");
        var external=Path.Combine(Path.GetTempPath(),"PixelForge-v02-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(external);
        string exported=Path.Combine(external,"Mine.png");AssetExport.SavePng(se,exported);
        Check(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(se.Png)),"G · PNG outside application folder matches exact bytes");
        Check(Raster.Read(exported).Pixels.SequenceEqual(Raster.Read(se.Png).Pixels),"G · exported PNG opens and matches preview pixels");
        AssetExport.SavePng(se,exported);Check(File.Exists(exported),"explicit overwrite path works");
        using(var locked=new FileStream(exported,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Reject(()=>AssetExport.SavePng(se,exported),"locked destination gives actionable error");
        AssetExport.SaveNative(component,Path.Combine(external,"Mine.aseprite"));Check(File.Exists(Path.Combine(external,"Mine.aseprite")),"native export outside application folder");
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(model))),"original model preserved");
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{passed,results,externalExport=exported},AssetSettings.JsonOptions));
        ComparisonSheet(results.Take(14).ToArray(),Path.Combine(root,"comparisons.png"));
        Console.WriteLine($"v0.2: {passed} executed checks passed. Comparisons: {root}");
    }
    private static (int W,int H,int Bottom) Bounds(Raster image)
    {
        var points=Enumerable.Range(0,image.Width*image.Height).Where(i=>image.Pixels[i*4+3]!=0).Select(i=>(X:i%image.Width,Y:i/image.Width)).ToArray();
        return(points.Max(p=>p.X)-points.Min(p=>p.X)+1,points.Max(p=>p.Y)-points.Min(p=>p.Y)+1,points.Max(p=>p.Y));
    }
    public static async Task RunAssembly(string flat,string parented,string large,string root)
    {
        var pipeline=new ConversionPipeline(Toolchain.Discover());Directory.CreateDirectory(root);
        var paths=new List<string>();
        foreach(var view in new[]{CameraView.IsometricSE,CameraView.IsometricNE,CameraView.IsometricSW,CameraView.IsometricNW})
        {
            var settings=new AssetSettings{Model=Path.GetFullPath(flat),View=view};
            var a=await pipeline.Generate(settings,root);var b=await pipeline.Generate(settings with{Model=Path.GetFullPath(parented)},root);
            paths.Add(a.Directory);paths.Add(b.Directory);
            Check(Raster.Read(a.Png).Pixels.SequenceEqual(Raster.Read(b.Png).Pixels),"transformed parent preserves assembly and colors in "+view);
        }
        var world=new AssetSettings{Model=Path.GetFullPath(flat),Width=96,Height=96,WorldScale=true,PixelsPerUnit=10};
        var small=await pipeline.Generate(world,root);var big=await pipeline.Generate(world with{Model=Path.GetFullPath(large)},root);
        var sb=Bounds(Raster.Read(small.Png));var bb=Bounds(Raster.Read(big.Png));
        Check(Math.Abs(bb.W-2*sb.W)<=2&&Math.Abs(bb.H-2*sb.H)<=2,"two models at the same density preserve 2:1 physical size");
        File.WriteAllLines(Path.Combine(root,"tested-outputs.txt"),paths.Concat([small.Directory,big.Directory]));
        Console.WriteLine($"{passed} actual hierarchy/relative-scale checks passed.");
    }
    public static async Task RunLighting(string model,string root)
    {
        var pipeline=new ConversionPipeline(Toolchain.Discover());var basis=new AssetSettings{Model=Path.GetFullPath(model),Width=96,Height=96};
        var original=await pipeline.Generate(basis,root);var baseline=Raster.Read(original.Png);
        var comparisons=new Dictionary<string,ConversionOutput>{{"Default studio",original}};
        foreach(var (label,settings) in new[]{("Azimuth only · 315°",basis with{LightAzimuth=315}),("Elevation only · 10°",basis with{LightElevation=10}),("Intensity only · 0.2",basis with{LightIntensity=.2}),("Ambient only · 0",basis with{AmbientIntensity=0})})
        {
            var result=await pipeline.Generate(settings,root);comparisons[label]=result;
            Check(!Raster.Read(result.Png).Pixels.SequenceEqual(baseline.Pixels),label+" independently affects rendered pixels");
        }
        ComparisonSheet(comparisons.ToArray(),Path.Combine(root,"lighting-comparisons.png"));
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{passed,comparisons},AssetSettings.JsonOptions));
    }
    internal static void ComparisonSheet(KeyValuePair<string,ConversionOutput>[] results,string path)
    {
        Exception? error=null;var thread=new Thread(()=>
        {
            try
            {
                int cell=330,rows=(results.Length+3)/4;var visual=new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
                using(var d=visual.RenderOpen())
                {
                    d.DrawRectangle(new SolidColorBrush(Color.FromRgb(24,28,36)),null,new Rect(0,0,cell*4,cell*rows));
                    for(int i=0;i<results.Length;i++)
                    {
                        int x=i%4*cell,y=i/4*cell;
                        var text=new FormattedText(results[i].Key,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),15,Brushes.White,1);
                        d.DrawText(text,new Point(x+12,y+10));
                        for(int yy=0;yy<288;yy+=12)for(int xx=0;xx<312;xx+=12)d.DrawRectangle(new SolidColorBrush((xx/12+yy/12)%2==0?Color.FromRgb(46,51,61):Color.FromRgb(39,44,53)),null,new Rect(x+9+xx,y+34+yy,12,12));
                        var original=Raster.Read(results[i].Value.Png);
                        int zoom=Math.Max(1,Math.Min(3,Math.Min(312/original.Width,288/original.Height)));
                        var raster=original.Enlarge(zoom);
                        var bitmap=BitmapSource.Create(raster.Width,raster.Height,96,96,PixelFormats.Bgra32,null,raster.Pixels,raster.Width*4);
                        d.DrawImage(bitmap,new Rect(x+(cell-raster.Width)/2,y+34+(288-raster.Height)/2,raster.Width,raster.Height));
                    }
                }
                var render=new RenderTargetBitmap(cell*4,cell*rows,96,96,PixelFormats.Pbgra32);render.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using var file=File.Create(path);png.Save(file);
            }catch(Exception e){error=e;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
}
