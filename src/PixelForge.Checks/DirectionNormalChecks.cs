using System.IO;
using PixelForge.Core;

internal static class DirectionNormalChecks
{
    public static async Task Run(string model,string root,bool full)
    {
        var pipeline=new ConversionPipeline(Toolchain.Discover());
        var basis=new AssetSettings {Model=Path.GetFullPath(model),Width=96,Height=96,DynamicLighting=true,StudioLighting=false,
            Animation=true,StartFrame=1,EndFrame=12,FrameStep=1,RenderAllDirections=true,
            Camera=new(CameraFamily.Top,CameraDirection.N,CameraPerspective.TopDownThreeQuarter,40)};
        var progress=new Progress<string>(Console.WriteLine);
        var batch=await pipeline.GenerateDirections(basis,root,progress);
        foreach(var asset in batch){Check(asset);GeneratedAsset.Save(asset);}
        if(!full)return;
        var south=batch.Single(a=>AssetSettings.Load(Path.Combine(a.Directory,"asset.settings.json")).Camera!.Direction==CameraDirection.S);
        var failing=south.Frames.Single(f=>f.SourceFrame==12);
        string diagnostics=Path.Combine(Path.GetDirectoryName(failing.Png)!,"normal-sampling.json");
        using(var recovery=System.Text.Json.JsonDocument.Parse(File.ReadAllText(diagnostics)))
        {
            var trace=recovery.RootElement.GetProperty("recovered").EnumerateArray()
                .Single(item=>item[0].GetInt32()==43&&item[1].GetInt32()==74);
            var n=System.Numerics.Vector3.Normalize(new(trace[4].GetSingle(),trace[5].GetSingle(),trace[6].GetSingle()));
            var normal=Raster.Read(failing.Normal!);
            int p=(74*normal.Width+43)*4;
            if(normal.Pixels[p+3]!=255||normal.Pixels[p]!=LightingMaps.Encode(n.Z)||normal.Pixels[p+1]!=LightingMaps.Encode(n.Y)||
               normal.Pixels[p+2]!=LightingMaps.Encode(n.X)||Raster.Read(failing.Emission!).Pixels[p]!=trace[7].GetByte())
                throw new InvalidDataException("Recovered South frame 12 does not match the visible facet normal and material emission.");
            Console.WriteLine($"PASS: South frame 12 pixel (43, 74), object {trace[2]}, facet {trace[3]}, world normal {n}.");
        }
        var bin=Environment.GetEnvironmentVariable("PIXELFORGE_WEBP_TEST")!;
        var exports=Path.Combine(root,"Assets");Directory.CreateDirectory(exports);
        var profile=new AssetExportProfile(Root:Path.GetFullPath(exports),ExportPfa:false,ExportWebp:true,
            Cwebp:Path.Combine(bin,"cwebp.exe"),Dwebp:Path.Combine(bin,"dwebp.exe"),DetailedReport:true,PngPreviews:true,BasePalette:"Missing-optional.gpl");
        var result=PfaExport.Save(profile,"Mine",batch);
        var files=Directory.GetFiles(result.Directory);
        if(files.Length!=12||files.Any(f=>Path.GetExtension(f)!=".webp")||
            Directory.GetDirectories(result.Directory).Length!=0||Directory.GetFiles(exports,"*",SearchOption.TopDirectoryOnly).Length!=0||
            string.IsNullOrWhiteSpace(result.Report))
            throw new InvalidDataException("Image-only WebP export leaked auxiliary output.");
        var optional=PfaExport.Save(profile with{IncludeGameMetadata=true,DetailedReport=false},"MineWithTiming",batch);
        if(Directory.GetFiles(optional.Directory,"*.json").Length!=1)throw new InvalidDataException("Expected one asset-level timing file.");
        foreach(var (angle,samples) in new[]{(60,1),(75,3)})
        {
            var still=basis with {RenderAllDirections=false,Animation=false,Width=128,Height=128,SamplesPerPixel=samples,
                Padding=0,Outlines=true,OutlineType=OutlineStyle.Selective,RespectOutlineGroups=true,
                Camera=new(CameraFamily.Top,CameraDirection.W,CameraPerspective.TopDownThreeQuarter,angle)};
            Check(await pipeline.Generate(still,root,progress));
        }
        Console.WriteLine("PASS: twelve-frame 96×96 40° Mine in four directions; 128×128 60°/75° single-direction static; exact image-only WebP and one opt-in manifest.");
    }

    private static void Check(ConversionOutput asset)
    {
        foreach(var frame in asset.Frames)
        {
            var color=Raster.Read(frame.Png);var normal=Raster.Read(frame.Normal!);var emission=Raster.Read(frame.Emission!);
            foreach(var map in new[]{normal,emission})
                if(map.Width!=color.Width||map.Height!=color.Height)throw new InvalidDataException("Map dimensions differ.");
            for(int p=0;p<color.Pixels.Length;p+=4)
            {
                if(color.Pixels[p+3]!=normal.Pixels[p+3]||color.Pixels[p+3]!=emission.Pixels[p+3])
                    throw new InvalidDataException("Map coverage differs.");
                if(color.Pixels[p+3]==0)continue;
                double length=Enumerable.Range(0,3).Sum(c=>Math.Pow(normal.Pixels[p+c]/127.5-1,2));
                if(length<.97||length>1.03)throw new InvalidDataException("Invalid world-normal length.");
            }
        }
    }
}
