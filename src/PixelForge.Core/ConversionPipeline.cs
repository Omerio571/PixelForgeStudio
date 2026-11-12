using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelForge.Core;
public sealed record FrameOutput(int SourceFrame,string Png,int DurationMs);
public sealed record ConversionOutput(string Directory,string Png,string Aseprite,string Preview,string[] Warnings)
{
    public FrameOutput[] Frames { get; init; } = [];
    public string? Spritesheet { get; init; }
    public string? Metadata { get; init; }
}
internal sealed record LayerImage(string Key,string Name,string Path,int Order);
internal sealed record FrameLayers(FrameOutput Output,LayerImage[] Layers);

public sealed class ConversionPipeline(Toolchain tools)
{
    public async Task<ConversionOutput> Generate(AssetSettings settings,string outputRoot,IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        settings.Validate();tools.Validate();
        if(!File.Exists(settings.Model)||!Path.GetExtension(settings.Model).Equals(".blend",StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Choose an existing Blender .blend model.",settings.Model);
        AnimationExport.Grid(settings.Width,settings.Height,settings.FrameCount);
        var directory=Path.Combine(Path.GetFullPath(outputRoot),Path.GetFileNameWithoutExtension(settings.Model)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        System.IO.Directory.CreateDirectory(directory);
        var before=await Hash(settings.Model);
        settings.Save(Path.Combine(directory,"asset.settings.json"));
        var job=JsonSerializer.SerializeToNode(settings,AssetSettings.JsonOptions)!.AsObject();job["output"]=directory;
        var jobPath=Path.Combine(directory,"job.json");await File.WriteAllTextAsync(jobPath,job.ToJsonString(),cancellation);
        try
        {
            await Toolchain.Run(tools.Blender,["--background","--factory-startup","--disable-autoexec","--python-exit-code","1","--python",Path.Combine(AppContext.BaseDirectory,"bridge","render.py"),"--",jobPath],Path.Combine(directory,"blender.log"),cancellation,progress);
        }
        catch(IOException) when(File.Exists(Path.Combine(directory,"error.json")))
        {
            using var error=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"error.json"),cancellation));
            throw new InvalidDataException(error.RootElement.GetProperty("message").GetString()+$"\nTechnical log: {Path.Combine(directory,"blender.log")}");
        }
        var after=await Hash(settings.Model);
        if(!before.SequenceEqual(after))throw new IOException("The source Blender file changed during conversion. Output has not been approved.");
        using var metadata=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"model.json"),cancellation));
        using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"frames.json"),cancellation));
        var names=metadata.RootElement.GetProperty("objects");
        var knownIds=names.EnumerateObject().Select(p=>int.Parse(p.Name)).ToArray();
        Dictionary<int,int>? outlineGroups=null;
        if(settings.RespectOutlineGroups)
        {
            using var groups=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"outline-groups.json"),cancellation));
            outlineGroups=groups.RootElement.GetProperty("objects").EnumerateObject().ToDictionary(p=>int.Parse(p.Name),p=>p.Value.GetProperty("groupId").GetInt32());
        }
        var warnings=new HashSet<string>();var frames=new List<FrameLayers>();
        foreach(var frame in manifest.RootElement.GetProperty("frames").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var frameDirectory=frame.GetProperty("directory").GetString()!;
            progress?.Report($"Converting pixel frame {frames.Count+1} of {settings.FrameCount}…");
            var result=await Task.Run(()=>ConvertFrame(settings,frameDirectory,frame.GetProperty("source_frame").GetInt32(),frames.Count,knownIds,names,outlineGroups),cancellation);
            frames.Add(result.Layers);foreach(var warning in result.Warnings)warnings.Add(warning);
        }
        string png=Path.Combine(directory,"asset.png"),preview=Path.Combine(directory,"asset.preview.png"),ase=Path.Combine(directory,"asset.aseprite");
        if(settings.Animation)File.Copy(frames[0].Output.Png,png);
        Raster.Read(png).Enlarge(Math.Max(1,Math.Min(8,2048/Math.Max(settings.Width,settings.Height)))).Save(preview);
        progress?.Report("Creating editable Aseprite frames and verifying every composite…");
        int layerCount=await NativeAnimationExport.Create(tools,settings,directory,frames,cancellation);
        foreach(var frame in frames)
        {
            var expected=Raster.Read(frame.Output.Png);
            var reopened=Raster.Read(Path.Combine(Path.GetDirectoryName(frame.Output.Png)!,"aseprite-reopened.png"));
            PixelEngine.Validate(reopened,settings);
            if(!reopened.Pixels.SequenceEqual(expected.Pixels))throw new InvalidDataException($"Reopened Aseprite frame {frame.Output.SourceFrame} does not match its PNG composite.");
        }
        if(metadata.RootElement.TryGetProperty("warnings",out var modelWarnings))foreach(var w in modelWarnings.EnumerateArray())warnings.Add(w.GetString()!);
        if(metadata.RootElement.GetProperty("materials").EnumerateArray().Any(m=>m.GetProperty("linked_base_color").GetBoolean()))
            warnings.Add("Some materials have linked shaders/textures; explicit Base Color fallback is used.");
        var outputs=frames.Select(f=>f.Output).ToArray();
        var export=AnimationExport.Create(settings,directory,outputs,manifest.RootElement);
        await File.WriteAllTextAsync(Path.Combine(directory,"validation.json"),JsonSerializer.Serialize(new
        {
            settings.Width,settings.Height,frameCount=outputs.Length,paletteCompliant=true,binaryAlpha=true,
            sourceSha256=Convert.ToHexString(before),sourceUnchanged=true,nativeReopened=true,
            layerCompositeMatches=true,generatedLayers=layerCount,sharedCamera=true,warnings,artisticApproval="Requires human review"
        },AssetSettings.JsonOptions),cancellation);
        progress?.Report($"Ready · {outputs.Length} validated frame(s). Review motion and pixels before approving.");
        return new(directory,png,ase,preview,warnings.ToArray()){Frames=outputs,Spritesheet=export.Sheet,Metadata=export.Metadata};
    }

    private static (FrameLayers Layers,string[] Warnings) ConvertFrame(AssetSettings settings,string directory,int sourceFrame,int index,int[] ids,JsonElement names,IReadOnlyDictionary<int,int>? outlineGroups)
    {
        bool effects=settings.Outlines&&settings.OutlineType==OutlineStyle.Selective||settings.EdgeHighlights&&settings.EdgeHighlightStrength>0;
        var surfaces=effects?SurfaceFrame.Read(Path.Combine(directory,"surfaces.json"),settings):null;
        var converted=PixelEngine.Convert(Raster.Read(Path.Combine(directory,"render.png")),Raster.Read(Path.Combine(directory,"ids.png")),settings,ids,surfaces,outlineGroups);
        var layers=new List<LayerImage>();Raster? shadow=null;var frameWarnings=converted.Warnings.ToList();
        if(File.Exists(Path.Combine(directory,"ground-shadowed.png")))
            shadow=GroundShadow.Create(Raster.Read(Path.Combine(directory,"ground-shadowed.png")),Raster.Read(Path.Combine(directory,"ground-clear.png")),converted.Image,settings,frameWarnings);
        if(shadow is not null)
        {
            var path=Path.Combine(directory,"ground-shadow.png");shadow.Save(path);layers.Add(new("ground","Ground shadow",path,0));
        }
        var baseImage=converted.BaseImage??converted.Image;
        foreach(int id in converted.ObjectIds.Distinct().Order().Where(i=>i!=0))
        {
            var bytes=new byte[baseImage.Pixels.Length];bool visible=false;
            for(int n=0;n<converted.ObjectIds.Length;n++)if(converted.ObjectIds[n]==id&&baseImage.Pixels[n*4+3]!=0)
            {Array.Copy(baseImage.Pixels,n*4,bytes,n*4,4);visible=true;}
            if(!visible)continue;
            var path=Path.Combine(directory,$"layer-{id}.png");new Raster(settings.Width,settings.Height,bytes).Save(path);
            layers.Add(new("component-"+id,names.GetProperty(id.ToString()).GetString()!,path,1));
        }
        if(converted.OutlineLayer is not null)
        {
            string prefix=settings.RespectOutlineGroups?(settings.GroupedOutlines==GroupedOutlineMode.Unified?"Unified group ":"Grouped "):"";
            var path=Path.Combine(directory,"outlines.png");converted.OutlineLayer.Save(path);layers.Add(new("outlines",prefix+(settings.OutlineType==OutlineStyle.Selective?"Selective outlines":"Outlines"),path,2));
        }
        if(converted.HighlightLayer is not null)
        {
            var path=Path.Combine(directory,"edge-highlights.png");converted.HighlightLayer.Save(path);layers.Add(new("edge-highlights","Edge highlights",path,3));
        }
        var image=GroundShadow.Composite(converted.Image,shadow);PixelEngine.Validate(image,settings);
        var png=Path.Combine(directory,"asset.png");image.Save(png);
        int duration=PlaybackTimeline.FrameDuration(index,settings.Fps);
        return(new(new(sourceFrame,png,duration),layers.ToArray()),frameWarnings.ToArray());
    }
    private static async Task<byte[]> Hash(string path){using var stream=File.OpenRead(path);return await SHA256.HashDataAsync(stream);}
}
