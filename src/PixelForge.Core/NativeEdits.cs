using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace PixelForge.Core;
public static class NativeEdits
{
    public static async Task<ConversionOutput> Read(ConversionOutput generated,Toolchain tools,CancellationToken cancellation=default)
    {
        if(!File.Exists(tools.Aseprite))throw new FileNotFoundException("Aseprite was not found. Set PIXELFORGE_ASEPRITE to its executable path.");
        var settings=AssetSettings.Load(Path.Combine(generated.Directory,"asset.settings.json"));
        var before=SHA256.HashData(File.ReadAllBytes(generated.Aseprite));
        string directory=Path.Combine(generated.Directory,"saved-edits-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(directory);
        var lua=new StringBuilder($"local s=app.open({Quote(generated.Aseprite)})\nassert(s.width=={settings.Width} and s.height=={settings.Height},'Keep the original canvas dimensions when editing')\nassert(#s.frames>=1 and #s.frames<=256,'Use 1-256 frames')\nassert(s.width*s.height*#s.frames<=16777216,'Too many animation pixels')\nlocal durations={{}}\n");
        lua.AppendLine($"for n=1,#s.frames do local image=Image(s.width,s.height,ColorMode.RGB); image:drawSprite(s,n,Point(0,0)); image:saveAs({Quote(directory.Replace('\\','/')+"/native-")}..string.format('%04d',n-1)..'.png'); durations[n]=math.floor(s.frames[n].duration*1000+0.5) end");
        lua.AppendLine("local tags={}\nfor _,tag in ipairs(s.tags) do local direction='forward'; if tag.aniDir==AniDir.REVERSE then direction='reverse' elseif tag.aniDir==AniDir.PING_PONG then direction='pingpong' elseif tag.aniDir==AniDir.PING_PONG_REVERSE then direction='pingpong_reverse' end; table.insert(tags,{name=tag.name,from=tag.fromFrame.frameNumber-1,to=tag.toFrame.frameNumber-1,direction=direction}) end");
        lua.AppendLine($"local f=io.open({Quote(Path.Combine(directory,"native.json"))},'w'); f:write(json.encode{{durations=durations,tags=tags}}); f:close()");
        var script=Path.Combine(directory,"read-native.lua");await File.WriteAllTextAsync(script,lua.ToString(),cancellation);
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(directory,"aseprite.log"),cancellation);
        if(!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(generated.Aseprite))))throw new IOException("The native file changed while loading. Finish saving in Aseprite and retry.");
        using var info=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"native.json"),cancellation));
        var durations=info.RootElement.GetProperty("durations").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        var frames=new List<FrameOutput>();
        for(int n=0;n<durations.Length;n++)
        {
            cancellation.ThrowIfCancellationRequested();
            var png=Path.Combine(directory,$"native-{n:0000}.png");PixelEngine.Validate(Raster.Read(png),settings);
            if(durations[n]<1)throw new InvalidDataException("Every edited frame needs a positive duration.");
            int sourceFrame=generated.Frames.Length==durations.Length?generated.Frames[n].SourceFrame:-1;
            frames.Add(new(sourceFrame,png,durations[n]));
        }
        // Matching exact frames also proves the frame order has not changed. A
        // changed composite could represent reordered poses with identical alpha.
        bool keepMaps=generated.NormalSheet is not null&&frames.Count==generated.Frames.Length&&
            frames.Select((f,i)=>Raster.Read(f.Png).Pixels.SequenceEqual(Raster.Read(generated.Frames[i].Png).Pixels)).All(equal=>equal);
        if(keepMaps)
            for(int i=0;i<frames.Count;i++)frames[i]=frames[i] with {Normal=generated.Frames[i].Normal,Emission=generated.Frames[i].Emission};
        settings=settings with{Animation=frames.Count>1,StartFrame=1,EndFrame=frames.Count,FrameStep=1,Fps=Math.Clamp((int)Math.Round(1000.0*frames.Count/durations.Sum()),1,120)};
        settings.Save(Path.Combine(directory,"asset.settings.json"));
        File.Copy(Path.Combine(generated.Directory,"frames.json"),Path.Combine(directory,"frames.json"));
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"frames.json")));
        var exported=AnimationExport.Create(settings,directory,frames.ToArray(),manifest.RootElement);
        var metadata=JsonNode.Parse(File.ReadAllText(exported.Metadata))!;
        metadata["tags"]=JsonNode.Parse(info.RootElement.GetProperty("tags").GetRawText());metadata["savedNativeEdits"]=true;
        metadata["fps"]=1000.0*frames.Count/durations.Sum();File.WriteAllText(exported.Metadata,metadata.ToJsonString(AssetSettings.JsonOptions));
        var preview=Path.Combine(directory,"asset.preview.png");Raster.Read(frames[0].Png).Enlarge(Math.Max(1,Math.Min(8,2048/Math.Max(settings.Width,settings.Height)))).Save(preview);
        File.WriteAllText(Path.Combine(directory,"validation.json"),JsonSerializer.Serialize(new{savedNativeEdits=true,frameCount=frames.Count,paletteCompliant=true,binaryAlpha=true,sourceNativeUnchanged=true,sourceNativeSha256=Convert.ToHexString(before)},AssetSettings.JsonOptions));
        return new(directory,frames[0].Png,generated.Aseprite,preview,settings.DynamicLighting&&!keepMaps?["Lighting maps were invalidated because the saved artwork or frame order changed. Regenerate from geometry to restore them."]:[])
        {Frames=frames.ToArray(),Spritesheet=exported.Sheet,Metadata=exported.Metadata,NormalSheet=keepMaps?generated.NormalSheet:null,EmissionSheet=keepMaps?generated.EmissionSheet:null};
    }
    private static string Quote(string text)=>"\""+text.Replace("\\","/").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
}
