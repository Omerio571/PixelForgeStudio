using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelForge.Core;
public static class GeneratedAsset
{
    public const string ManifestName="asset.output.json";
    public static void Save(ConversionOutput output)
    {
        string Relative(string path)=>Path.GetRelativePath(output.Directory,path);
        var portable=output with{Directory=".",Png=Relative(output.Png),Aseprite=Relative(output.Aseprite),Preview=Relative(output.Preview),
            Spritesheet=output.Spritesheet is null?null:Relative(output.Spritesheet),Metadata=output.Metadata is null?null:Relative(output.Metadata),
            NormalSheet=output.NormalSheet is null?null:Relative(output.NormalSheet),
            Frames=output.Frames.Select(f=>f with{Png=Relative(f.Png),Normal=f.Normal is null?null:Relative(f.Normal)}).ToArray()};
        File.WriteAllText(Path.Combine(output.Directory,ManifestName),JsonSerializer.Serialize(portable,AssetSettings.JsonOptions));
    }
    public static ConversionOutput Open(string path)
    {
        var directory=System.IO.Directory.Exists(path)?Path.GetFullPath(path):Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string p)=>Path.GetFullPath(Path.Combine(directory,p));
        ConversionOutput output;
        if(File.Exists(Path.Combine(directory,ManifestName)))
        {
            var stored=JsonSerializer.Deserialize<ConversionOutput>(File.ReadAllText(Path.Combine(directory,ManifestName)),AssetSettings.JsonOptions)??throw new InvalidDataException("Empty generated-asset manifest.");
            output=stored with{Directory=directory,Png=Resolve(stored.Png),Aseprite=Resolve(stored.Aseprite),Preview=Resolve(stored.Preview),
                Spritesheet=stored.Spritesheet is null?null:Resolve(stored.Spritesheet),Metadata=stored.Metadata is null?null:Resolve(stored.Metadata),
                NormalSheet=stored.NormalSheet is null?null:Resolve(stored.NormalSheet),
                Frames=stored.Frames.Select(f=>f with{Png=Resolve(f.Png),Normal=f.Normal is null?null:Resolve(f.Normal)}).ToArray()};
        }
        else
        {
            // Existing v0.3 generated folders need no migration or Blender invocation.
            var metadataPath=Path.Combine(directory,"asset.sheet.json");
            var json=JsonNode.Parse(File.ReadAllText(metadataPath))!;var entries=json["frames"]!.AsArray();
            var frames=entries.Select((entry,index)=>
            {
                var candidates=new[]{Path.Combine(directory,$"frame-{index:0000}","asset.png"),Path.Combine(directory,$"native-{index:0000}.png"),index==0?Path.Combine(directory,"asset.png"):""};
                string png=candidates.FirstOrDefault(File.Exists)??throw new FileNotFoundException($"Generated PNG frame {index+1} is missing. Choose the original generated asset folder.");
                return new FrameOutput(entry!["sourceFrame"]?.GetValue<int>()??-1,png,entry["durationMs"]!.GetValue<int>());
            }).ToArray();
            output=new(directory,frames[0].Png,Path.Combine(directory,"asset.aseprite"),Path.Combine(directory,"asset.preview.png"),[])
                {Frames=frames,Spritesheet=Resolve(json["image"]!.GetValue<string>()),Metadata=metadataPath};
        }
        AssetSettings.Load(Path.Combine(directory,"asset.settings.json"));
        if(output.Frames.Length is < 1 or > 256||output.Frames.Any(f=>!File.Exists(f.Png))||!File.Exists(output.Aseprite)||output.Metadata is null||!File.Exists(output.Metadata)||output.Spritesheet is null||!File.Exists(output.Spritesheet))
            throw new InvalidDataException("This is not a complete generated asset. Keep its native document, PNG frames, settings and sheet metadata together.");
        if(output.NormalSheet is not null && (!File.Exists(output.NormalSheet)||output.Frames.Any(f=>!File.Exists(f.Normal))))
            return output with {NormalSheet=null,Frames=output.Frames.Select(f=>f with {Normal=null}).ToArray(),
                Warnings=[..output.Warnings,"Lighting maps are incomplete. Regenerate the asset to restore them."]};
        return output;
    }
}
