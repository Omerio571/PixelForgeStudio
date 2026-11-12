using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelForge.Core;

internal static class TimingChecks
{
    private static int count;
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
    public static async Task Run(string existingAsset,string outputRoot)
    {
        var source=GeneratedAsset.Open(existingAsset);
        string root=Path.GetFullPath(Path.Combine(outputRoot,"case-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        string asset=Path.Combine(root,"asset");CopyFolder(source.Directory,asset);
        var current=GeneratedAsset.Open(asset);Check(current.Frames.Length==12,"reopen existing twelve-frame v0.3 asset without Blender");
        var settingsPath=Path.Combine(asset,"asset.settings.json");var settings=AssetSettings.Load(settingsPath);
        (settings with{Model=Path.Combine(root,"model-is-not-required.blend")}).Save(settingsPath);
        var pngs=Directory.GetFiles(asset,"*.png",SearchOption.AllDirectories).ToDictionary(p=>p,p=>(Hash:Hash(p),Time:File.GetLastWriteTimeUtc(p)));
        var tools=Toolchain.Discover();var oldBlender=Environment.GetEnvironmentVariable("PIXELFORGE_BLENDER");
        Environment.SetEnvironmentVariable("PIXELFORGE_BLENDER",Path.Combine(root,"Blender-must-not-run.exe"));
        try
        {
            var clock=new PlaybackTimeline(current.Frames.Select(f=>f.DurationMs).ToArray());clock.Advance(125,false);int selected=clock.CurrentFrame;
            var preview=AnimationTiming.Preview(current,6);clock.SetDurations(preview.Frames.Select(f=>f.DurationMs).ToArray());
            Check(clock.CurrentFrame==selected&&clock.TotalDurationMs==2000,"live preview retimes to six FPS without resetting the selected frame");
            clock.Advance(90,false);Check(clock.CurrentFrame>selected,"retimed timeline advances with the new durations");
            foreach(int invalid in new[]{0,-1,121})
            {
                bool rejected=false;try{AnimationTiming.Preview(current,invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"reject invalid FPS "+invalid);
            }
            foreach(int fps in new[]{12,6,24,7,29,120,1})
            {
                if(fps==24)await AddSavedEdits(current,tools,root);
                var before=File.ReadAllBytes(current.Aseprite);var offsets=AnimationTiming.FrameOffsets(before);
                var metadataBefore=JsonNode.Parse(File.ReadAllText(current.Metadata!))!;
                current=AnimationTiming.Apply(AnimationTiming.Preview(current,fps),fps);
                var after=File.ReadAllBytes(current.Aseprite);var changedOffsets=offsets.SelectMany(p=>new[]{p+8,p+9}).ToHashSet();
                Check(before.Length==after.Length&&Enumerable.Range(0,before.Length).All(i=>changedOffsets.Contains(i)||before[i]==after[i]),$"{fps} FPS preserves EVERY native byte outside duration fields");
                Check(offsets.Select((p,i)=>BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan(p+8,2))==PlaybackTimeline.FrameDuration(i,fps)).All(v=>v),$"{fps} FPS native duration fields");
                using var metadata=JsonDocument.Parse(File.ReadAllText(current.Metadata!));var frames=metadata.RootElement.GetProperty("frames").EnumerateArray().ToArray();
                Check(frames.Select(f=>f.GetProperty("durationMs").GetInt32()).SequenceEqual(current.Frames.Select(f=>f.DurationMs))&&metadata.RootElement.GetProperty("fps").GetInt32()==fps,$"{fps} FPS sheet metadata");
                var metadataAfter=JsonNode.Parse(File.ReadAllText(current.Metadata!))!;
                foreach(var node in new[]{metadataBefore,metadataAfter}){node.AsObject().Remove("fps");foreach(var frame in node["frames"]!.AsArray())frame!.AsObject().Remove("durationMs");}
                Check(JsonNode.DeepEquals(metadataBefore,metadataAfter),$"{fps} FPS preserves metadata frame rectangles, ordering, tags and palette");
                int cumulative=0;
                for(int i=0;i<12;i++){cumulative+=current.Frames[i].DurationMs;if(Math.Abs(cumulative-(i+1)*1000.0/fps)>.500001)throw new Exception("Cumulative timing error exceeds half a millisecond.");}
                Check(true,$"{fps} FPS distributed rounding stays within 0.5 ms at every prefix");
                Check(pngs.All(p=>Hash(p.Key)==p.Value.Hash&&File.GetLastWriteTimeUtc(p.Key)==p.Value.Time),$"{fps} FPS leaves all frame/render/sheet/preview PNG bytes and timestamps untouched");
                var reopened=GeneratedAsset.Open(Path.Combine(asset,GeneratedAsset.ManifestName));
                Check(AssetSettings.Load(settingsPath).Fps==fps&&reopened.Frames.Select(f=>f.DurationMs).SequenceEqual(current.Frames.Select(f=>f.DurationMs)),$"{fps} FPS persists on asset reopen");
                await VerifyNative(current,tools,fps,root);
                if(fps==24)
                {
                    string export=AssetExport.SaveFrames(current,root);
                    using var exported=JsonDocument.Parse(File.ReadAllText(Path.Combine(export,"animation-metadata.json")));
                    Check(exported.RootElement.GetProperty("frames").EnumerateArray().Sum(f=>f.GetProperty("durationMs").GetInt32())==500,"individual frame export carries the new half-second timing");
                    string sheet=Path.Combine(root,"export-sheet.png");AssetExport.SaveSpritesheet(current,sheet);
                    Check(Hash(sheet)==Hash(current.Spritesheet!),"spritesheet export copies unchanged PNG bytes");
                }
            }
            Check(!File.Exists(Toolchain.Discover().Blender)&&!File.Exists(AssetSettings.Load(settingsPath).Model),"timing succeeds with no Blender executable and no required model");
            Check(Directory.GetFiles(Path.Combine(asset,"timing-backups"),"*.aseprite",SearchOption.AllDirectories).Length==7,"every timing apply preserves a native backup");
            var lockedBefore=File.ReadAllBytes(current.Aseprite);
            using(var locked=new FileStream(current.Aseprite,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                bool rejected=false;try{AnimationTiming.Apply(current,24);}catch(IOException){rejected=true;}Check(rejected,"locked native file is rejected without a replacement");
            }
            Check(lockedBefore.SequenceEqual(File.ReadAllBytes(current.Aseprite)),"lock failure leaves native artwork and timing untouched");
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,asset=current,evidence="Timing-only API, unavailable Blender/model, byte-identical non-duration native data, unchanged PNG hashes and timestamps",pngFilesUnchanged=pngs.Count},AssetSettings.JsonOptions));
        }
        finally{Environment.SetEnvironmentVariable("PIXELFORGE_BLENDER",oldBlender);}
        Console.WriteLine($"{count} timing checks passed. Results: {root}");
    }
    private static async Task AddSavedEdits(ConversionOutput output,Toolchain tools,string root)
    {
        var script=Path.Combine(root,"manual-edit.lua");
        File.WriteAllText(script,$"local s=app.open({Q(output.Aseprite)})\nlocal layer=s:newLayer(); layer.name='Manual cleanup preserved'\nlocal image=Image(s.width,s.height,ColorMode.RGB)\nimage:drawPixel(0,0,app.pixelColor.rgba(23,25,35,255))\ns:newCel(layer,2,image,Point(0,0))\ns.tags[1].name='Manually edited animation tag'\ns:saveAs({Q(output.Aseprite)})\n");
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(root,"manual-edit.log"));
        Check(true,"real Aseprite manual edits saved AFTER the first timing change");
    }
    private static async Task VerifyNative(ConversionOutput output,Toolchain tools,int fps,string root)
    {
        var script=Path.Combine(root,$"verify-{fps}.lua");
        var text=$"local s=app.open({Q(output.Aseprite)})\nassert(#s.frames==12)\n";
        for(int i=0;i<12;i++)text+=$"assert(math.floor(s.frames[{i+1}].duration*1000+0.5)=={output.Frames[i].DurationMs})\n";
        if(fps is not (12 or 6))text+="assert(s.tags[1].name=='Manually edited animation tag')\nlocal found=false\nfor _,layer in ipairs(s.layers) do if layer.name=='Manual cleanup preserved' then assert(layer:cel(2).image:getPixel(0,0)==app.pixelColor.rgba(23,25,35,255)); found=true end end\nassert(found)\n";
        File.WriteAllText(script,text);await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(root,$"verify-{fps}.log"));
        Check(true,$"{fps} FPS reopens correctly in actual Aseprite with saved layers, cels and tags");
    }
    private static string Q(string path)=>"\""+path.Replace('\\','/').Replace("\"","\\\"")+"\"";
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void CopyFolder(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));
        foreach(var folder in Directory.GetDirectories(source).Where(p=>!Path.GetFileName(p).StartsWith("saved-edits-")&&Path.GetFileName(p)!="timing-backups"))CopyFolder(folder,Path.Combine(destination,Path.GetFileName(folder)));
    }
}
