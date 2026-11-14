using System.IO;
using System.Security.Cryptography;
using PixelForge.Core;

if(args.Length>0&&args[0]=="--padding") { await PaddingChecks.Run(args.Length>1?args[1]:"outputs/padding"); return; }

if(args.Length>0&&args[0]=="--session-orphan") { var orphan=new RenderSession(args[1]);File.WriteAllText(Path.Combine(orphan.DirectoryPath,"registered.dat"),"application-owned");orphan.Snapshot();Environment.Exit(0); }

if(args.Length>0&&args[0]=="--workflow") { await WorkflowChecks.Run(args[1],args.Length>2?args[2]:"outputs/v034"); return; }

if(args.Length>0&&args[0]=="--v033") { await V033Checks.Run(args.Length>1?args[1]:"-",args.Length>2?args[2]:"outputs/v033"); return; }

if(args.Length>0&&args[0]=="--shadow-probe") { await GroupShadowChecks.Probe(args[1],args[2]); return; }
if(args.Length>0&&args[0]=="--shadow-measure") { GroupShadowChecks.Measure(args[1]); return; }
if(args.Length>0&&args[0]=="--groups-shadow") { await GroupShadowChecks.Run(args[1],args.Length>2?args[2]:"outputs/groups-shadow/verified"); return; }

if(args.Length>0&&args[0]=="--usability") { await UsabilityChecks.Run(args[1],args[2],args.Length>3?args[3]:"outputs/usability"); return; }

if(args.Length>0&&args[0]=="--edges") { await EdgeChecks.Run(args[1],args[2],args.Length>3?args[3]:"outputs/edges"); return; }
if(args.Length>0&&args[0]=="--edge-regression") { await EdgeRegression.Run(args[1]); return; }

if(args.Length>0&&args[0]=="--timing") { await TimingChecks.Run(args[1],args.Length>2?args[2]:"outputs/v031-timing"); return; }

if(args.Length>0&&args[0]=="--v03") { await V03Checks.Run(args[1],args[2],args.Length>3?args[3]:"outputs/v03"); return; }
if(args.Length>0&&args[0]=="--native-edits") { await V03Checks.CheckEdits(args[1]); return; }

if(args.Length>0&&args[0]=="--v02") { await V02Checks.Run(args[1],args.Length>2?args[2]:"outputs/v02"); return; }
if(args.Length>0&&args[0]=="--assembly") { await V02Checks.RunAssembly(args[1],args[2],args[3],args.Length>4?args[4]:"outputs/assembly"); return; }
if(args.Length>0&&args[0]=="--lighting-controls") { await V02Checks.RunLighting(args[1],args.Length>2?args[2]:"outputs/lighting-controls"); return; }

