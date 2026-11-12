using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class V033Checks
{
    private static int passed;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
    public static async Task Run(string mine,string outputRoot)
    {
        string root=Path.GetFullPath(Path.Combine(outputRoot,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var tools=Toolchain.Discover();string fixture=Path.Combine(root,"machine.blend");
        await Toolchain.Run(tools.Blender,["--background","--factory-startup","--disable-autoexec","--python-exit-code","1","--python",Path.GetFullPath("tests/create_unified_fixture.py"),"--",fixture],Path.Combine(root,"fixture.log"));
        if(mine=="-")mine=fixture;mine=Path.GetFullPath(mine);var sourceHash=Hash(File.ReadAllBytes(fixture));
        var results=new Dictionary<string,ConversionOutput>();var pipeline=new ConversionPipeline(tools);
        async Task<ConversionOutput> Generate(string key,AssetSettings settings)
        {
            Console.WriteLine("RENDER "+key);var r=await pipeline.Generate(settings,root);GeneratedAsset.Save(r);results[key]=r;
            Check(File.Exists(r.Aseprite),key+" native frame composites, palette and alpha validated");
            File.WriteAllText(Path.Combine(root,"outputs.json"),JsonSerializer.Serialize(results,AssetSettings.JsonOptions));return r;
        }
        var s=new AssetSettings{Model=fixture,Animation=true,EndFrame=10,FrameStep=3,Outlines=true,OutlineType=OutlineStyle.VisibleComponents,RespectOutlineGroups=true};
        Check(s.GroupedOutlines==GroupedOutlineMode.Unified&&s.FitBounds==FitBoundsSource.ObjectOnly,"new settings default to Unified grouping and Object Only fit");
        foreach(var style in new[]{OutlineStyle.VisibleComponents,OutlineStyle.Selective})
        {
            var conservative=await Generate(style+"-conservative",s with{GroupedOutlines=GroupedOutlineMode.Conservative,OutlineType=style,EdgeHighlights=style==OutlineStyle.Selective});
            var unified=await Generate(style+"-unified",s with{OutlineType=style,EdgeHighlights=style==OutlineStyle.Selective});
            int removed=0;
            for(int frame=0;frame<4;frame++)
            {
                var a=Folder(conservative,frame);var b=Folder(unified,frame);
                Check(PixelsEqual(Path.Combine(a,"render.png"),Path.Combine(b,"render.png"))&&PixelsEqual(Path.Combine(a,"ids.png"),Path.Combine(b,"ids.png")),style+" pose retains geometry, materials, camera and visible masks");
                var oldOutline=Raster.Read(Path.Combine(a,"outlines.png"));var outline=Raster.Read(Path.Combine(b,"outlines.png"));
                removed+=Enumerable.Range(0,128*128).Count(n=>oldOutline.Pixels[n*4+3]!=0&&outline.Pixels[n*4+3]==0);
                var oldImage=Raster.Read(conservative.Frames[frame].Png);var image=Raster.Read(unified.Frames[frame].Png);
                Check(Enumerable.Range(0,128*128).All(n=>oldImage.Pixels[n*4+3]==image.Pixels[n*4+3]),"Unified keeps every transparent gap and never paints artificial connecting pixels");
                foreach(string layer in Directory.GetFiles(a,"layer-*.png"))Check(PixelsEqual(layer,Path.Combine(b,Path.GetFileName(layer))),"individual component artwork stays identical and editable");
                if(style==OutlineStyle.Selective)Check(PixelsEqual(Path.Combine(a,"edge-highlights.png"),Path.Combine(b,"edge-highlights.png")),"Unified retains legitimate highlight pixels exactly");
            }
            Check(removed>40,style+" visibly reduces enclosed internal contour coverage across animation");
            Check(unified.Frames.Select(f=>Hash(Raster.Read(f.Png).Pixels)).Distinct().Count()==4,"gear and conveyor animation remain visible in all four poses");
            Legacy(conservative,style);
            if(style==OutlineStyle.VisibleComponents)
            {
                Strip([Raster.Read(conservative.Png),Raster.Read(unified.Png)],Path.Combine(root,"grouping.png"));
                await AnimatedComparison(tools,conservative,unified,root);
            }
        }
        var thick=await Generate("unified-2px",s with{OutlineThickness=2});
        Check(thick.Frames.Length==4,"two-pixel Unified outlines preserve editable animation");
        var variants=new[]{("off",false,0d,.65), ("sharp",true,0d,.65), ("soft",true,.5,.65), ("weak",true,.25,.25), ("strong",true,.25,1d)};
        foreach(var view in new[]{CameraView.IsometricSE,CameraView.IsometricSW,CameraView.IsometricNE,CameraView.IsometricNW})
        {
            ConversionOutput? reference=null;var images=new List<Raster>();
            foreach(var (key,on,soft,strength) in variants)
            {
                var config=s with{View=view,Animation=view==CameraView.IsometricSE,Frame=4,GroundShadows=on,GroundShadowSoftness=soft,GroundShadowStrength=strength};
                var r=view==CameraView.IsometricSE&&key=="off"?results["VisibleComponents-unified"]:await Generate(view+"-"+key,config);
                reference??=r;images.Add(Raster.Read(r.Png));
                Check(Framing(reference)==Framing(r),view+" "+key+" keeps fit scale, shift, pivot and motion bounds unchanged");
                for(int n=0;n<r.Frames.Length;n++)Check(PixelsEqual(Path.Combine(Folder(reference,n),"render.png"),Path.Combine(Folder(r,n),"render.png"))&&PixelsEqual(Path.Combine(Folder(reference,n),"ids.png"),Path.Combine(Folder(r,n),"ids.png")),view+" "+key+" does not change machine pixels or scale");
            }
            if(view==CameraView.IsometricSE)Strip(images.ToArray(),Path.Combine(root,"shadow-variants.png"));
            var relit=await Generate(view+"-different-direction",s with{View=view,Animation=view==CameraView.IsometricSE,Frame=4,GroundShadows=true,LightAzimuth=0});
            Check(Framing(reference!)==Framing(relit)&&PixelsEqual(Path.Combine(Folder(reference!,0),"ids.png"),Path.Combine(Folder(relit,0),"ids.png")),view+" changing light/shadow direction preserves object framing");
        }
        var mineSettings=new AssetSettings{Model=mine,GroundShadows=true,GroundShadowStrength=.8,GroundShadowSoftness=.25,Outlines=true,OutlineType=OutlineStyle.VisibleComponents};
        var oldFit=await Generate("mine-previous-fit",mineSettings with{FitBounds=FitBoundsSource.ObjectAndGroundShadow});
        var noShadow=await Generate("mine-no-shadow",mineSettings with{GroundShadows=false});var newFit=await Generate("mine-object-fit",mineSettings);
        Check(Framing(noShadow)==Framing(newFit)&&PixelsEqual(Path.Combine(noShadow.Directory,"render.png"),Path.Combine(newFit.Directory,"render.png")),"128×128 mine stays the same size with shadows on/off");
        Check(Framing(oldFit)!=Framing(newFit),"shadow-inclusive fit is explicit and differs from the fixed default");
        Strip([Raster.Read(noShadow.Png),Raster.Read(oldFit.Png),Raster.Read(newFit.Png)],Path.Combine(root,"shadow-fit.png"));
        var fixedOff=await Generate("fixed-density-off",s with{Animation=false,Frame=4,WorldScale=true,PixelsPerUnit=16});
        var fixedOn=await Generate("fixed-density-on",s with{Animation=false,Frame=4,WorldScale=true,PixelsPerUnit=16,GroundShadows=true});
        Check(Framing(fixedOff)==Framing(fixedOn),"fixed-density framing ignores ground shadows in Object Only mode");
        var isoOff=await Generate("true-iso-off",s with{Animation=false,Frame=4,Projection=Projection.TrueIsometric,View=CameraView.IsometricNE});
        var isoOn=await Generate("true-iso-on",s with{Animation=false,Frame=4,Projection=Projection.TrueIsometric,View=CameraView.IsometricNE,GroundShadows=true});
        Check(Framing(isoOff)==Framing(isoOn),"true-isometric framing remains stable with ground shadows");
        Check(sourceHash==Hash(File.ReadAllBytes(fixture)),"source geometry, animation and .blend file remain byte-identical");
        UnitContracts();UiContracts(root,s);
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=passed,results},AssetSettings.JsonOptions));Console.WriteLine($"{passed} v0.3.3 checks passed. Results: {root}");
    }
    private static void UnitContracts()
    {
        var bytes=new byte[32*32*4];var ids=new int[32*32];
        for(int y=4;y<28;y++)for(int x=4;x<28;x++)if(x<6||x>=26||y<6||y>=26){int n=y*32+x;ids[n]=10;bytes[n*4]=56;bytes[n*4+1]=84;bytes[n*4+2]=121;bytes[n*4+3]=255;}
        for(int y=12;y<21;y++)for(int x=12;x<21;x++){int n=y*32+x;ids[n]=20;bytes[n*4]=176;bytes[n*4+1]=175;bytes[n*4+2]=165;bytes[n*4+3]=255;}
        var source=new PixelResult(new(32,32,bytes),ids,[],OutlineIds:ids.Select(i=>i==20?10:i).ToArray(),GroupKeys:[10]);var s=new AssetSettings{Width=32,Height=32,Outlines=true,RespectOutlineGroups=true,OutlineType=OutlineStyle.VisibleComponents};
        var old=OutlineEngine.Apply(source,s with{GroupedOutlines=GroupedOutlineMode.Conservative});var current=OutlineEngine.Apply(source,s);
        Check(old.OutlineLayer!.Pixels[(12*32+12)*4+3]==255&&current.OutlineLayer!.Pixels[(12*32+12)*4+3]==0,"an enclosed gear loses its separate outline while retaining its artwork");
        Check(current.Image.Pixels[(10*32+10)*4+3]==0&&current.OutlineLayer!.Pixels[(4*32+10)*4+3]==255,"real gaps stay transparent and the shared exterior remains outlined");
        var disabled=OutlineEngine.Apply(source,s with{RespectOutlineGroups=false});Check(disabled.Image.Pixels.SequenceEqual(old.Image.Pixels),"Unified classification is inactive when respecting groups is disabled");
        var b=new byte[8*8*4];var labels=new int[64];for(int y=1;y<3;y++)for(int x=1;x<3;x++){int n=y*8+x;b[n*4+3]=255;labels[n]=1;}for(int y=5;y<7;y++)for(int x=5;x<7;x++){int n=y*8+x;b[n*4+3]=255;labels[n]=2;}
        var separated=new PixelResult(new(8,8,b),labels,[],OutlineIds:labels.Select(i=>i>0?1:0).ToArray(),GroupKeys:[1]);
        Check(GroupSilhouette.InternalEdges(separated,s)!.All(v=>!v),"large open gaps between disconnected parts retain their exterior boundaries");
    }
    private static void UiContracts(string root,AssetSettings settings)
    {
        Exception? failure=null;var thread=new Thread(()=>
        {
            string? previous=Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE");Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"profile"));
            try
            {
                var app=new PixelForge.Desktop.App();app.InitializeComponent();var window=new MainWindow();
                var fit=(ComboBox)window.FindName("FitBoundsInput");var group=(ComboBox)window.FindName("GroupedModeInput");
                Check((FitBoundsSource)fit.SelectedValue==FitBoundsSource.ObjectOnly&&(GroupedOutlineMode)group.SelectedItem==GroupedOutlineMode.Unified,"desktop defaults expose Object Only and Unified");
                fit.SelectedValue=FitBoundsSource.ObjectAndGroundShadow;group.SelectedItem=GroupedOutlineMode.Conservative;window.Close();
                var reopened=new MainWindow();Check((FitBoundsSource)((ComboBox)reopened.FindName("FitBoundsInput")).SelectedValue==FitBoundsSource.ObjectAndGroundShadow&&(GroupedOutlineMode)((ComboBox)reopened.FindName("GroupedModeInput")).SelectedItem==GroupedOutlineMode.Conservative,"framing/group modes persist without altering UI presentation preferences");reopened.Close();app.Shutdown();
            }
            catch(Exception e){failure=e;}finally{Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",previous);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw new Exception("UI checks failed",failure);
    }
    private static void Legacy(ConversionOutput output,OutlineStyle style)
    {
        string? path=Environment.GetEnvironmentVariable("PIXELFORGE_LEGACY_CORE");if(string.IsNullOrEmpty(path)){Console.WriteLine("SKIP: optional previous compiled renderer (set PIXELFORGE_LEGACY_CORE)");return;}
        var context=new AssemblyLoadContext("v032",true);var asm=context.LoadFromAssemblyPath(Path.GetFullPath(path));
        var rt=asm.GetType("PixelForge.Core.Raster")!;var st=asm.GetType("PixelForge.Core.AssetSettings")!;var pt=asm.GetType("PixelForge.Core.PixelEngine")!;
        string folder=Folder(output,0);var settings=st.GetMethod("Load")!.Invoke(null,[Path.Combine(output.Directory,"asset.settings.json")]);
        using var names=JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Directory,"model.json")));var ids=names.RootElement.GetProperty("objects").EnumerateObject().Select(p=>int.Parse(p.Name)).ToArray();
        using var groups=JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Directory,"outline-groups.json")));var map=groups.RootElement.GetProperty("objects").EnumerateObject().ToDictionary(p=>int.Parse(p.Name),p=>p.Value.GetProperty("groupId").GetInt32());
        object Read(string name)=>rt.GetMethod("Read")!.Invoke(null,[Path.Combine(folder,name)])!;
        object? surface=style==OutlineStyle.Selective?asm.GetType("PixelForge.Core.SurfaceFrame")!.GetMethod("Read")!.Invoke(null,[Path.Combine(folder,"surfaces.json"),settings]):null;
        var result=pt.GetMethod("Convert")!.Invoke(null,[Read("render.png"),Read("ids.png"),settings,ids,surface,map])!;var image=result.GetType().GetProperty("Image")!.GetValue(result);
        Check(((byte[])rt.GetProperty("Pixels")!.GetValue(image)!).SequenceEqual(Raster.Read(output.Png).Pixels),style+" Conservative exactly matches v0.3.2 compiled grouping");context.Unload();
    }
    private static string Folder(ConversionOutput r,int n)=>Path.GetDirectoryName(r.Frames[n].Png)!;
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static bool PixelsEqual(string a,string b)=>Raster.Read(a).Pixels.SequenceEqual(Raster.Read(b).Pixels);
    private static string Framing(ConversionOutput r)
    {
        using var frames=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"frames.json")));var f=frames.RootElement;
        return f.GetProperty("ortho_scale").ToString()+"|"+f.GetProperty("ground_pivot").ToString()+"|"+string.Join('|',f.GetProperty("frames").EnumerateArray().Select(e=>e.GetProperty("camera_shift").ToString()));
    }
    private static void Strip(Raster[] images,string path)
    {
        int w=images.Sum(i=>i.Width),h=images.Max(i=>i.Height);var bytes=new byte[w*h*4];int offset=0;
        foreach(var image in images){for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){int a=(y*image.Width+x)*4,b=(y*w+offset+x)*4;if(image.Pixels[a+3]>0)Array.Copy(image.Pixels,a,bytes,b,4);else{bytes[b]=55;bytes[b+1]=75;bytes[b+2]=62;bytes[b+3]=255;}}offset+=image.Width;}
        var r=new Raster(w,h,bytes);r.Save(path);r.Enlarge(4).Save(Path.ChangeExtension(path,null)+"-4x.png");
    }
    private static async Task AnimatedComparison(Toolchain tools,ConversionOutput a,ConversionOutput b,string root)
    {
        static string Q(string p)=>JsonSerializer.Serialize(p.Replace('\\','/'));var lua=new System.Text.StringBuilder("local s=Sprite(256,128,ColorMode.RGB)\nlocal l=s.layers[1]\n");
        for(int n=0;n<a.Frames.Length;n++){if(n>0)lua.AppendLine($"s:newEmptyFrame({n+1})");lua.AppendLine($"local i=Image(256,128,ColorMode.RGB)\ni:drawImage(Image{{fromFile={Q(a.Frames[n].Png)}}},Point(0,0))\ni:drawImage(Image{{fromFile={Q(b.Frames[n].Png)}}},Point(128,0))\ns:newCel(l,{n+1},i,Point(0,0))\ns.frames[{n+1}].duration={(a.Frames[n].DurationMs/1000d).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)}");}
        string native=Path.Combine(root,"grouping.aseprite"),script=Path.Combine(root,"comparison.lua");lua.AppendLine($"s:saveAs({Q(native)})");File.WriteAllText(script,lua.ToString());
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(root,"comparison.log"));await Toolchain.Run(tools.Aseprite,["--batch",native,"--save-as",Path.Combine(root,"grouping.gif")],Path.Combine(root,"gif.log"));
    }
}
