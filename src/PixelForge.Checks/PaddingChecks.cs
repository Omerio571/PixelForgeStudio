using System.IO;
using System.Reflection;
using System.Text.Json;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class PaddingChecks
{
    private static int count;
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
    public static async Task Run(string outputRoot)
    {
        var root=Path.GetFullPath(Path.Combine(outputRoot,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var tools=Toolchain.Discover();var pipeline=new ConversionPipeline(tools);
        var square=Path.Combine(root,"square.blend");var rectangle=Path.Combine(root,"rectangle.blend");
        foreach(var file in new[]{square,rectangle})await Toolchain.Run(tools.Blender,["--background","--factory-startup","--python-exit-code","1","--python",Path.GetFullPath("tests/create_padding_fixture.py"),"--",file,..(file==rectangle?new[]{"--rectangle"}:Array.Empty<string>())],file+".log");
        var squareHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(square));
        var baseline=new AssetSettings{Model=square,View=CameraView.Top,Padding=0,Outlines=true,Width=32,Height=32};
        foreach(int size in new[]{8,16,32,64,128})foreach(int sampling in new[]{1,3})
        {
            Console.WriteLine($"RENDER tile {size} sampling {sampling}");
            var settings=baseline with{Width=size,Height=size,SamplesPerPixel=sampling};var output=await pipeline.Generate(settings,root);GeneratedAsset.Save(output);var image=Raster.Read(output.Png);
            Check(image.Width==size&&image.Height==size,$"{size}/{sampling} exact requested canvas and native composite validated");
            Check(Enumerable.Range(0,size*size).All(i=>image.Pixels[i*4+3]==255),$"{size}/{sampling} square uses entire canvas with no forced border");
            uint outline=Palette.ParseColor(settings.OutlineColor).Packed;
            Check(Enumerable.Range(0,size).All(i=>PixelEngine.ColorAt(image.Pixels,i*4)==outline&&PixelEngine.ColorAt(image.Pixels,((size-1)*size+i)*4)==outline&&PixelEngine.ColorAt(image.Pixels,(i*size)*4)==outline&&PixelEngine.ColorAt(image.Pixels,(i*size+size-1)*4)==outline),$"{size}/{sampling} inward outlines remain complete at every canvas edge");
        }
        var r=await pipeline.Generate(baseline with{Model=rectangle},root);var ri=Raster.Read(r.Png);var bounds=Bounds(ri);
        Check(bounds.w==32&&bounds.h==16,"zero padding preserves 2:1 source aspect rather than stretching to fill canvas");
        var padded=await pipeline.Generate(baseline with{Padding=.08},root);var pb=Bounds(Raster.Read(padded.Png));
        Check(pb.w<32&&pb.h<32,"positive padding retains visible framing margin");
        var animated=await pipeline.Generate(baseline with{Model=rectangle,Animation=true,StartFrame=1,EndFrame=3},root);GeneratedAsset.Save(animated);
        using(var framing=JsonDocument.Parse(File.ReadAllText(Path.Combine(animated.Directory,"framing.json")) ))Check(framing.RootElement.GetProperty("shared_frames").GetArrayLength()==3,"zero-padding animation uses one shared framing union");
        var boxes=animated.Frames.Select(f=>Bounds(Raster.Read(f.Png))).ToArray();
        Check(boxes.Length==3&&boxes.All(b=>b.w==boxes[0].w&&b.h==boxes[0].h)&&boxes.Select(b=>b.y).Distinct().Count()==1,"animated zero-padding keeps size and ground alignment stable");
        Check(boxes[0].x!=boxes[^1].x,"shared framing retains source motion instead of recentering each frame");
        Check(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(square)).SequenceEqual(squareHash),"zero-padding checks leave source Blender artwork unchanged");
        Exception? error=null;var thread=new Thread(()=>{try{
            string? previous=Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE");Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"profile"));
            var app=new App();app.InitializeComponent();var window=new MainWindow();
            var control=(NumericSlider)window.FindName("PaddingInput");control.Slider.Value=0;
            var read=typeof(MainWindow).GetMethod("ReadSettings",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Check(((AssetSettings)read.Invoke(window,null)!).Padding==0,"actual padding slider accepts exactly zero");
            control.NumericMode=true;control.Editor.Text="0.08";control.Commit();control.Editor.Text="0";
            Check(control.Commit()&&((AssetSettings)read.Invoke(window,null)!).Padding==0,"actual numeric padding input accepts exactly zero");
            window.Close();var reopened=new MainWindow();Check(((NumericSlider)reopened.FindName("PaddingInput")).NumericMode&&((AssetSettings)read.Invoke(reopened,null)!).Padding==0,"zero padding and numeric mode persist across restart");reopened.Close();Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",previous);
        }catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,root},AssetSettings.JsonOptions));
        Console.WriteLine($"{count} zero-padding checks passed: {root}");
    }
    private static (int x,int y,int w,int h) Bounds(Raster image)
    {
        var points=Enumerable.Range(0,image.Width*image.Height).Where(i=>image.Pixels[i*4+3]>0).ToArray();int x=points.Min(i=>i%image.Width),y=points.Min(i=>i/image.Width);
        return(x,y,points.Max(i=>i%image.Width)-x+1,points.Max(i=>i/image.Width)-y+1);
    }
}
