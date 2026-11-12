using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class GroupShadowChecks
{
    private static int count;
    public static void Measure(string path)
    {
        var outputs=JsonSerializer.Deserialize<ConversionOutput[]>(File.ReadAllText(Path.Combine(path,"outputs.json")),AssetSettings.JsonOptions)!;
        foreach(var output in outputs)
        {
            var settings=AssetSettings.Load(Path.Combine(output.Directory,"asset.settings.json"));
            var shaded=Raster.Read(Path.Combine(output.Directory,"ground-shadowed.png"));var clear=Raster.Read(Path.Combine(output.Directory,"ground-clear.png"));
            var original=PixelEngine.Convert(Raster.Read(Path.Combine(output.Directory,"render.png")),Raster.Read(Path.Combine(output.Directory,"ids.png")),settings with{GroundShadows=false});
            var shadow=GroundShadow.Create(shaded,clear,original.Image,settings)!;
            var values=new List<double>();int step=settings.SamplesPerPixel;
            static double Linear(byte b){double c=b/255d;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
            static double Luma(byte[] b,int p)=>.2126*Linear(b[p+2])+.7152*Linear(b[p+1])+.0722*Linear(b[p]);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                if(original.Image.Pixels[(y*128+x)*4+3]!=0)continue;double coverage=0;
                for(int dy=0;dy<step;dy++)for(int dx=0;dx<step;dx++){int p=((y*step+dy)*shaded.Width+x*step+dx)*4;double baseline=Luma(clear.Pixels,p);if(baseline>=.005)coverage+=Math.Clamp(1-Luma(shaded.Pixels,p)/baseline,0,1);}
                coverage/=step*step;if(coverage*settings.GroundShadowStrength>=.18)values.Add(coverage);
            }
            var colors=Enumerable.Range(0,128*128).Where(n=>shadow.Pixels[n*4+3]!=0).GroupBy(n=>PixelEngine.ColorAt(shadow.Pixels,n*4)).Select(g=>new{color=g.Key.ToString("X6"),pixels=g.Count()});
            Console.WriteLine(JsonSerializer.Serialize(new{settings.GroundShadowSoftness,coverageMin=values.Min(),coverageMax=values.Max(),colors},AssetSettings.JsonOptions));
            string preview=Path.Combine(path,"measured-"+settings.GroundShadowSoftness.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png");
            if(!File.Exists(preview))Strip([GroundShadow.Composite(original.Image,shadow)],preview);
        }
    }
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    public static async Task Run(string mine,string outputRoot)
    {
        string root=Path.GetFullPath(Path.Combine(outputRoot,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var tools=Toolchain.Discover();string fixture=Path.Combine(root,"grouped-machine.blend");
        await Toolchain.Run(tools.Blender,["--background","--factory-startup","--disable-autoexec","--python-exit-code","1","--python",Path.GetFullPath("tests/create_grouped_fixture.py"),"--",fixture],Path.Combine(root,"fixture.log"));
        var original=SHA256.HashData(File.ReadAllBytes(fixture));
        using(var audit=JsonDocument.Parse(File.ReadAllText(fixture+".audit.json")))
        {
            Check(audit.RootElement.EnumerateArray().Select(f=>f.GetProperty("body").ToString()).Distinct().Count()==1,"fixture body transforms remain stationary");
            Check(audit.RootElement.EnumerateArray().Select(f=>f.GetProperty("gearRotation").ToString()).Distinct().Count()==4,"separate gear has independent keyframed rotation");
        }
        var outputs=new Dictionary<string,ConversionOutput>();var pipeline=new ConversionPipeline(tools);
        var settings=new AssetSettings{Model=fixture,Animation=true,EndFrame=10,FrameStep=3,Outlines=true};
        foreach(var style in new[]{OutlineStyle.VisibleComponents,OutlineStyle.Selective})foreach(int thickness in new[]{1,2})
        {
            var s=settings with{OutlineType=style,OutlineThickness=thickness,EdgeHighlights=style==OutlineStyle.Selective};
            string key=style+"-"+thickness;Console.WriteLine("RENDER "+key);
            var off=await pipeline.Generate(s,root);var on=await pipeline.Generate(s with{RespectOutlineGroups=true,GroupedOutlines=GroupedOutlineMode.Conservative},root);GeneratedAsset.Save(off);GeneratedAsset.Save(on);
            outputs[key+"-off"]=off;outputs[key+"-on"]=on;
            Check(on.Frames.Length==4&&on.Frames.Select(f=>f.SourceFrame).SequenceEqual(new[]{1,4,7,10}),key+" preserves frame selection and native composites");
            Check(on.Warnings.Any(w=>w.Contains("ambiguity")&&w.Contains("Right support")),key+" clearly reports competing collection membership");
            using var namesJson=JsonDocument.Parse(File.ReadAllText(Path.Combine(on.Directory,"model.json")));var names=namesJson.RootElement.GetProperty("objects");
            var ids=names.EnumerateObject().Select(p=>int.Parse(p.Name)).ToArray();int Id(string name)=>int.Parse(names.EnumerateObject().Single(p=>p.Value.GetString()==name).Name);
            using var groups=JsonDocument.Parse(File.ReadAllText(Path.Combine(on.Directory,"outline-groups.json")));
            var map=groups.RootElement.GetProperty("objects").EnumerateObject().ToDictionary(p=>int.Parse(p.Name),p=>p.Value.GetProperty("groupId").GetInt32());
            Check(map[Id("Animated gear")]==map[Id("Static wooden machine base")]&&map[Id("Asymmetric rotating marker")]==map[Id("Animated gear")],key+" nested collection and parented objects share a contour identity");
            Check(map[Id("Armature-driven part")]!=map[Id("Animated gear")]&&!map.ContainsKey(Id("Ungrouped block")),key+" separate groups and ungrouped objects keep separate identities");
            int suppressed=0;var outerMasks=new List<string>();
            for(int i=0;i<4;i++)
            {
                string a=Path.GetDirectoryName(off.Frames[i].Png)!,b=Path.GetDirectoryName(on.Frames[i].Png)!;
                Check(Equal(Path.Combine(a,"render.png"),Path.Combine(b,"render.png"))&&Equal(Path.Combine(a,"ids.png"),Path.Combine(b,"ids.png")),key+$" frame {i+1} beauty/materials/object masks unchanged");
                var sg=s with{RespectOutlineGroups=true,GroupedOutlines=GroupedOutlineMode.Conservative};var surfaces=style==OutlineStyle.Selective?SurfaceFrame.Read(Path.Combine(b,"surfaces.json"),sg):null;
                var result=PixelEngine.Convert(Raster.Read(Path.Combine(b,"render.png")),Raster.Read(Path.Combine(b,"ids.png")),sg,ids,surfaces,map);
                var oldLayer=Raster.Read(Path.Combine(a,"outlines.png"));var newLayer=Raster.Read(Path.Combine(b,"outlines.png"));
                var image=Raster.Read(on.Frames[i].Png);var baseImage=result.BaseImage!;
                for(int n=0;n<result.ObjectIds.Length;n++)
                {
                    if(baseImage.Pixels[n*4+3]==0){if(image.Pixels[n*4+3]!=0)throw new Exception("Grouping introduced an artificial pixel bridge");continue;}
                    if(oldLayer.Pixels[n*4+3]!=0&&newLayer.Pixels[n*4+3]==0)suppressed++;
                }
                Check(result.ObjectIds.SequenceEqual(PixelEngine.Convert(Raster.Read(Path.Combine(a,"render.png")),Raster.Read(Path.Combine(a,"ids.png")),s,ids,surfaces).ObjectIds),key+$" frame {i+1} retains object identity for editable artwork");
                Check(Enumerable.Range(0,128*128).Where(n=>result.ObjectIds[n]==Id("Ungrouped block")).All(n=>oldLayer.Pixels.AsSpan(n*4,4).SequenceEqual(newLayer.Pixels.AsSpan(n*4,4))),key+" ungrouped object retains its existing outline pixels");
                if(style==OutlineStyle.VisibleComponents)
                {
                    bool boundaries=true;int boundaryCount=0;
                    for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                    {
                        int n=y*128+x;if(baseImage.Pixels[n*4+3]==0)continue;
                        foreach(var (dx,dy) in new[]{(-1,0),(1,0),(0,-1),(0,1)})
                        {
                            int nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=128||ny>=128)continue;int j=ny*128+nx;
                            if(baseImage.Pixels[j*4+3]==0){boundaries&=newLayer.Pixels[n*4+3]!=0;continue;}
                            if(result.ContourIds[n]>result.ContourIds[j]){boundaryCount++;boundaries&=newLayer.Pixels[n*4+3]!=0;}
                        }
                    }
                    Check(boundaries&&boundaryCount>0,key+" preserves visible external and distinct-group boundaries");
                }
                Check(!File.Exists(Path.Combine(b,$"layer-{Id("Hidden group member")}.png")),key+" hidden group geometry generates no artwork");
                if(style==OutlineStyle.Selective)
                {
                    var hlOff=Raster.Read(Path.Combine(a,"edge-highlights.png"));var hlOn=Raster.Read(Path.Combine(b,"edge-highlights.png"));
                    Check(Enumerable.Range(0,128*128).All(n=>hlOff.Pixels[n*4+3]==0||hlOn.Pixels[n*4+3]!=0),key+" grouping does not remove legitimate material/edge highlights");
                }
                outerMasks.Add(Hash(newLayer.Pixels));
            }
            Check(suppressed>10,key+" suppresses internal outlines between grouped components");
            Check(outerMasks.Distinct().Count()>1,key+" contour is recomputed for moving geometry each frame");
            string gearLayer="layer-"+Id("Asymmetric rotating marker")+".png";
            Check(on.Frames.Select(f=>Hash(Raster.Read(Path.Combine(Path.GetDirectoryName(f.Png)!,gearLayer)).Pixels)).Distinct().Count()>1,key+" rotating parented artwork remains animated");
            LegacyCheck(off,s,ids);
            if(style==OutlineStyle.VisibleComponents&&thickness==1)
            {
                Strip([Raster.Read(off.Png),Raster.Read(on.Png)],Path.Combine(root,"groups-first-frame.png"));
                await AnimationComparison(tools,off,on,root);
            }
        }
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))),"original grouped Blender file remains byte-identical");
        var native=outputs["VisibleComponents-1-on"];var nativeHash=SHA256.HashData(File.ReadAllBytes(native.Aseprite));
        string nativeScript=Path.Combine(root,"verify-native-editability.lua"),editableCopy=Path.Combine(root,"editable-proof.aseprite");
        string Q(string p)=>JsonSerializer.Serialize(p.Replace('\\','/'));
        File.WriteAllText(nativeScript,$"local s=app.open({Q(native.Aseprite)})\nassert(#s.frames==4)\nlocal layers={{}}\nfor _,l in ipairs(s.layers) do layers[l.name]=l end\nassert(layers['Static wooden machine base'] and layers['Animated gear'] and layers['Grouped Outlines'])\nfor i=1,4 do assert(layers['Static wooden machine base']:cel(i)); assert(layers['Animated gear']:cel(i)) end\nlocal image=Image(s.width,s.height,ColorMode.RGB)\nimage:drawPixel(0,0,app.pixelColor.rgba(23,25,35,255))\ns:newCel(layers['Manual corrections'],2,image,Point(0,0))\ns:saveCopyAs({Q(editableCopy)})\n");
        await Toolchain.Run(tools.Aseprite,["--batch","--script",nativeScript],Path.Combine(root,"native-editability.log"));
        Check(File.Exists(editableCopy)&&nativeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(native.Aseprite))),"real Aseprite keeps separate body/gear cels, grouped outline layer, and manual edits in a new copy");
        string probeRoot=Path.Combine(root,"softness");await Probe(mine,probeRoot);
        var probe=JsonSerializer.Deserialize<ConversionOutput[]>(File.ReadAllText(Path.Combine(probeRoot,"outputs.json")),AssetSettings.JsonOptions)!;
        var softSettings=AssetSettings.Load(Path.Combine(probe[1].Directory,"asset.settings.json"));
        var shadows=probe.Select(o=>Raster.Read(Path.Combine(o.Directory,"ground-shadow.png"))).ToArray();
        Check(probe.Select(o=>Hash(Raster.Read(Path.Combine(o.Directory,"render.png")).Pixels)).Distinct().Count()==1,"softness 0 / 0.25 / 0.5 keeps object lighting, scale and framing pixel-identical");
        Check(shadows.Select(r=>Hash(r.Pixels)).Distinct().Count()==3,"minimum, midpoint and maximum produce visibly different mine shadow pixels");
        Check(Shades(shadows[0])==1&&Shades(shadows[1])>1&&Shades(shadows[2])>1,"soft shadows retain discrete palette penumbra instead of one flat hard shade");
        foreach(var r in probe){PixelEngine.Validate(Raster.Read(r.Png),AssetSettings.Load(Path.Combine(r.Directory,"asset.settings.json")));}
        Check(true,"all mine softness variants obey palette, binary alpha and exact native compositing");
        Strip(probe.Select(o=>Raster.Read(o.Png)).ToArray(),Path.Combine(root,"mine-softness.png"));Strip(shadows,Path.Combine(root,"shadow-only.png"));
        if(File.Exists("outputs/groups-shadow/before/outputs.json"))
        {
            var before=JsonSerializer.Deserialize<ConversionOutput[]>(File.ReadAllText("outputs/groups-shadow/before/outputs.json"),AssetSettings.JsonOptions)!;
            Strip(before.Select(o=>Raster.Read(o.Png)).ToArray(),Path.Combine(root,"before-softness.png"));
        }
        var weaker=await pipeline.Generate(softSettings with{GroundShadowStrength=.4},root);outputs["strength-40"]=weaker;
        Check(Equal(Path.Combine(weaker.Directory,"ground-shadowed.png"),Path.Combine(probe[1].Directory,"ground-shadowed.png")),"strength remains independent of 3D softness/receiver coverage");
        Check(!Equal(Path.Combine(weaker.Directory,"ground-shadow.png"),Path.Combine(probe[1].Directory,"ground-shadow.png")),"strength independently changes final palette shadow coverage/shade");
        var saved=Path.Combine(root,"softness.settings.json");softSettings.Save(saved);var repeat=await pipeline.Generate(AssetSettings.Load(saved),root);outputs["softness-settings-repeat"]=repeat;
        Check(Equal(Path.Combine(repeat.Directory,"ground-shadow.png"),Path.Combine(probe[1].Directory,"ground-shadow.png")),"softness survives settings save/load and deterministic regeneration");
        var singlePalette=softSettings with{Colors=["#171923"]};var warn=new List<string>();var modelMask=PixelEngine.Convert(Raster.Read(Path.Combine(probe[1].Directory,"render.png")),Raster.Read(Path.Combine(probe[1].Directory,"ids.png")),softSettings with{GroundShadows=false});
        GroundShadow.Create(Raster.Read(Path.Combine(probe[1].Directory,"ground-shadowed.png")),Raster.Read(Path.Combine(probe[1].Directory,"ground-clear.png")),modelMask.Image,singlePalette,warn);
        Check(warn.Any(w=>w.Contains("one shadow shade")),"palette/resolution limitations produce an explicit softness warning");
        Sta(()=>NumericChecks(root,softSettings));
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,outputs,softness=probe},AssetSettings.JsonOptions));Console.WriteLine($"{count} collection/shadow/precision checks passed. Results: {root}");
    }
    private static void LegacyCheck(ConversionOutput output,AssetSettings settings,int[] ids)
    {
        string? baseline=Environment.GetEnvironmentVariable("PIXELFORGE_LEGACY_CORE");if(string.IsNullOrEmpty(baseline)){Console.WriteLine("SKIP: optional compiled legacy comparison; set PIXELFORGE_LEGACY_CORE.");return;}
        var context=new AssemblyLoadContext("legacy-"+Guid.NewGuid(),true);var asm=context.LoadFromAssemblyPath(Path.GetFullPath(baseline));
        var rt=asm.GetType("PixelForge.Core.Raster")!;var st=asm.GetType("PixelForge.Core.AssetSettings")!;var pt=asm.GetType("PixelForge.Core.PixelEngine")!;
        string folder=Path.GetDirectoryName(output.Frames[0].Png)!;var oldSettings=st.GetMethod("Load")!.Invoke(null,[Path.Combine(output.Directory,"asset.settings.json")]);
        var raw=rt.GetMethod("Read")!.Invoke(null,[Path.Combine(folder,"render.png")]);var idsRaster=rt.GetMethod("Read")!.Invoke(null,[Path.Combine(folder,"ids.png")]);
        object? surface=settings.OutlineType==OutlineStyle.Selective?asm.GetType("PixelForge.Core.SurfaceFrame")!.GetMethod("Read")!.Invoke(null,[Path.Combine(folder,"surfaces.json"),oldSettings]):null;
        var oldResult=pt.GetMethod("Convert")!.Invoke(null,[raw,idsRaster,oldSettings,ids,surface]);var image=oldResult!.GetType().GetProperty("Image")!.GetValue(oldResult);
        Check(((byte[])rt.GetProperty("Pixels")!.GetValue(image)!).SequenceEqual(Raster.Read(output.Png).Pixels),settings.OutlineType+" / "+settings.OutlineThickness+" grouping disabled matches previous compiled renderer exactly");context.Unload();
    }
    private static void NumericChecks(string root,AssetSettings settings)
    {
        foreach(var (value,text) in new[]{(.521312312,"0.52"),(1.234567,"1.23"),(16.5,"16.5"),(2d,"2"),(.008,"0.01")})Check(UiNumbers.Format(value)==text,"clean display "+text);
        var c=new NumericSlider{Minimum=0,Maximum=1,Value=.052131312,NumericMode=true};double original=c.Value;Check(c.Editor.Text=="0.05","numeric slider displays maximum two decimals");
        for(int i=0;i<6;i++){c.NumericMode=!c.NumericMode;Check(c.Commit()&&c.Value==original,"presentation switch preserves internal precision");}
        c.NumericMode=true;c.Editor.Text="0.008";Check(!c.Commit()&&c.Value==original,"fractional control rejects more than two entered decimals");c.CancelEdit();
        c.Editor.Text="0.25";Check(c.Commit()&&c.Value==.25,"ground softness accepts a valid fractional value without integer truncation");
        var percent=new NumericSlider{Minimum=0,Maximum=1,DisplayScale=100,DecimalPlaces=0,Value=.651231,NumericMode=true};Check(percent.Editor.Text=="65"&&percent.Commit()&&percent.Value==.651231,"integer percent display preserves precise original parameter");
        percent.Editor.Text="82.5";Check(!percent.Commit(),"integer-only percentage rejects fractional entry");percent.CancelEdit();percent.Editor.Text="82";Check(percent.Commit()&&percent.Value==.82,"integer percent accepts exact supported input");
        var previous=Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE");Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"profile"));
        var app=new PixelForge.Desktop.App();app.InitializeComponent();
        try
        {
            var window=new MainWindow();var apply=typeof(MainWindow).GetMethod("ApplySettings",BindingFlags.NonPublic|BindingFlags.Instance)!;var read=typeof(MainWindow).GetMethod("ReadSettings",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var precise=settings with{Padding=.081231231,PixelsPerUnit=16.5123123,GroundShadowSoftness=.052131312,GroundShadowStrength=.651231,RespectOutlineGroups=true,GroupedOutlines=GroupedOutlineMode.Conservative};apply.Invoke(window,[precise]);
            var box=(NumericSlider)window.FindName("GroundSoftnessInput");box.NumericMode=true;
            var s=(AssetSettings)read.Invoke(window,null)!;
            Check(s.GroundShadowSoftness==precise.GroundShadowSoftness&&s.Padding==precise.Padding&&s.PixelsPerUnit==precise.PixelsPerUnit,"settings extraction does not round displayed source precision");
            Check(((TextBox)window.FindName("PaddingInput")).Text=="0.08"&&((TextBox)window.FindName("PixelsPerUnitInput")).Text=="16.51","ordinary fractional fields also show clean two-decimal values");
            box.Editor.Text="0.25";Check(box.Commit(),"actual window softness numeric binding commits");s=(AssetSettings)read.Invoke(window,null)!;
            Check(s.GroundShadowSoftness==.25&&s.GroundShadowStrength==precise.GroundShadowStrength,"softness UI binding changes softness only, preserving strength");window.Close();
            var reopened=new MainWindow();var restored=(AssetSettings)read.Invoke(reopened,null)!;
            Check(restored.GroundShadowSoftness==.25&&restored.GroundShadowStrength==precise.GroundShadowStrength&&restored.RespectOutlineGroups,"restart preserves softness, independent strength and collection option");
            Check(((NumericSlider)reopened.FindName("GroundSoftnessInput")).NumericMode,"softness numeric presentation mode persists after restart");reopened.Close();
        }
        finally{app.Shutdown();Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",previous);}
    }
    private static void Sta(Action action){Exception? failure=null;var thread=new Thread(()=>{try{action();}catch(Exception e){failure=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw new Exception("Numeric UI checks failed",failure);}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Equal(string a,string b)=>Raster.Read(a).Pixels.SequenceEqual(Raster.Read(b).Pixels);
    private static int Shades(Raster r)=>Enumerable.Range(0,r.Width*r.Height).Where(n=>r.Pixels[n*4+3]!=0).Select(n=>PixelEngine.ColorAt(r.Pixels,n*4)).Distinct().Count();
    private static void Strip(Raster[] images,string path)
    {
        int w=images.Sum(i=>i.Width),h=images.Max(i=>i.Height);var bytes=new byte[w*h*4];int offset=0;
        foreach(var image in images)
        {
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            {int a=(y*image.Width+x)*4,b=(y*w+x+offset)*4;if(image.Pixels[a+3]!=0)Array.Copy(image.Pixels,a,bytes,b,4);else{bytes[b]=55;bytes[b+1]=75;bytes[b+2]=62;bytes[b+3]=255;}}
            offset+=image.Width;
        }
        var r=new Raster(w,h,bytes);r.Save(path);r.Enlarge(4).Save(Path.ChangeExtension(path,null)+"-4x.png");
    }
    private static async Task AnimationComparison(Toolchain tools,ConversionOutput a,ConversionOutput b,string root)
    {
        static string Q(string p)=>JsonSerializer.Serialize(p.Replace('\\','/'));
        var lua=new System.Text.StringBuilder("local s=Sprite(256,128,ColorMode.RGB)\nlocal layer=s.layers[1]\n");
        for(int n=0;n<a.Frames.Length;n++)
        {
            if(n>0)lua.AppendLine($"s:newEmptyFrame({n+1})");
            lua.AppendLine($"local image=Image(256,128,ColorMode.RGB)\nimage:drawImage(Image{{fromFile={Q(a.Frames[n].Png)}}},Point(0,0))\nimage:drawImage(Image{{fromFile={Q(b.Frames[n].Png)}}},Point(128,0))\ns:newCel(layer,{n+1},image,Point(0,0))\ns.frames[{n+1}].duration=0.083");
        }
        string native=Path.Combine(root,"groups-comparison.aseprite");lua.AppendLine($"s:saveAs({Q(native)})");string script=Path.Combine(root,"comparison.lua");File.WriteAllText(script,lua.ToString());
        await Toolchain.Run(tools.Aseprite,["--batch","--script",script],Path.Combine(root,"comparison.log"));await Toolchain.Run(tools.Aseprite,["--batch",native,"--save-as",Path.Combine(root,"groups-comparison.gif")],Path.Combine(root,"gif.log"));
    }
    public static async Task Probe(string model,string root)
    {
        Directory.CreateDirectory(root);var results=new List<ConversionOutput>();
        foreach(double softness in new[]{0d,.25,.5})
        {
            var s=new AssetSettings{Model=Path.GetFullPath(model),GroundShadows=true,GroundShadowStrength=.8,GroundShadowSoftness=softness};
            var r=await new ConversionPipeline(Toolchain.Discover()).Generate(s,root);GeneratedAsset.Save(r);results.Add(r);Console.WriteLine($"Softness {softness}: {r.Directory}");
        }
        File.WriteAllText(Path.Combine(root,"outputs.json"),JsonSerializer.Serialize(results,AssetSettings.JsonOptions));
    }
}
