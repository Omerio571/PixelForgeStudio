using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Nodes;

namespace PixelForge.Core;
public static class AnimationTiming
{
    public static ConversionOutput Preview(ConversionOutput output,int fps)
    {
        ValidateFps(fps);
        return output with{Frames=output.Frames.Select((f,i)=>f with{DurationMs=PlaybackTimeline.FrameDuration(i,fps)}).ToArray()};
    }
    public static void ValidateFps(int fps){if(fps is < 1 or > 120)throw new ArgumentException("Playback FPS must be a whole number from 1–120.");}

    // The .aseprite format puts a WORD duration at frame-header offset +8.
    // Never decode, flatten, recompress, or replace any artwork chunks here.
    public static int[] FrameOffsets(byte[] native)
    {
        static ushort Word(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p,2));
        if(native.Length<128||Word(native,4)!=0xA5E0||BinaryPrimitives.ReadUInt32LittleEndian(native)!=native.Length)
            throw new InvalidDataException("The saved native animation is incomplete or invalid. Save it in Aseprite and retry.");
        int count=Word(native,6),offset=128;if(count is < 1 or > 256)throw new InvalidDataException("Use a native animation with 1–256 frames.");
        var result=new int[count];
        for(int i=0;i<count;i++)
        {
            if(offset>native.Length-16||Word(native,offset+4)!=0xF1FA)throw new InvalidDataException("Invalid Aseprite frame header.");
            uint size=BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(offset,4));
            if(size<16||size>native.Length-offset)throw new InvalidDataException("Incomplete Aseprite frame data.");
            result[i]=offset;offset+=(int)size;
        }
        if(offset!=native.Length)throw new InvalidDataException("Unexpected trailing native data; timing was not changed.");
        return result;
    }

    public static ConversionOutput Apply(ConversionOutput output,int fps)
    {
        var timed=Preview(output,fps);
        if(output.Metadata is null)throw new InvalidDataException("Spritesheet metadata is missing.");
        var settingsPath=Path.Combine(output.Directory,"asset.settings.json");
        var settingsText=File.ReadAllText(settingsPath);var metadataText=File.ReadAllText(output.Metadata);
        var settings=AssetSettings.Load(settingsPath);
        var settingsJson=JsonNode.Parse(settingsText)!.AsObject();
        var fpsKey=settingsJson.Select(p=>p.Key).FirstOrDefault(k=>k.Equals("Fps",StringComparison.OrdinalIgnoreCase))??"Fps";
        settingsJson[fpsKey]=fps;
        var metadata=JsonNode.Parse(metadataText)!.AsObject();var entries=metadata["frames"]!.AsArray();
        if(entries.Count!=timed.Frames.Length)throw new InvalidDataException("Frame metadata changed. Reload the saved asset before applying timing.");
        for(int i=0;i<entries.Count;i++)entries[i]!["durationMs"]=timed.Frames[i].DurationMs;
        metadata["fps"]=fps;

        // Exclusive access reads the latest saved document and excludes concurrent saves.
        // An already-open editor can still hold unsaved changes; the UI warns explicitly.
        using var native=new FileStream(output.Aseprite,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        if(native.Length>int.MaxValue)throw new InvalidDataException("The native animation is too large to retime safely.");
        var original=new byte[(int)native.Length];native.ReadExactly(original);
        var offsets=FrameOffsets(original);
        if(offsets.Length!=timed.Frames.Length||BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(8,2))!=settings.Width||BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(10,2))!=settings.Height)
            throw new InvalidDataException("The saved native frame count or canvas changed. Use Reload saved edits first; no timing or pixels were changed.");

        var backup=Path.Combine(output.Directory,"timing-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        File.WriteAllBytes(Path.Combine(backup,"asset.before-timing.aseprite"),original);
        File.WriteAllText(Path.Combine(backup,"asset.sheet.before-timing.json"),metadataText);
        File.WriteAllText(Path.Combine(backup,"asset.settings.before-timing.json"),settingsText);
        try
        {
            WriteMetadata(output.Metadata,metadata.ToJsonString(AssetSettings.JsonOptions));
            WriteMetadata(settingsPath,settingsJson.ToJsonString(AssetSettings.JsonOptions));
            var duration=new byte[2];
            for(int i=0;i<offsets.Length;i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(duration,(ushort)timed.Frames[i].DurationMs);
                native.Position=offsets[i]+8;native.Write(duration);
            }
            native.Flush(true);
            GeneratedAsset.Save(timed);
        }
        catch(Exception failure)
        {
            // Roll back timing only. Original artwork never leaves its current file.
            foreach(int offset in offsets){native.Position=offset+8;native.Write(original.AsSpan(offset+8,2));}native.Flush(true);
            try{WriteMetadata(output.Metadata,metadataText);WriteMetadata(settingsPath,settingsText);}
            catch(Exception rollback){throw new IOException($"Timing could not be fully restored. Artwork bytes are unchanged; native/JSON backups are in {backup}.",new AggregateException(failure,rollback));}
            throw new IOException($"Timing was not applied. Close programs locking the files and retry. Original backups: {backup}",failure);
        }
        return timed;
    }
    private static void WriteMetadata(string path,string content)
    {
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,content);File.Move(temporary,path,true);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
