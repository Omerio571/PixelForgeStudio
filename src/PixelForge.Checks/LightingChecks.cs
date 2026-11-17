using System.IO;
using System.Numerics;
using System.Text.Json;
using PixelForge.Core;

internal static class LightingChecks
{
    public static async Task Run(string cube,string machine,string baseline,string root)
    {
        root=Path.GetFullPath(root);
        var pipeline=new ConversionPipeline(Toolchain.Discover());
        var compare=AssetSettings.Load(Path.Combine(baseline,"asset.settings.json"));
        var baked=await pipeline.Generate(compare,root);
        if(baked.NormalSheet is not null||!Raster.Read(baked.Png).Pixels.SequenceEqual(Raster.Read(Path.Combine(baseline,"asset.png")).Pixels))
            throw new InvalidDataException("The existing baked output changed.");


        var views=new[]
        {
            new CameraSelection(CameraFamily.Isometric,CameraDirection.NE,CameraPerspective.TrueIsometric),
            new CameraSelection(CameraFamily.Top,CameraDirection.E,CameraPerspective.TopDownThreeQuarter,60),
            new CameraSelection(CameraFamily.Isometric,CameraDirection.SW,CameraPerspective.Dimetric2To1)
        };
        var expected=new[]{new Vector3[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ},
            new Vector3[]{Vector3.UnitX,Vector3.UnitZ},new Vector3[]{-Vector3.UnitX,-Vector3.UnitY,Vector3.UnitZ}};
        for(int i=0;i<views.Length;i++)
        {
            var asset=await pipeline.Generate(compare with {Model=Path.GetFullPath(cube),Camera=views[i],DynamicLighting=true},root);
            Validate(asset);
            var emission=Raster.Read(asset.EmissionSheet!);
            if(Enumerable.Range(0,emission.Width*emission.Height).Any(index=>emission.Pixels[index*4]!=0))
                throw new InvalidDataException("A non-emissive material produced emission.");
            var map=Raster.Read(asset.Frames[0].Normal!);
            var normals=Enumerable.Range(0,map.Width*map.Height).Where(p=>map.Pixels[p*4+3]==255)
                .Select(p=>Vector3.Normalize(new Vector3(map.Pixels[p*4+2],map.Pixels[p*4+1],map.Pixels[p*4])/127.5f-Vector3.One)).ToArray();
            if(expected[i].Any(axis=>!normals.Any(n=>Vector3.Dot(n,axis)>.999f))||
               normals.Any(n=>!expected[i].Any(axis=>Vector3.Dot(n,axis)>.999f)))
                throw new InvalidDataException("A rotated cube encoded an unexpected world-space normal.");
            LightingMaps.Export(asset,root);
            Console.WriteLine($"PASS: {views[i]} retains expected world-facing normals.");
        }

        var settings=compare with {Model=Path.GetFullPath(machine),DynamicLighting=true,Animation=true,StartFrame=1,EndFrame=10,FrameStep=3,
            GroundShadows=true,Outlines=true,OutlineType=OutlineStyle.Selective,RespectOutlineGroups=true,EdgeHighlights=true};
        var machineBaked=await pipeline.Generate(settings with {DynamicLighting=false,GroundShadows=false},root);
        File.Copy(machineBaked.Png,Path.Combine(root,"baked.png"));
        var animated=await pipeline.Generate(settings,root);
        Validate(animated);
        if(animated.Frames.Select(f=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.Normal!)))).Distinct().Count()<2)
            throw new InvalidDataException("Animated normal maps did not change with the pose.");
        GeneratedAsset.Save(animated);
        var mapBytes=File.ReadAllBytes(animated.NormalSheet!);
        animated=AnimationTiming.Apply(GeneratedAsset.Open(animated.Directory),24);
        if(!File.ReadAllBytes(animated.NormalSheet!).SequenceEqual(mapBytes))throw new InvalidDataException("Retiming rewrote normal pixels.");
        var reloaded=await NativeEdits.Read(animated,Toolchain.Discover());
        if(reloaded.NormalSheet is null)throw new InvalidDataException("Reloading identical cels dropped valid maps.");
        File.Copy(animated.Png,Path.Combine(root,"base-color.png"));
        File.Copy(animated.Frames[0].Normal!,Path.Combine(root,"normal.png"));
        // Save a change to the test native document, then reload through the same
        // path used by the app. The edited native bytes must survive intact.
        string editedNative=Path.Combine(root,"edited.aseprite");
        File.Copy(animated.Aseprite,editedNative);
        var script=Path.Combine(root,"edit-alpha.lua");
        File.WriteAllText(script,"local s=app.open("+JsonSerializer.Serialize(editedNative.Replace('\\','/'))+")\n"+
            "for _,cel in ipairs(s.cels) do local im=cel.image:clone(); im:clear(); cel.image=im end\ns:saveAs("+JsonSerializer.Serialize(editedNative.Replace('\\','/'))+")");
        var tools=Toolchain.Discover();
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(root,"edit.log"));
        var editedBytes=File.ReadAllBytes(editedNative);
        var edited=await NativeEdits.Read(animated with {Aseprite=editedNative},tools);
        if(edited.NormalSheet is not null||edited.EmissionSheet is not null||edited.Frames.Any(f=>f.Normal is not null||f.Emission is not null)||
            !edited.Warnings.Any(w=>w.Contains("invalidated"))||!File.ReadAllBytes(editedNative).SequenceEqual(editedBytes))
            throw new InvalidDataException("Silhouette edits did not safely invalidate maps.");
        var export=LightingMaps.Export(animated,root);
        using var meta=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(export,"*.lighting.json").Single()));
        if(meta.RootElement.GetProperty("fps").GetInt32()!=24 || meta.RootElement.GetProperty("frames").GetArrayLength()!=4)
            throw new InvalidDataException("Lighting export did not retain updated frame timing.");
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new {animated=animated.Directory,export,bakedPixelsIdentical=true,cubeViews=3,frames=4},AssetSettings.JsonOptions));
        Console.WriteLine("PASS: baked pixels, synchronized animation atlas, native reload, and retiming.");
    }

    public static async Task Emission(string model,string root)
    {
        root=Path.GetFullPath(root);
        var pipeline=new ConversionPipeline(Toolchain.Discover());
        var settings=new AssetSettings
        {
            Model=Path.GetFullPath(model),Width=128,Height=96,Animation=true,StartFrame=1,EndFrame=4,FrameStep=3,
            DynamicLighting=true,StudioLighting=false,GroundShadows=false,
            Camera=new(CameraFamily.Top,CameraDirection.S,CameraPerspective.TopDownThreeQuarter,60)
        };
        var output=await pipeline.Generate(settings,root);
        Validate(output);
        for(int i=0;i<output.Frames.Length;i++)
        {
            var frame=output.Frames[i];var mask=Raster.Read(frame.Emission!);var color=Raster.Read(frame.Png);
            var visible=new HashSet<byte>();
            for(int p=0;p<mask.Pixels.Length;p+=4)
            {
                var v=mask.Pixels[p];
                if(v!=mask.Pixels[p+1]||v!=mask.Pixels[p+2]||mask.Pixels[p+3]!=color.Pixels[p+3])
                    throw new InvalidDataException("Emission is not grayscale and alpha-aligned.");
                if(mask.Pixels[p+3]!=0)visible.Add(v);
            }
            if(!visible.SetEquals(new byte[] {0,(byte)(i==0?128:191),255}))
                throw new InvalidDataException($"Mixed and animated material intensities do not match: {string.Join(',',visible)}.");
        }
        var atlas=File.ReadAllBytes(output.EmissionSheet!);
        GeneratedAsset.Save(output);
        output=AnimationTiming.Apply(GeneratedAsset.Open(output.Directory),24);
        if(!File.ReadAllBytes(output.EmissionSheet!).SequenceEqual(atlas))
            throw new InvalidDataException("Retiming changed emission bytes.");
        var export=LightingMaps.Export(output,root);
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{asset=output.Directory,export,frames=2,intensities=new[]{0,128,191,255}}));
        Console.WriteLine("PASS: material-level zero, partial and full emission; animated strength, alpha alignment and retiming.");
    }

    private static void Validate(ConversionOutput asset)
    {
        var color=Raster.Read(asset.Spritesheet!);var normal=Raster.Read(asset.NormalSheet!);
        if((color.Width,color.Height)!=(normal.Width,normal.Height))throw new InvalidDataException("Atlas dimensions differ.");
        for(int p=0;p<color.Pixels.Length;p+=4)
        {
            if(color.Pixels[p+3]!=normal.Pixels[p+3])throw new InvalidDataException("Atlas alpha differs.");
            if(normal.Pixels[p+3]==0 && (normal.Pixels[p]|normal.Pixels[p+1]|normal.Pixels[p+2])!=0)
                throw new InvalidDataException("Transparent normals must be zero.");
        }
        if(LightingMaps.Encode(0)!=128||LightingMaps.Encode(1)!=255||LightingMaps.Encode(-1)!=0)
            throw new InvalidDataException("Incorrect normal encoding.");
    }
}
