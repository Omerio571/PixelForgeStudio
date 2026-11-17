using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelForge.Core;

internal static class CameraChecks
{
    public static async Task Verify(string model,string root)
    {
        var pipeline=new ConversionPipeline(Toolchain.Discover());
        var common=new AssetSettings
        {
            Model=Path.GetFullPath(model),Width=64,Height=48,Frame=1,Padding=0,
            SamplesPerPixel=1,GroundShadows=true,FitBounds=FitBoundsSource.ObjectAndGroundShadow,
            Outlines=true,OutlineType=OutlineStyle.Selective,EdgeHighlights=true,RespectOutlineGroups=true
        };
        var cases=new List<object>();
        foreach(var family in Enum.GetValues<CameraFamily>())
        foreach(var preset in CameraSelection.Perspectives(family))
        {
            bool animated=preset is CameraPerspective.TopDownThreeQuarter or CameraPerspective.AngledUnderside or CameraPerspective.SlightlyElevatedSide;
            bool fixedScale=preset==CameraPerspective.SlightlyElevatedSide;
            var settings=common with {Camera=new(family,CameraSelection.Directions(family)[0],preset),
                RenderAllDirections=true,Animation=animated,StartFrame=1,EndFrame=4,FrameStep=3,
                Fps=24,WorldScale=fixedScale,PixelsPerUnit=4};
            var batch=await pipeline.GenerateDirections(settings,root);
            var directions=new HashSet<CameraDirection>();
            var images=new HashSet<string>();
            var framing=new HashSet<string>();
            foreach(var item in batch)
            {
                using var json=JsonDocument.Parse(File.ReadAllText(item.Metadata!));
                var meta=json.RootElement;
                var camera=meta.GetProperty("camera").Deserialize<CameraSelection>(AssetSettings.JsonOptions)!;
                directions.Add(camera.Direction);
                images.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Raster.Read(item.Png).Pixels)));
                framing.Add(string.Join("|",new[]{"groundWorld","worldUnitsPerPixel","pivot"}.Select(key=>meta.GetProperty(key).GetRawText())));
                if(item.Frames.Length!=settings.FrameCount||item.Frames.Sum(f=>f.DurationMs)!=Math.Round(settings.FrameCount*1000.0/settings.Fps))
                    throw new InvalidDataException("Unexpected directional frame count or duration.");
                if(fixedScale&&meta.GetProperty("worldUnitsPerPixel").GetDouble()!=.25)
                    throw new InvalidDataException("Fixed world scale was not preserved.");
            }
            if(batch.Length!=4 || directions.Count!=4 || images.Count!=4 || framing.Count!=1)
                throw new InvalidDataException($"Invalid directional output or framing for {preset}.");
            if(preset==CameraPerspective.TopDownThreeQuarter)
            {
                var folder=AssetExport.SaveDirections(batch,root);
                using var index=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"directions.json")));
                foreach(var path in index.RootElement.GetProperty("directions").EnumerateArray())
                {
                    var filename=path.GetString()!;
                    var stem=filename[..^".sheet.json".Length];
                    using var exported=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,filename)));
                    var image=exported.RootElement.GetProperty("image").GetString()!;
                    if(!File.Exists(Path.Combine(folder,image))||
                       !File.Exists(Path.Combine(folder,stem+".aseprite"))||
                       Directory.GetFiles(Path.Combine(folder,stem+".frames"),"*.png").Length!=settings.FrameCount)
                        throw new InvalidDataException("Directional export is incomplete.");
                }
                Console.WriteLine("PASS: four named directional sheets, JSON, native animations and PNG-frame sets");
            }
            cases.Add(new {family,preset,animated,directions,sharedFraming=true});
            Console.WriteLine($"PASS: {preset}, four distinct views with shared scale and anchor");
        }
        foreach(var view in Enum.GetValues<CameraView>().Where(v=>v.ToString().StartsWith("Isometric")))
        foreach(var projection in Enum.GetValues<Projection>())
        {
            var old=common with {View=view,Projection=projection};
            var legacy=await pipeline.Generate(old,root);
            var modern=await pipeline.Generate(old with {Camera=CameraSelection.FromLegacy(view,projection)},root);
            if(!Raster.Read(legacy.Png).Pixels.SequenceEqual(Raster.Read(modern.Png).Pixels))
                throw new InvalidDataException($"Legacy camera differs for {view} / {projection}.");
            Console.WriteLine($"PASS: {view} / {projection} legacy pixel equivalence");
        }
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new {cases,legacyPixelComparisons=8},AssetSettings.JsonOptions));
    }

    public static async Task Compare(string model,string root)
    {
        root=Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        var settings=new AssetSettings {Model=Path.GetFullPath(model),Frame=1,Padding=.08};
        (string Label, CameraSelection Camera)[] views =
        [
            ("A. Dimetric 2-1",new(CameraFamily.Isometric,CameraDirection.SE,CameraPerspective.Dimetric2To1)),
            ("B. True Isometric",new(CameraFamily.Isometric,CameraDirection.SE,CameraPerspective.TrueIsometric)),
            ("C. Direct Top",new(CameraFamily.Top,CameraDirection.N,CameraPerspective.DirectTop)),
            ("D. High Top-Down 75°",new(CameraFamily.Top,CameraDirection.S,CameraPerspective.HighTopDown)),
            ("E. Top-Down 3-4 60°",new(CameraFamily.Top,CameraDirection.S,CameraPerspective.TopDownThreeQuarter,60)),
            ("F. Top-Down 3-4 45°",new(CameraFamily.Top,CameraDirection.S,CameraPerspective.TopDownThreeQuarter,45))
        ];
        var outputs=await new ConversionPipeline(Toolchain.Discover()).GenerateShared(
            views.Select(v=>settings with {Camera=v.Camera}).ToArray(),Path.Combine(root,"work"));
        var entries=new List<JsonElement>();
        for(int i=0;i<outputs.Length;i++)
        {
            File.Copy(outputs[i].Png,Path.Combine(root,$"{(char)('A'+i)}.png"));
            using var json=JsonDocument.Parse(File.ReadAllText(outputs[i].Metadata!));
            entries.Add(json.RootElement.Clone());
        }
        if(entries.Select(e=>e.GetProperty("worldUnitsPerPixel").GetDouble()).Distinct().Count()!=1)
            throw new InvalidDataException("Comparison scale differs between views.");
        File.WriteAllText(Path.Combine(root,"comparison.json"),JsonSerializer.Serialize(new
        {
            settings,views=views.Select((v,i)=>new {v.Label,file=$"{(char)('A'+i)}.png",metadata=entries[i]})
        },AssetSettings.JsonOptions));
        var thread=new Thread(()=>
        {
            const int w=288,h=306;
            var visual=new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
            using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(39,42,48)),null,new Rect(0,0,w*3,h*2));
                for(int i=0;i<outputs.Length;i++)
                {
                    int x=i%3*w,y=i/3*h;
                    dc.DrawImage(new BitmapImage(new Uri(outputs[i].Png)),new Rect(x+16,y+37,256,256));
                    var label=views[i].Label.Replace("2-1","2:1").Replace("3-4","3/4");
                    dc.DrawText(new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),18,Brushes.White,1),new Point(x+16,y+8));
                }
            }
            var bitmap=new RenderTargetBitmap(w*3,h*2,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(root,"comparison.png"));
            encoder.Save(stream);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();thread.Join();
        Console.WriteLine("Six rendered perspectives validated at shared scale: "+root);
    }
}
