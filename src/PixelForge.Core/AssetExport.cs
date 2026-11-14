using System.IO;

namespace PixelForge.Core;
public static class AssetExport
{
    public static void SavePng(ConversionOutput output,string destination)
        =>SaveFrame(output,0,destination);
    public static void SaveFrame(ConversionOutput output,int frameIndex,string destination)
    {
        var settings=AssetSettings.Load(Path.Combine(output.Directory,"asset.settings.json"));
        var path=output.Frames.Length==0?output.Png:output.Frames[Math.Clamp(frameIndex,0,output.Frames.Length-1)].Png;
        PixelEngine.Validate(Raster.Read(path),settings);
        Copy(path,destination);
    }
    public static void SaveNative(ConversionOutput output,string destination)
    {
        using(var source=File.OpenRead(output.Aseprite))
        {
            if(source.Length<128)throw new InvalidDataException("The native Aseprite file is missing or incomplete.");
            source.Position=4;if(source.ReadByte()!=0xE0||source.ReadByte()!=0xA5)throw new InvalidDataException("The native file is not a valid Aseprite document.");
        }
        Copy(output.Aseprite,destination);
    }
    public static void SaveSpritesheet(ConversionOutput output,string destination)
    {
        if(output.Spritesheet is null||output.Metadata is null)throw new InvalidDataException("Generate an asset before exporting a spritesheet.");
        Copy(output.Spritesheet,destination);
        // Match the user-selected PNG filename in its companion metadata.
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(output.Metadata))!;
        json["image"]=Path.GetFileName(destination);
        var temporary=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".pixelforge-sheet-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temporary,json.ToJsonString(AssetSettings.JsonOptions));Copy(temporary,Path.ChangeExtension(destination,"json"));}
        finally{try{File.Delete(temporary);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
    }
    public static string SaveFrames(ConversionOutput output,string selectedDirectory)
    {
        // A fresh child directory protects any manually edited previous frame set.
        var directory=Path.Combine(selectedDirectory,"PixelForge-frames-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(directory);
        foreach(var (frame,index) in output.Frames.Select((f,i)=>(f,i)))Copy(frame.Png,Path.Combine(directory,$"frame-{index:0000}.png"));
        var source=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(output.Metadata!))!;
        File.WriteAllText(Path.Combine(directory,"animation-metadata.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            canvas=source["canvas"],pivot=source["pivot"],fps=source["fps"],tags=source["tags"],
            frames=output.Frames.Select((f,i)=>new{index=i,file=$"frame-{i:0000}.png",sourceFrame=f.SourceFrame,durationMs=f.DurationMs})
        },AssetSettings.JsonOptions));
        return directory;
    }
    private static void Copy(string source,string destination)
    {
        destination=Path.GetFullPath(destination);
        if(string.Equals(Path.GetFullPath(source),destination,StringComparison.OrdinalIgnoreCase))return;
        string temporary=Path.Combine(Path.GetDirectoryName(destination)!,".pixelforge-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            // Stage in the chosen directory, then replace only after a complete write.
            File.Copy(source,temporary,false);
            File.Move(temporary,destination,true);
        }
        catch(UnauthorizedAccessException e){throw new IOException($"Windows denied saving '{destination}'. Choose a folder your account can write to, or check whether the destination file is read-only. No alternate location was used.",e);}
        catch(IOException e){throw new IOException($"Could not save '{destination}'. Close programs using the destination file, check the folder exists and has space, then retry. No alternate location was used.",e);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