int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}catch(FormatException){Check(true,name);return;}catch(InvalidDataException){Check(true,name);return;}catch(FileNotFoundException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
Check(Palette.ParseColor("#FF8001")==new Rgb(255,128,1),"HEX parsing");
Check(Palette.ParseColor("rgb(255, 128, 1)")==new Rgb(255,128,1),"RGB parsing");
Reject(()=>Palette.ParseColor("256,0,0"),"invalid RGB");
Check(Palette.SelectIndices(Palette.Default,"0,2").SequenceEqual(new[]{Palette.Default[0],Palette.Default[2]}),"zero-based palette indices");
Reject(()=>Palette.SelectIndices(Palette.Default,"-1"),"invalid palette index");
Reject(()=>(new AssetSettings{Width=0}).Validate(),"invalid width");
Reject(()=>(new AssetSettings{Height=-4}).Validate(),"invalid height");
Reject(()=>(new AssetSettings{Width=2049}).Validate(),"oversize canvas");
Reject(()=>(new AssetSettings{Colors=[]}).Validate(),"empty palette");
Reject(()=>new Toolchain("missing-blender.exe","missing-aseprite.exe").Validate(),"missing Blender error");
Reject(()=>new Toolchain(Environment.ProcessPath!,"missing-aseprite.exe").Validate(),"missing Aseprite error");
var temp=Path.Combine(Path.GetTempPath(),"PixelForge-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
var settingsFile=Path.Combine(temp,"asset.json");
new AssetSettings{Model="model.blend",Width=24,Height=42,View=CameraView.Back}.Save(settingsFile);
var loaded=AssetSettings.Load(settingsFile);Check(loaded.Width==24&&loaded.Height==42&&loaded.View==CameraView.Back,"asset settings round trip");
Check(loaded.Model==Path.Combine(temp,"model.blend"),"relative model path resolves beside settings");
var gpl=Path.Combine(temp,"test.gpl");File.WriteAllText(gpl,"GIMP Palette\nName: test\n255 0 0 red\n0 0 0 black\n");Check(Palette.Load(gpl).Length==2,"GPL import");
var pal=Path.Combine(temp,"test.pal");File.WriteAllText(pal,"JASC-PAL\n0100\n2\n255 0 0\n0 0 0\n");Check(Palette.Load(pal).Length==2,"PAL import");
File.WriteAllText(pal,"JASC-PAL\n0100\n3\n255 0 0\n");Reject(()=>Palette.Load(pal),"PAL count validation");
File.WriteAllText(gpl,"not a palette");Reject(()=>Palette.Load(gpl),"invalid palette file");
foreach(var (w,h) in new[]{(32,32),(24,42)})
{
    var pixels=new byte[w*h*4];var ids=new byte[pixels.Length];
    for(int y=2;y<h-2;y++)for(int x=2;x<w-2;x++){int p=(y*w+x)*4;pixels[p+2]=160;pixels[p+1]=90;pixels[p]=40;pixels[p+3]=255;ids[p+2]=1;ids[p+3]=255;}
    var settings=new AssetSettings{Width=w,Height=h,SamplesPerPixel=1};
    var a=PixelEngine.Convert(new(w,h,pixels),new(w,h,ids),settings);
    PixelEngine.Validate(a.Image,settings);Check(a.Image.Width==w&&a.Image.Height==h,$"exact {w}×{h}, palette and alpha");
    var bad=(byte[])a.Image.Pixels.Clone();bad[(2*w+2)*4+3]=128;Reject(()=>PixelEngine.Validate(new(w,h,bad),settings),"reject partial alpha");
    Check(a.Image.Pixels.SequenceEqual(PixelEngine.Convert(new(w,h,pixels),new(w,h,ids),settings).Image.Pixels),"deterministic processing");
    var png=Path.Combine(temp,$"{w}-{h}.png");a.Image.Save(png);Check(Raster.Read(png).Pixels.SequenceEqual(a.Image.Pixels),"PNG round trip");
}
Console.WriteLine($"{passed} unit checks passed. Actual integration tests are separate.");
if(args.Length>0&&args[0]=="--integration")
{
    var model=Path.GetFullPath(args[1]);var root=Path.GetFullPath(args.Length>2?args[2]:"outputs");
    var pipeline=new ConversionPipeline(Toolchain.Discover());var progress=new Progress<string>(Console.WriteLine);
    var results=new List<ConversionOutput>();
    byte[]? editedHash=null;string? editedPath=null;
    foreach(var (w,h,view,sampling) in new[]{(64,64,CameraView.IsometricSE,3),(64,64,CameraView.IsometricNW,3),(32,32,CameraView.IsometricSE,3),(24,42,CameraView.IsometricSE,3),(64,64,CameraView.IsometricSE,1),(64,64,CameraView.IsometricSE,3)})
    {
        var result=await pipeline.Generate(new(){Model=model,Width=w,Height=h,View=view,SamplesPerPixel=sampling},root,progress);results.Add(result);Console.WriteLine("OUTPUT: "+result.Directory);
        if(results.Count==1)
        {
            editedPath=Path.Combine(result.Directory,"edited-preservation-test.aseprite");File.Copy(result.Aseprite,editedPath);
            var script=Path.Combine(result.Directory,"edit-preservation-test.lua");
            var safePath=editedPath.Replace('\\','/');
            File.WriteAllText(script,$"local s=app.open(\"{safePath}\")\nlocal layer=s.layers[#s.layers]\nassert(layer.name=='Manual corrections')\nlocal image=Image(s.width,s.height,ColorMode.RGB)\nimage:drawPixel(0,0,app.pixelColor.rgba(255,0,0,255))\ns:newCel(layer,1,image,Point(0,0))\ns:saveAs(\"{safePath}\")\n");
            await Toolchain.Run(Toolchain.Discover().Aseprite,["--batch","--script",script],Path.Combine(result.Directory,"edit-preservation.log"));
            editedHash=SHA256.HashData(File.ReadAllBytes(editedPath));
        }
    }
    Check(!Raster.Read(results[0].Png).Pixels.SequenceEqual(Raster.Read(results[1].Png).Pixels),"actual camera direction changes visible geometry");
    Check(Raster.Read(results[0].Png).Pixels.SequenceEqual(Raster.Read(results[5].Png).Pixels),"actual Blender deterministic regeneration");
    Check(results.Select(r=>r.Directory).Distinct().Count()==results.Count,"non-destructive regeneration directories");
    Check(editedHash!.SequenceEqual(SHA256.HashData(File.ReadAllBytes(editedPath!))),"actual Aseprite manual edit preserved across regeneration");
    Check((await Palette.LoadNative(results[0].Aseprite,Toolchain.Discover().Aseprite)).SequenceEqual(Palette.Default),"native Aseprite palette round trip");
    File.WriteAllLines(Path.Combine(root,"latest-tests.txt"),results.Select(r=>r.Directory));
    Console.WriteLine("All integration conversions passed native reopening, source hashing, composite equality and pixel validation.");
}
if(args.Length>0&&args[0]=="--fixture")
{
    var root=Path.GetFullPath(args.Length>2?args[2]:"outputs");var model=Path.GetFullPath(args[1]);
    var pipeline=new ConversionPipeline(Toolchain.Discover());
    foreach(var view in Enum.GetValues<CameraView>())
    {
        var s=new AssetSettings{Model=model,Width=24,Height=42,View=view,Colors=["#171923","#505563","#A5AFB0","#DFE4D8","#50392B","#A77749","#D1A46A","#263E55","#466781","#739BA6"]};
        var result=await pipeline.Generate(s,root,new Progress<string>(Console.WriteLine));Console.WriteLine($"FIXTURE {view}: {result.Directory}");
    }
    var iso=await pipeline.Generate(new(){Model=model,Projection=Projection.TrueIsometric},root);
    Check(File.ReadAllText(Path.Combine(iso.Directory,"model.json")).Contains("35.264"),"true isometric elevation");
    var world=await pipeline.Generate(new(){Model=model,WorldScale=true,PixelsPerUnit=10},root);
    Check(File.Exists(world.Aseprite),"world scale render");
}
