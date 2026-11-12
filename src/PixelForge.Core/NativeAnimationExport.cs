using System.Globalization;
using System.IO;
using System.Text;

namespace PixelForge.Core;
internal static class NativeAnimationExport
{
    public static async Task<int> Create(Toolchain tools,AssetSettings settings,string directory,IReadOnlyList<FrameLayers> frames,CancellationToken cancellation)
    {
        var lua=new StringBuilder($"local sprite=Sprite({settings.Width},{settings.Height},ColorMode.RGB)\nlocal initial=sprite.layers[1]\n");
        lua.AppendLine($"local palette=Palette({settings.Colors.Length})");
        for(int i=0;i<settings.Colors.Length;i++)
        {var c=Palette.ParseColor(settings.Colors[i]);lua.AppendLine($"palette:setColor({i},Color{{r={c.R},g={c.G},b={c.B},a=255}})");}
        lua.AppendLine("sprite:setPalette(palette)");
        for(int i=1;i<frames.Count;i++)lua.AppendLine($"sprite:newEmptyFrame({i+1})");
        var layers=frames.SelectMany(f=>f.Layers).DistinctBy(l=>l.Key).OrderBy(l=>l.Order).ThenBy(l=>l.Key).ToArray();
        foreach(var (layer,index) in layers.Select((value,i)=>(value,i)))
        {
            lua.AppendLine($"local layer{index}=sprite:newLayer(); layer{index}.name={Quote(layer.Name)}");
            for(int n=0;n<frames.Count;n++)
            {
                var cel=frames[n].Layers.FirstOrDefault(l=>l.Key==layer.Key);if(cel is null)continue;
                lua.AppendLine($"sprite:newCel(layer{index},{n+1},Image{{fromFile={Quote(cel.Path)}}},Point(0,0))");
            }
        }
        lua.AppendLine("sprite:deleteLayer(initial)\nlocal corrections=sprite:newLayer(); corrections.name='Manual corrections'");
        for(int n=0;n<frames.Count;n++)lua.AppendLine($"sprite.frames[{n+1}].duration={Seconds(frames[n].Output.DurationMs)}");
        if(settings.Animation)lua.AppendLine($"local tag=sprite:newTag(1,{frames.Count}); tag.name={Quote(string.IsNullOrWhiteSpace(settings.AnimationTag)?"Animation":settings.AnimationTag)}");
        var ase=Path.Combine(directory,"asset.aseprite");
        lua.AppendLine($"sprite:saveAs({Quote(ase)})\nsprite:close()\nlocal reopened=app.open({Quote(ase)})");
        lua.AppendLine($"assert(reopened.width=={settings.Width} and reopened.height=={settings.Height})\nassert(#reopened.frames=={frames.Count})\nassert(#reopened.layers=={layers.Length+1})");
        if(settings.Animation)lua.AppendLine($"assert(#reopened.tags==1 and reopened.tags[1].fromFrame.frameNumber==1 and reopened.tags[1].toFrame.frameNumber=={frames.Count})");
        for(int n=0;n<frames.Count;n++)
        {
            string png=Path.Combine(Path.GetDirectoryName(frames[n].Output.Png)!,"aseprite-reopened.png");
            lua.AppendLine($"assert(math.abs(reopened.frames[{n+1}].duration-{Seconds(frames[n].Output.DurationMs)})<0.0001)\nlocal flat=Image(reopened.width,reopened.height,ColorMode.RGB)\nflat:drawSprite(reopened,{n+1},Point(0,0))\nflat:saveAs({Quote(png)})");
        }
        var script=Path.Combine(directory,"export.lua");await File.WriteAllTextAsync(script,lua.ToString(),cancellation);
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(directory,"aseprite.log"),cancellation);
        if(!File.Exists(ase))throw new IOException("Aseprite did not create a native document. See aseprite.log.");
        return layers.Length;
    }
    private static string Seconds(int ms)=>(ms/1000.0).ToString("0.000",CultureInfo.InvariantCulture);
    private static string Quote(string value)=>"\""+value.Replace("\\","/").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
}
