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
        string tiny=Path.Combine(root,"tiny-square.blend");
        await Toolchain.Run(tools.Blender,["--background","--factory-startup","--python-exit-code","1","--python",Path.GetFullPath("tests/create_padding_fixture.py"),"--",tiny,"--tiny"],tiny+".log");
        foreach(var pair in new[]{(padding:0d,width:32),(padding:.02,width:30),(padding:.10,width:26)})
        {
            var small=await pipeline.Generate(baseline with{Model=tiny,Padding=pair.padding},root);var sb=Bounds(Raster.Read(small.Png));
            Check(sb.w==pair.width&&sb.h==pair.width,$"tiny world-unit tile at {pair.padding} has no hidden minimum camera span");
        }
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
            window.Show();var info=(System.Windows.Controls.TextBlock)window.FindName("OutputInfo");var preview=(System.Windows.Controls.ScrollViewer)window.FindName("PreviewScroll");
            var image=(System.Windows.Controls.Image)window.FindName("PreviewImage");var imageSource=new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(padded.Png)));image.Source=imageSource;
            info.Text="One short output line";window.UpdateLayout();double beforeHeight=preview.ActualHeight,beforeWidth=image.Width;
            info.Text=string.Join("\n",Enumerable.Repeat("Canvas-edge and shadow warning that must remain readable without changing preview zoom.",14));window.UpdateLayout();
            Check(preview.ActualHeight==beforeHeight&&image.Width==beforeWidth,"long padding warnings do not change preview viewport or integer Fit zoom");
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

    public static async Task Compare(string model,string outputRoot)
    {
        var root=Path.GetFullPath(Path.Combine(outputRoot,"run-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var pipeline=new ConversionPipeline(Toolchain.Discover());var rows=new List<object>();int checks=0;
        void Verify(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);checks++;}
        foreach(int size in new[]{32,128})foreach(bool animated in new[]{false,true})foreach(bool outline in new[]{false,true})foreach(bool shadow in new[]{false,true})
        {
            var measurements=new List<(double Padding,int W,int H,double Scale)>();
            foreach(double padding in new[]{0,.02,.10})
            {
                string key=$"{size}-{(animated?"animated":"static")}-outline{outline}-shadow{shadow}-p{padding:0.00}";
                Console.WriteLine("RENDER "+key);
                var s=new AssetSettings{Model=Path.GetFullPath(model),Width=size,Height=size,Padding=padding,View=CameraView.IsometricSE,Animation=animated,StartFrame=1,EndFrame=7,FrameStep=3,Outlines=outline,GroundShadows=shadow,EdgeHighlights=true};
                var r=await pipeline.Generate(s,root);GeneratedAsset.Save(r);var exported=Path.Combine(root,key+".png");AssetExport.SavePng(r,exported);AssetExport.SaveSpritesheet(r,Path.Combine(root,key+"-sheet.png"));
                var union=new byte[size*size*4];foreach(var f in r.Frames){var dir=Path.GetDirectoryName(f.Png)!;foreach(string layer in Directory.GetFiles(dir,"layer-*.png")){var pixels=Raster.Read(layer).Pixels;for(int p=3;p<union.Length;p+=4)if(pixels[p]>0)union[p]=255;}}
                var b=Bounds(new Raster(size,size,union));var composite=Bounds(Raster.Read(exported));
                using var framing=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"framing.json")));var projected=framing.RootElement.GetProperty("projected_world_size").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                using var frames=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"frames.json")));double scale=frames.RootElement.GetProperty("ortho_scale").GetDouble();
                Verify(Math.Abs(scale-Math.Max(projected[0],projected[1])/(1-2*padding))<1e-9,key+" camera fits projected union with exactly requested margin");
                Verify(AssetSettings.Load(Path.Combine(r.Directory,"asset.settings.json")).Padding==padding,key+" serialized padding preserved");
                using var job=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"job.json")));Verify(job.RootElement.GetProperty("Padding").GetDouble()==padding,key+" Blender job uses exact padding");
                Verify(Raster.Read(exported).Pixels.SequenceEqual(Raster.Read(r.Png).Pixels),key+" GUI-equivalent PNG export preserves exact pixels");
                measurements.Add((padding,b.w,b.h,scale));rows.Add(new{key,padding,size,animated,outline,shadow,modelBounds=b.ToString(),compositeBounds=composite.ToString(),scale,directory=r.Directory,exported});
            }
            Verify(measurements[0].W>=measurements[1].W&&measurements[1].W>=measurements[2].W&&measurements[0].H>=measurements[1].H&&measurements[1].H>=measurements[2].H,$"{size}/{animated}/{outline}/{shadow} decreasing padding never shrinks visible model union");
        }
        foreach(bool fixedScale in new[]{false,true})
        {
            var scales=new List<double>();
            foreach(double padding in new[]{0,.02,.10})
            {
                var s=new AssetSettings{Model=Path.GetFullPath(model),Width=128,Height=128,Padding=padding,WorldScale=fixedScale,PixelsPerUnit=12,FitBounds=FitBoundsSource.ObjectAndGroundShadow,GroundShadows=true};
                var r=await pipeline.Generate(s,root);using var frame=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"frames.json")));double scale=frame.RootElement.GetProperty("ortho_scale").GetDouble();scales.Add(scale);
                Verify(AssetSettings.Load(Path.Combine(r.Directory,"asset.settings.json")).FitBounds==s.FitBounds&&AssetSettings.Load(Path.Combine(r.Directory,"asset.settings.json")).WorldScale==fixedScale,"zero/positive padding preserves requested density and shadow-fit mode");
                if(fixedScale)Verify(Math.Abs(scale-128d/12)<1e-9,"world scale retains fixed density instead of fitting to canvas");
            }
            if(!fixedScale)Verify(scales[0]<scales[1]&&scales[1]<scales[2],"shadow-inclusive fit respects zero and positive margin without silently switching modes");
        }
        File.WriteAllText(Path.Combine(root,"comparisons.json"),JsonSerializer.Serialize(new{checks,rows},AssetSettings.JsonOptions));
        Console.WriteLine($"{checks} padding comparison checks passed: {root}");
    }

    public static void MeasureExports(string directory)
    {
        var paths=Directory.GetFiles(directory,"*.png").Where(p=>!Path.GetFileName(p).Contains("sheet")&&!Path.GetFileName(p).Contains("contact")).Order().ToArray();
        var rows=paths.Select(path=>{var image=Raster.Read(path);var b=Bounds(image);return new{file=Path.GetFileName(path),canvasWidth=image.Width,canvasHeight=image.Height,x=b.x,y=b.y,visibleWidth=b.w,visibleHeight=b.h};}).ToArray();
        File.WriteAllText(Path.Combine(directory,"visible-bounds.json"),JsonSerializer.Serialize(rows,AssetSettings.JsonOptions));foreach(var row in rows)Console.WriteLine($"{row.file}: {row.canvasWidth}x{row.canvasHeight}; visible x={row.x}, y={row.y}, {row.visibleWidth}x{row.visibleHeight}");
        var comparisonFiles=(from size in new[]{32,128} from padding in new[]{"000","002","010"} select Path.Combine(directory,$"{size}-p{padding}.png")).ToArray();
        if(comparisonFiles.All(File.Exists))
        {
            const int cell=256,gap=16,w=cell*3+gap*4,h=cell*2+gap*3;var bytes=new byte[w*h*4];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=(y*w+x)*4;byte value=(byte)(((x/8+y/8)%2)==0?40:50);bytes[p]=bytes[p+1]=bytes[p+2]=value;bytes[p+3]=255;}
            for(int n=0;n<comparisonFiles.Length;n++){var original=Raster.Read(comparisonFiles[n]);var enlarged=original.Enlarge(cell/original.Width);int left=gap+(n%3)*(cell+gap),top=gap+(n/3)*(cell+gap);for(int y=0;y<cell;y++)for(int x=0;x<cell;x++){int source=(y*cell+x)*4,dest=((top+y)*w+left+x)*4;if(enlarged.Pixels[source+3]>0)Array.Copy(enlarged.Pixels,source,bytes,dest,4);}}
            string contact=Path.Combine(directory,"padding-contact-"+Guid.NewGuid().ToString("N")[..6]+".png");new Raster(w,h,bytes).Save(contact);Console.WriteLine("Contact sheet (rows 32/128; columns 0/0.02/0.10): "+contact);
        }
    }
}
