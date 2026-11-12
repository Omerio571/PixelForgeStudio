using System.IO;
using System.Numerics;
using System.Runtime.Loader;
using System.Text.Json;
using PixelForge.Core;

internal static class EdgeRegression
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    public static async Task Run(string root)
    {
        root=Path.GetFullPath(root);
        var outputs=JsonSerializer.Deserialize<Dictionary<string,ConversionOutput>>(File.ReadAllText(Path.Combine(root,"outputs.json")),AssetSettings.JsonOptions)!;
        var a=outputs["SE-A"];var settings=AssetSettings.Load(Path.Combine(a.Directory,"asset.settings.json"));
        // Compare with the actual pre-change assembly, not another invocation of the new code.
        string? baseline=Environment.GetEnvironmentVariable("PIXELFORGE_LEGACY_CORE");
        var rawPath=Path.Combine(a.Directory,"render.png");var idsPath=Path.Combine(a.Directory,"ids.png");
        if(!string.IsNullOrEmpty(baseline)) {
        var context=new AssemblyLoadContext("previous-renderer",true);
        var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(baseline));
        var rasterType=assembly.GetType("PixelForge.Core.Raster")!;var settingsType=assembly.GetType("PixelForge.Core.AssetSettings")!;var pixelType=assembly.GetType("PixelForge.Core.PixelEngine")!;

        using var model=JsonDocument.Parse(File.ReadAllText(Path.Combine(a.Directory,"model.json")));
        var ids=model.RootElement.GetProperty("objects").EnumerateObject().Select(p=>int.Parse(p.Name)).ToArray();
        foreach(var mode in new[]{OutlineStyle.OuterSilhouette,OutlineStyle.VisibleComponents})foreach(bool enabled in new[]{false,true})foreach(int thickness in new[]{1,2})
        {
            var s=settings with{Outlines=enabled,OutlineType=mode,OutlineThickness=thickness};var path=Path.Combine(root,"legacy-input.json");s.Save(path);
            var oldSettings=settingsType.GetMethod("Load")!.Invoke(null,[path]);
            var oldRaw=rasterType.GetMethod("Read")!.Invoke(null,[rawPath]);var oldIds=rasterType.GetMethod("Read")!.Invoke(null,[idsPath]);
            var method=pixelType.GetMethod("Convert")!;var parameters=new object?[method.GetParameters().Length];parameters[0]=oldRaw;parameters[1]=oldIds;parameters[2]=oldSettings;parameters[3]=ids;
            var oldResult=method.Invoke(null,parameters);
            var oldImage=oldResult!.GetType().GetProperty("Image")!.GetValue(oldResult);
            var oldPixels=(byte[])rasterType.GetProperty("Pixels")!.GetValue(oldImage)!;
            var current=PixelEngine.Convert(Raster.Read(rawPath),Raster.Read(idsPath),s,ids);
            Check(oldPixels.SequenceEqual(current.Image.Pixels),$"previous assembly pixel-identical: {mode}, enabled {enabled}, thickness {thickness}");
        }
        context.Unload();
        } else Console.WriteLine("SKIP: optional compiled legacy comparison; set PIXELFORGE_LEGACY_CORE.");
        var oldSettingsFile=Environment.GetEnvironmentVariable("PIXELFORGE_LEGACY_SETTINGS");
        if(!string.IsNullOrEmpty(oldSettingsFile)) {
        var old=AssetSettings.Load(oldSettingsFile);Check(!old.EdgeHighlights&&old.OutlineType!=OutlineStyle.Selective,"old saved settings leave new effects disabled");
        Check(Raster.Read(rawPath).Pixels.SequenceEqual(Raster.Read(Path.Combine(Path.GetDirectoryName(oldSettingsFile)!,"render.png")).Pixels),"same mine beauty render identical to saved v0.3 output");
        }
        var s0=new AssetSettings{Width=128,Height=128,SamplesPerPixel=1,EdgeHighlights=true,EdgeHighlightStrength=.5,Colors=["#202020","#606060","#A0A0A0","#E0E0E0"]};
        (PixelResult Source,SurfaceFrame Geometry) Plane(int shift,Vector3 normal)
        {
            var bytes=new byte[128*128*4];var labels=new int[128*128];var surface=new SurfacePoint?[128*128];
            for(int y=40;y<60;y++)for(int x=40+shift;x<60+shift;x++)
            {int n=y*128+x;bytes[n*4]=bytes[n*4+1]=bytes[n*4+2]=96;bytes[n*4+3]=255;labels[n]=1;surface[n]=new(1,normal,10,1,.2,1);}
            return(new(new(128,128,bytes),labels,[]),new(128,128,1,Vector3.UnitX,surface));
        }
        var plane=Plane(0,Vector3.UnitX);var lit=EdgeEffects.Apply(plane.Source,s0,plane.Geometry);
        var shadow=EdgeEffects.Apply(plane.Source,s0,plane.Geometry with{Light=-Vector3.UnitX});
        Check(Opaque(lit.HighlightLayer!)>0&&Opaque(shadow.HighlightLayer!)==0,"light direction, not top/left image position, controls edge highlights");
        Check(lit.HighlightLayer!.Pixels[(50*128+50)*4+3]==0,"uniform internal mesh surface gets no polygon/fill highlight");
        var translated=Plane(7,Vector3.UnitX);var moved=EdgeEffects.Apply(translated.Source,s0,translated.Geometry);
        Check(Enumerable.Range(0,128*128-7).All(n=>lit.HighlightLayer.Pixels[n*4+3]==moved.HighlightLayer!.Pixels[(n+7)*4+3]),"highlight mask follows moving geometry rather than screen-space pattern");
        var perturbed=Plane(0,Vector3.Normalize(new(1,.001f,0)));var stable=EdgeEffects.Apply(perturbed.Source,s0,perturbed.Geometry);
        Check(lit.Image.Pixels.SequenceEqual(stable.Image.Pixels),"tiny normal changes do not flicker flat confident edge runs");
        var blocked=plane.Geometry with{Pixels=plane.Geometry.Pixels.Select(p=>p is {} v?(SurfacePoint?)(v with{KeyVisibility=0}):null).ToArray()};
        Check(Opaque(EdgeEffects.Apply(plane.Source,s0,blocked).HighlightLayer!)==0,"occluded key light never adds false exposed-edge glints");
        var matte=plane.Geometry with{Pixels=plane.Geometry.Pixels.Select(p=>p is {} v?(SurfacePoint?)(v with{Metallic=0,Roughness=.8}):null).ToArray()};
        Check(Opaque(EdgeEffects.Apply(plane.Source,s0 with{EdgeHighlightStrength=.3},matte).HighlightLayer!)<Opaque(lit.HighlightLayer),"matte materials avoid metallic contrast jumps");
        var pipeline=new ConversionPipeline(Toolchain.Discover());
        foreach(int size in new[]{32,64})
        {
            var smallSettings=settings with{Width=size,Height=size,OutlineType=OutlineStyle.Selective,EdgeHighlights=true};
            var small=await pipeline.Generate(smallSettings,Path.Combine(root,"small-canvas"));GeneratedAsset.Save(small);
            var layer=Raster.Read(Path.Combine(small.Directory,"edge-highlights.png"));
            Check(size==32?Opaque(layer)==0:Opaque(layer)<size*size*.04,$"{size}×{size} simplifies or omits highlight clutter");
        }
        // Native effect removal must reveal the original untouched component artwork.
        var c=outputs["SE-C"];var script=Path.Combine(root,"verify-editable-effects.lua");
        static string Q(string p)=>JsonSerializer.Serialize(p.Replace('\\','/'));
        var removed=Path.Combine(root,"native-highlights-removed.png");
        File.WriteAllText(script,$"local s=app.open({Q(c.Aseprite)})\nlocal found=false\nfor _,l in ipairs(s.layers) do if l.name=='Edge highlights' then l.isVisible=false;found=true end end\nassert(found)\nlocal image=Image(s.width,s.height,ColorMode.RGB)\nimage:drawSprite(s,1,Point(0,0))\nimage:saveAs({Q(removed)})\n");
        await Toolchain.Run(Toolchain.Discover().Aseprite,["--batch","--script",script],Path.Combine(root,"verify-editable-effects.log"));
        Check(Raster.Read(removed).Pixels.SequenceEqual(Raster.Read(outputs["SE-B"].Png).Pixels),"hiding native highlight layer exactly restores selective-only artwork");
        var animation=outputs["animated"];
        var masks=animation.Frames.Select(f=>Raster.Read(Path.Combine(Path.GetDirectoryName(f.Png)!,"edge-highlights.png"))).ToArray();
        Check(masks.Select(m=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(m.Pixels))).Distinct().Count()>1,"actual animated highlight positions change with evaluated poses");
        using var frames=JsonDocument.Parse(File.ReadAllText(Path.Combine(animation.Directory,"frames.json")));
        Check(frames.RootElement.GetProperty("frames").EnumerateArray().Select(f=>f.GetProperty("camera_shift").ToString()).Distinct().Count()==1,"new effects preserve shared animation camera");
        File.WriteAllText(Path.Combine(root,"regression-results.json"),JsonSerializer.Serialize(new{checks=count,previousAssemblyCompared=!string.IsNullOrEmpty(baseline)},AssetSettings.JsonOptions));
        Console.WriteLine($"{count} edge regression checks passed.");
    }
    private static int Opaque(Raster r)=>Enumerable.Range(0,r.Width*r.Height).Count(n=>r.Pixels[n*4+3]==255);
}
