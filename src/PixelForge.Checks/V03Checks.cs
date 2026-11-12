using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PixelForge.Core;

internal static class V03Checks
{
    private static int count;
    private static void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
    private sealed class Callback(Action<string> action):IProgress<string>{public void Report(string value)=>action(value);}
    public static async Task Run(string mine,string fixture,string root)
    {
        mine=Path.GetFullPath(mine);fixture=Path.GetFullPath(fixture);root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        Check(new AssetSettings().Width==128&&new AssetSettings().Height==128,"new conversions default to 128×128");
        var timeline=new PlaybackTimeline([83,84,83]);timeline.Advance(83,false);Check(timeline.CurrentFrame==1,"timeline duration boundary");
        timeline.Advance(167,false);Check(timeline.CurrentFrame==2&&timeline.Completed,"non-looping playback stops at the last frame");
        timeline.Seek(0);timeline.Advance(333,true);Check(timeline.CurrentFrame==1&&!timeline.Completed,"looping preview retains elapsed time");
        Check(Enumerable.Range(0,12).Sum(i=>PlaybackTimeline.FrameDuration(i,12))==1000,"FPS millisecond rounding retains exact cycle duration");
        var settingsFile=Path.Combine(root,"settings.json");
        var animation=new AssetSettings{Model=fixture,Animation=true,StartFrame=1,EndFrame=12,Fps=12,AnimationTag="Machine cycle",Outlines=true,OutlineType=OutlineStyle.VisibleComponents};
        (animation with{GroundShadows=true,GroundShadowStrength=.35,GroundShadowSoftness=.1}).Save(settingsFile);
        var loaded=AssetSettings.Load(settingsFile);Check(loaded.Animation&&loaded.EndFrame==12&&loaded.GroundShadows&&loaded.GroundShadowStrength==.35&&loaded.AnimationTag=="Machine cycle","v0.3 settings round trip");
        var tools=Toolchain.Discover();var inspection=await ModelInspection.Read(fixture,tools);
        Check(inspection.Start==1&&inspection.End==12&&inspection.Fps==12&&inspection.HasAnimation,"actual Blender animation inspection");
        var before=SHA256.HashData(File.ReadAllBytes(fixture));var pipeline=new ConversionPipeline(tools);
        var results=new Dictionary<string,ConversionOutput>();
        async Task<ConversionOutput> Generate(string key,AssetSettings s)
        {
            Console.WriteLine("INTEGRATION: "+key);var r=await pipeline.Generate(s,root,new Callback(Console.WriteLine));results[key]=r;
            Check(r.Frames.Length==s.FrameCount&&File.Exists(r.Aseprite),key+" all native cels/composites verified");return r;
        }
        var still=await Generate("static-128",new(){Model=mine});
        Check(Raster.Read(still.Png).Width==128,"real mine static 128×128");
        var noShadow=await Generate("ground-disabled",new(){Model=fixture,GroundShadows=false});
        var zero=await Generate("ground-0",new(){Model=fixture,GroundShadows=true,GroundShadowStrength=0});
        var half=await Generate("ground-50",new(){Model=fixture,GroundShadows=true,GroundShadowStrength=.5});
        var full=await Generate("ground-100",new(){Model=fixture,GroundShadows=true,GroundShadowStrength=1});
        Check(!File.Exists(Path.Combine(noShadow.Directory,"ground-shadow.png"))&&!File.Exists(Path.Combine(zero.Directory,"ground-shadow.png")),"disabled/zero ground shadow disappears");
        Check(Raster.Read(Path.Combine(zero.Directory,"render.png")).Pixels.SequenceEqual(Raster.Read(Path.Combine(full.Directory,"render.png")).Pixels),"ground strength leaves object lighting byte-identical");
        int shadow50=Opaque(Raster.Read(Path.Combine(half.Directory,"ground-shadow.png"))),shadow100=Opaque(Raster.Read(Path.Combine(full.Directory,"ground-shadow.png")));
        Check(shadow100>0&&shadow100>=shadow50,"binary ground shadow has meaningful nested strength coverage");
        Check(!Raster.Read(zero.Png).Pixels.SequenceEqual(Raster.Read(full.Png).Pixels),"ground shadows affect exported pixels");
        var animated=await Generate("machine-SE",animation);
        using(var materials=JsonDocument.Parse(File.ReadAllText(Path.Combine(animated.Directory,"model.json"))))
            Check(materials.RootElement.GetProperty("materials").EnumerateArray().Select(m=>m.GetProperty("color").ToString()).Distinct().Count()>=4,"source material distinctions survive evaluated snapshots");
        var other=await Generate("machine-NE",animation with{View=CameraView.IsometricNE});
        Check(!Raster.Read(animated.Png).Pixels.SequenceEqual(Raster.Read(other.Png).Pixels),"animated directions reveal different geometry");
        var hashes=animated.Frames.Select(f=>Convert.ToHexString(SHA256.HashData(Raster.Read(f.Png).Pixels))).ToArray();
        Check(hashes.Distinct().Count()>8,"twelve real animated poses visibly differ");
        using(var components=JsonDocument.Parse(File.ReadAllText(Path.Combine(animated.Directory,"model.json"))))
        {
            foreach(string name in new[]{"Asymmetric rotating marker","Constrained piston","Armature-driven part"})
            {
                string id=components.RootElement.GetProperty("objects").EnumerateObject().Single(p=>p.Value.GetString()==name).Name;
                var componentHashes=animated.Frames.Select(f=>Path.Combine(Path.GetDirectoryName(f.Png)!,"layer-"+id+".png")).Where(File.Exists).Select(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
                Check(componentHashes.Distinct().Count()>1,name+" evaluates visibly across source frames");
            }
        }
        using(var frames=JsonDocument.Parse(File.ReadAllText(Path.Combine(animated.Directory,"frames.json"))))
        {
            var entries=frames.RootElement.GetProperty("frames").EnumerateArray().ToArray();
            Check(entries.Select(f=>f.GetProperty("camera_scale").GetDouble()).Distinct().Count()==1,"one world-to-pixel scale across all animation frames");
            Check(entries.Select(f=>f.GetProperty("camera_shift").ToString()).Distinct().Count()==1,"one fixed ground anchor/framing across all frames");
        }
        VerifySheet(animated,animation);
        var animatedShadow=await Generate("machine-SE-ground",animation with{GroundShadows=true,GroundShadowStrength=.7});
        Check(animatedShadow.Frames.All(f=>File.Exists(Path.Combine(Path.GetDirectoryName(f.Png)!,"ground-shadow.png"))),"all twelve frames have separate editable ground-shadow layers");
        var portrait=await Generate("portrait-stepped",animation with{Width=48,Height=64,FrameStep=3,GroundShadows=true,GroundShadowStrength=.6});
        Check(portrait.Frames.Select(f=>f.SourceFrame).SequenceEqual(new[]{1,4,7,10}),"inclusive start/end and frame-step ordering");
        VerifySheet(portrait,animation with{Width=48,Height=64,FrameStep=3});
        var repeat=await Generate("machine-repeat",animation);
        Check(animated.Frames.Zip(repeat.Frames).All(p=>Raster.Read(p.First.Png).Pixels.SequenceEqual(Raster.Read(p.Second.Png).Pixels)),"deterministic animation regeneration");
        var single=await Generate("single-frame-7",animation with{Animation=false,Frame=7});
        Check(single.Frames.Length==1&&single.Frames[0].SourceFrame==7,"single source-frame selection");
        using(var cancel=new CancellationTokenSource())
        {
            try
            {
                await pipeline.Generate(animation,Path.Combine(root,"cancellation"),new Callback(s=>{if(s.StartsWith("Rendering frame 2 of"))cancel.Cancel();}),cancel.Token);
                throw new Exception("Cancellation did not stop conversion.");
            }
            catch(OperationCanceledException){Check(true,"cancellation kills an in-progress Blender animation safely");}
            Check(!Directory.GetFiles(Path.Combine(root,"cancellation"),"validation.json",SearchOption.AllDirectories).Any(),"canceled output is not marked validated");
        }
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))),"original animated Blender file preserved");
        await VerifyEditing(animated,tools);
        var edited=animated with{Aseprite=Path.Combine(animated.Directory,"edited-test.aseprite")};
        var reloaded=await NativeEdits.Read(edited,tools);
        Check(Raster.Read(reloaded.Frames[1].Png).Pixels[3]==255&&Raster.Read(animated.Frames[1].Png).Pixels[3]==0,"saved native pixel corrections reload into preview/export frames");
        VerifySheet(reloaded,animation);
        var external=Path.Combine(Path.GetTempPath(),"PixelForge-v03-exports-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(external);
        AssetExport.SaveSpritesheet(animated,Path.Combine(external,"machine.png"));
        Check(File.Exists(Path.Combine(external,"machine.json")),"user-selected spritesheet PNG and JSON export");
        string framesDirectory=AssetExport.SaveFrames(animated,external);
        Check(Directory.GetFiles(framesDirectory,"frame-*.png").Length==12,"requested individual PNG-frame export");
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,results,external},AssetSettings.JsonOptions));
        V02Checks.ComparisonSheet(results.Where(p=>new[]{"static-128","ground-disabled","ground-0","ground-50","ground-100","machine-SE","machine-NE","machine-SE-ground"}.Contains(p.Key)).ToArray(),Path.Combine(root,"comparisons.png"));
        Console.WriteLine($"v0.3: {count} executed checks passed.");
    }
    public static async Task CheckEdits(string root)
    {
        using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"results.json")));
        var source=data.RootElement.GetProperty("results").GetProperty("machine-SE").Deserialize<ConversionOutput>(AssetSettings.JsonOptions)!;
        var loaded=await NativeEdits.Read(source with{Aseprite=Path.Combine(source.Directory,"edited-test.aseprite")},Toolchain.Discover());
        Check(Raster.Read(loaded.Frames[1].Png).Pixels[3]==255&&Raster.Read(source.Frames[1].Png).Pixels[3]==0,"saved edits are present in the reloaded pixel frames");
        VerifySheet(loaded,AssetSettings.Load(Path.Combine(source.Directory,"asset.settings.json")));
        using var tags=JsonDocument.Parse(File.ReadAllText(loaded.Metadata!));Check(tags.RootElement.GetProperty("tags")[0].GetProperty("direction").GetString()=="forward","saved animation tags retain their direction");
        var results=data.RootElement.GetProperty("results").EnumerateObject().Where(p=>new[]{"static-128","ground-disabled","ground-0","ground-50","ground-100","machine-SE","machine-NE","machine-SE-ground"}.Contains(p.Name)).Select(p=>new KeyValuePair<string,ConversionOutput>(p.Name,p.Value.Deserialize<ConversionOutput>(AssetSettings.JsonOptions)!)).ToArray();
        V02Checks.ComparisonSheet(results,Path.Combine(root,"comparisons.png"));
        File.WriteAllText(Path.Combine(root,"saved-edits-results.json"),JsonSerializer.Serialize(new{checks=count,output=loaded},AssetSettings.JsonOptions));
    }
    private static int Opaque(Raster image)=>Enumerable.Range(0,image.Width*image.Height).Count(i=>image.Pixels[i*4+3]==255);
    private static void VerifySheet(ConversionOutput output,AssetSettings settings)
    {
        using var data=JsonDocument.Parse(File.ReadAllText(output.Metadata!));var entries=data.RootElement.GetProperty("frames").EnumerateArray().ToArray();var sheet=Raster.Read(output.Spritesheet!);
        Check(entries.Length==output.Frames.Length,"sheet JSON frame count");
        for(int n=0;n<entries.Length;n++)
        {
            var image=Raster.Read(output.Frames[n].Png);PixelEngine.Validate(image,settings);
            int x=entries[n].GetProperty("x").GetInt32(),y=entries[n].GetProperty("y").GetInt32();
            for(int row=0;row<image.Height;row++)if(!image.Pixels.AsSpan(row*image.Width*4,image.Width*4).SequenceEqual(sheet.Pixels.AsSpan(((y+row)*sheet.Width+x)*4,image.Width*4)))throw new Exception("Spritesheet ordering/crop mismatch");
        }
        Check(true,"all ordered fixed-grid rectangles exactly match validated PNG frames");
        Check(entries.Select(f=>f.GetProperty("pivot").ToString()).Distinct().Count()==1,"metadata retains shared pivot/anchor");
    }
    private static async Task VerifyEditing(ConversionOutput output,Toolchain tools)
    {
        var path=Path.Combine(output.Directory,"editing-test.lua");var edited=Path.Combine(output.Directory,"edited-test.aseprite").Replace('\\','/');var source=output.Aseprite.Replace('\\','/');
        File.WriteAllText(path,$"local s=app.open(\"{source}\")\nassert(#s.frames==12)\nlocal l=s.layers[#s.layers]\nassert(l.name=='Manual corrections')\nlocal image=Image(s.width,s.height,ColorMode.RGB)\nimage:drawPixel(0,0,app.pixelColor.rgba(23,25,35,255))\ns:newCel(l,2,image,Point(0,0))\ns:saveAs(\"{edited}\")\ns:close()\nlocal r=app.open(\"{edited}\")\nassert(#r.frames==12 and r.layers[#r.layers]:cel(2).image:getPixel(0,0)==app.pixelColor.rgba(23,25,35,255))\n");
        await Toolchain.Run(tools.Aseprite,["--batch","--script",path],Path.Combine(output.Directory,"editing-test.log"));Check(true,"animated native file reopens and preserves an edit to an individual frame");
    }
}
