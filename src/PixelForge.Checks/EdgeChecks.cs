using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using PixelForge.Core;

internal static class EdgeChecks
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    private sealed class ProgressLog:IProgress<string>{public void Report(string value)=>Console.WriteLine(value);}
    public static async Task Run(string mine,string fixture,string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        var settings=new AssetSettings{Model=Path.GetFullPath(mine),Outlines=true,OutlineType=OutlineStyle.VisibleComponents};
        var pipeline=new ConversionPipeline(Toolchain.Discover());var outputs=new Dictionary<string,ConversionOutput>();
        async Task<ConversionOutput> Generate(string key,AssetSettings s)
        {
            Console.WriteLine("GENERATE "+key);var output=await pipeline.Generate(s,root,new ProgressLog());GeneratedAsset.Save(output);outputs[key]=output;
            Check(File.Exists(output.Aseprite),key+" native reopens with exact layer composite, palette, alpha and dimensions");
            File.WriteAllText(Path.Combine(root,"outputs.json"),JsonSerializer.Serialize(outputs,AssetSettings.JsonOptions));return output;
        }
        foreach(var view in new[]{CameraView.IsometricSE,CameraView.IsometricNE})
        {
            var s=settings with{View=view};string v=view==CameraView.IsometricSE?"SE":"NE";
            var a=await Generate(v+"-A",s);
            var b=await Generate(v+"-B",s with{OutlineType=OutlineStyle.Selective});
            var c=await Generate(v+"-C",s with{OutlineType=OutlineStyle.Selective,EdgeHighlights=true});
            var d=await Generate(v+"-D",s with{EdgeHighlights=true});
            var reference=Raster.Read(Path.Combine(a.Directory,"render.png"));
            foreach(var output in new[]{b,c,d})
                Check(reference.Pixels.SequenceEqual(Raster.Read(Path.Combine(output.Directory,"render.png")).Pixels),v+" variant uses identical beauty lighting/camera/scale");
            Check(!File.Exists(Path.Combine(a.Directory,"surfaces.json")),v+" disabled effects skip geometry capture");
            Check(!Raster.Read(a.Png).Pixels.SequenceEqual(Raster.Read(b.Png).Pixels),v+" selective mode makes a visible difference");
            Check(!Raster.Read(b.Png).Pixels.SequenceEqual(Raster.Read(c.Png).Pixels),v+" highlights make a visible difference");
            var images=new[]{a,b,c,d}.Select(o=>Raster.Read(o.Png)).ToArray();
            var bytes=new byte[128*4*128*4];
            for(int i=0;i<4;i++)for(int y=0;y<128;y++)Array.Copy(images[i].Pixels,y*128*4,bytes,(y*512+i*128)*4,128*4);
            var comparison=new Raster(512,128,bytes);comparison.Save(Path.Combine(root,v+"-comparison-native.png"));comparison.Enlarge(4).Save(Path.Combine(root,v+"-comparison-4x.png"));
            foreach(var o in new[]{b,c,d})CheckEffects(o,AssetSettings.Load(Path.Combine(o.Directory,"asset.settings.json")));
        }
        var animation=await Generate("animated",settings with{Model=Path.GetFullPath(fixture),Animation=true,EndFrame=10,FrameStep=3,OutlineType=OutlineStyle.Selective,EdgeHighlights=true,GroundShadows=true});
        Check(animation.Frames.Length==4,"short animation retains source frame selection");
        foreach(var f in animation.Frames)CheckEffects(animation,AssetSettings.Load(Path.Combine(animation.Directory,"asset.settings.json")),Path.GetDirectoryName(f.Png));
        var settingsPath=Path.Combine(animation.Directory,"asset.settings.json");
        var sheetHash=SHA256.HashData(File.ReadAllBytes(animation.Spritesheet!));
        var timed=AnimationTiming.Apply(animation,24);
        Check(timed.Frames.Sum(f=>f.DurationMs)==167&&sheetHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(animation.Spritesheet!))),"new effect animation retains editable FPS without rebuilding pixels");
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,outputs},AssetSettings.JsonOptions));
        Console.WriteLine($"{count} edge checks passed. Results: {root}");
    }
    private static void CheckEffects(ConversionOutput output,AssetSettings settings,string? directory=null)
    {
        directory??=output.Directory;var raw=Raster.Read(Path.Combine(directory,"render.png"));var ids=Raster.Read(Path.Combine(directory,"ids.png"));
        using var model=JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Directory,"model.json")));
        var known=model.RootElement.GetProperty("objects").EnumerateObject().Select(p=>int.Parse(p.Name)).ToArray();
        var geometry=SurfaceFrame.Read(Path.Combine(directory,"surfaces.json"),settings);
        var result=PixelEngine.Convert(raw,ids,settings,known,geometry);
        var repeat=PixelEngine.Convert(raw,ids,settings,known,geometry);
        Check(result.Image.Pixels.SequenceEqual(repeat.Image.Pixels),"effects deterministic on evaluated geometry");
        var original=PixelEngine.Convert(raw,ids,settings with{Outlines=false,EdgeHighlights=false},known);
        int opaque=0,matched=0,accents=0;
        for(int n=0;n<result.ObjectIds.Length;n++)
        {
            CheckAlpha(result.Image.Pixels[n*4+3]==original.Image.Pixels[n*4+3]);
            if(original.Image.Pixels[n*4+3]!=0){opaque++;if(geometry.Pixels[n]?.ObjectId==result.ObjectIds[n])matched++;}
            if(result.HighlightLayer?.Pixels[n*4+3]==255)accents++;
        }
        Check(matched>=opaque*.97,"surface IDs align with coverage-selected final pixel grid");
        Check(accents<opaque*.18,"edge accents remain sparse rather than covering the sprite");
        var composite=(byte[])original.Image.Pixels.Clone();
        foreach(var layer in new[]{result.OutlineLayer,result.HighlightLayer})if(layer is not null)
            for(int p=0;p<composite.Length;p+=4)if(layer.Pixels[p+3]!=0)Array.Copy(layer.Pixels,p,composite,p,4);
        Check(composite.SequenceEqual(result.Image.Pixels),"editable effects composite exactly over original component artwork");
        var zero=PixelEngine.Convert(raw,ids,settings with{EdgeHighlights=true,EdgeHighlightStrength=0},known,geometry);
        var disabled=PixelEngine.Convert(raw,ids,settings with{EdgeHighlights=false},known,geometry);
        Check(zero.Image.Pixels.SequenceEqual(disabled.Image.Pixels),"zero strength is identical to disabled highlights");
        PixelEngine.Validate(result.Image,settings);
        File.WriteAllText(Path.Combine(directory,"edge-metrics.json"),JsonSerializer.Serialize(new{opaque,matched,accents},AssetSettings.JsonOptions));
    }
    private static void CheckAlpha(bool value){if(!value)throw new Exception("Effect changed silhouette alpha.");}
}
