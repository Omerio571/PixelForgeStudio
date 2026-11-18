using System.IO;
using System.Text.Json;
using PixelForge.Core;
using PixelForge.Core.IndexedLab;
using PixelForge.Desktop;

internal static class IndexedLabChecks
{
    public static async Task Run(string root,string[] art)
    {
        root=Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        string assets=Path.Combine(root,"Assets");
        string palettes=Path.Combine(assets,"Palettes");Directory.CreateDirectory(palettes);
        const string reference="Palettes/example.pfpal";
        var palette=SharedPalette.Create(Palette.Default.Concat(new[]{"#78434D","#AB657C","#DDA1BC","#EBC4D5","#544975","#816CA2","#B8A5CE"}.Select(Palette.ParseColor)));
        string paletteFile=IndexedCodec.PalettePath(assets,reference);
        using(var stream=new FileStream(paletteFile,FileMode.CreateNew))stream.Write(palette.Serialize());
        int passed=0;
        void Check(bool result,string name){if(!result)throw new InvalidDataException(name);Console.WriteLine("PASS: "+name);passed++;}
        void Reject(Action action,string name)
        {
            try{action();}
            catch(Exception e) when(e is IOException or ArgumentException or InvalidDataException){Check(true,name);return;}
            throw new Exception("Expected rejection: "+name);
        }
        Check(SharedPalette.Read(paletteFile).Colors.SequenceEqual(palette.Colors)&&new FileInfo(paletteFile).Length==115,"compact palette, 31 colors and stable identity");
        Reject(()=>SharedPalette.Create(Palette.Default),"incorrect palette count rejected");
        Reject(()=>IndexedCodec.PalettePath(assets,"Palettes/../example.pfpal"),"traversal rejected");
        Reject(()=>IndexedCodec.PalettePath(assets,"C:/Palettes/example.pfpal"),"absolute reference rejected");
        Reject(()=>IndexedCodec.Pack([32]),"out-of-range input index rejected");
        Check(IndexedCodec.Pack([1,2,31]).SequenceEqual(new byte[]{65,124}),"non-aligned LSB-first packing");
        var image=new IndexedPixels(3,1,[0,2,31]);
        foreach(var method in Enum.GetValues<EncodingMethod>())
        {
            var path=Path.Combine(assets,method+".pfi");
            var written=IndexedCodec.Write(path,assets,reference,image,method);
            var read=IndexedCodec.Read(path,assets);
            Check(read.Image.Indices.SequenceEqual(image.Indices)&&read.Image.ToRaster(read.Palette).Pixels[3]==0,method+" round trip and transparent index");
            Check(new FileInfo(path).Length==96+reference.Length+written.Payload,method+" on-disk byte accounting");
        }
        string good=Path.Combine(assets,"Packed.pfi");
        var bytes=File.ReadAllBytes(good);
        void Corrupt(string name,Action<byte[]> alter)
        {
            var bad=(byte[])bytes.Clone();alter(bad);
            var file=Path.Combine(root,name+".pfi");File.WriteAllBytes(file,bad);
            Reject(()=>IndexedCodec.Read(file,assets),name);
        }
        Corrupt("malformed header",b=>b[0]=0);
        Corrupt("oversized dimensions",b=>b[7]=255);
        Corrupt("corrupted packed checksum",b=>b[^1]^=1);
        Corrupt("invalid final bit padding",b=>b[^1]|=128);
        var runs=File.ReadAllBytes(Path.Combine(assets,"Runs.pfi"));runs[^1]=32;
        var badRun=Path.Combine(root,"invalid-run.pfi");File.WriteAllBytes(badRun,runs);
        Reject(()=>IndexedCodec.Read(badRun,assets),"invalid RLE index rejected");
        string moved=Path.Combine(root,"Portable");Directory.CreateDirectory(Path.Combine(moved,"Palettes"));
        File.Copy(good,Path.Combine(moved,"image.pfi"));
        Reject(()=>IndexedCodec.Read(Path.Combine(moved,"image.pfi"),moved),"missing palette rejected");
        string movedPalette=IndexedCodec.PalettePath(moved,reference);
        File.Copy(paletteFile,movedPalette);
        Check(IndexedCodec.Read(Path.Combine(moved,"image.pfi"),moved).Image.Indices.SequenceEqual(image.Indices),"portable Assets root");
        var changed=File.ReadAllBytes(movedPalette);changed[22]^=1;File.WriteAllBytes(movedPalette,changed);
        Reject(()=>IndexedCodec.Read(Path.Combine(moved,"image.pfi"),moved),"palette hash mismatch rejected even with same identifier");
        Check(IndexedCodec.Read(Path.Combine(moved,"image.pfi"),moved,reference).Image.Indices.SequenceEqual(image.Indices),
            "explicit palette rebind preserves ordered pixel indices");
        var alpha=IndexedCodec.Convert(new Raster(3,1,[0,0,0,0,0,0,0,127,0,0,0,128]),palette,128);
        Check(alpha.Indices[0]==0&&alpha.Indices[1]==0&&alpha.Indices[2]!=0&&alpha.PartialAlpha==2,"binary alpha and affected count");

        var sources=new List<string>(art);
        var rng=new Random(20);
        foreach(var (name,w,h,pattern) in new[]{("icon",19,13,0),("flat",96,64,1),("pattern",128,128,2),("noise",311,207,3),("large",768,512,2)})
        {
            var indices=new byte[w*h];
            for(int i=0;i<indices.Length;i++)indices[i]=(byte)(pattern switch
            {
                0=>i%19<3||i/19<2?0:3,1=>7,2=>(i/w/8+i%w/8)%7, _=>rng.Next(32)
            });
            string file=Path.Combine(root,name+".png");
            new IndexedPixels(w,h,indices).ToRaster(palette).Save(file);sources.Add(file);
        }
        var encoder=Environment.GetEnvironmentVariable("PIXELFORGE_CJXL");
        var decoder=Environment.GetEnvironmentVariable("PIXELFORGE_DJXL");
        var batch=await IndexedBenchmark.Run(assets,reference,sources,128,encoder,decoder);
        Check(batch.Rows.All(r=>r.Measurements.All(m=>m.Verification=="PASS")),"every measured format decodes to exact canonical RGBA");
        Check(batch.Rows.Count==sources.Count,"batch tested shared palette across all images");
        File.WriteAllText(Path.Combine(root,"measurements.json"),JsonSerializer.Serialize(new{paletteBytes=SharedPalette.Size,batch.JxlStatus,batch.Rows,passed},new JsonSerializerOptions{WriteIndented=true}));
        var examples=Path.Combine(assets,"Examples");Directory.CreateDirectory(examples);
        for(int i=0;i<sources.Count;i++)
        {
            var converted=IndexedCodec.Convert(IndexedBenchmark.Load(sources[i]),palette,128);
            var file=Path.Combine(examples,$"{i+1:00}-{Path.GetFileNameWithoutExtension(sources[i])}.pfi");
            IndexedCodec.Write(file,assets,reference,converted,EncodingMethod.Auto);
        }
        CheckWindow(root);
        Console.WriteLine($"{passed} indexed lab checks passed. Results: {root}");
    }

    private static void CheckWindow(string root)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{
            try
            {
                var app=new App();app.InitializeComponent();app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
                foreach(string theme in new[]{"Dark","Light"})
                {
                    ThemeManager.Apply(theme);
                    var window=new IndexedLabWindow(Path.Combine(root,"profile"),Palette.Default);
                    window.Show();window.UpdateLayout();
                    if(window.FindName("RunButton") is not System.Windows.Controls.Button run||run.IsEnabled)
                        throw new InvalidDataException("Expected the lab to ask for inputs before running.");
                    window.Close();
                }
                app.Shutdown();
            }
            catch(Exception e){failure=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw new Exception("Indexed lab window verification",failure);
    }
}
