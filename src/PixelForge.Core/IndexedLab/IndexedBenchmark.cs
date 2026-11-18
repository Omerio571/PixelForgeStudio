using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core.IndexedLab;

public sealed record LabMeasurement(string Format,long Bytes,int? PayloadBytes,double EncodeMs,double DecodeMs,string Verification);
public sealed record LabImageResult(string Source,int Width,int Height,int Colors,bool Transparent,int PartialAlpha,int ChangedPixels,long SourceBytes,
    IReadOnlyList<LabMeasurement> Measurements);
public sealed record LabSample(Raster Source,Raster Converted,Raster Decoded,IndexedPixels Indices);
public sealed record LabBatch(IReadOnlyList<LabImageResult> Rows,LabSample? First,string JxlStatus);

public static class IndexedBenchmark
{
    private static readonly EncodingMethod[] Methods=[EncodingMethod.Packed,EncodingMethod.Runs,EncodingMethod.Brotli];

    public static Raster Load(string path,int maxDimension=IndexedCodec.MaxDimension)
    {
        using var stream=File.OpenRead(path);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
        var frame=decoder.Frames[0];
        if(frame.PixelWidth<1||frame.PixelWidth>maxDimension||frame.PixelHeight<1||frame.PixelHeight>maxDimension||(long)frame.PixelWidth*frame.PixelHeight>33_554_432)
            throw new InvalidDataException($"Image dimensions exceed the {maxDimension} pixel side or total pixel limit.");
        var image=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);
        var pixels=new byte[checked(frame.PixelWidth*frame.PixelHeight*4)];
        image.CopyPixels(pixels,frame.PixelWidth*4,0);
        return new(frame.PixelWidth,frame.PixelHeight,pixels);
    }

    public static string Verify(Raster expected,Raster decoded)
    {
        if(expected.Width!=decoded.Width||expected.Height!=decoded.Height)return "FAIL: dimensions differ";
        int count=0,first=-1;
        for(int p=0;p<expected.Pixels.Length;p+=4)
            if(!expected.Pixels.AsSpan(p,4).SequenceEqual(decoded.Pixels.AsSpan(p,4)))
            { if(first<0)first=p/4;count++; }
        return count==0?"PASS":$"FAIL: {count} pixels differ, first at ({first%expected.Width}, {first/expected.Width})";
    }

    public static async Task<LabBatch> Run(string root,string reference,IReadOnlyList<string> sources,byte threshold,string? encoder,string? decoder,
        IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        var palette=SharedPalette.Read(IndexedCodec.PalettePath(root,reference));
        var work=Directory.CreateTempSubdirectory("pixelforge-indexed-").FullName;
        var rows=new List<LabImageResult>();LabSample? first=null;
        string jxlStatus="Unavailable. Set paths to cjxl and djxl or add both tools to PATH.";
        bool jxl=!string.IsNullOrEmpty(encoder)&&!string.IsNullOrEmpty(decoder);
        try
        {
            string snapshotRoot=Path.Combine(work,"Assets");
            string snapshotPalette=IndexedCodec.PalettePath(snapshotRoot,reference);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPalette)!);
            File.WriteAllBytes(snapshotPalette,palette.Serialize());
            if(jxl)
            {
                try
                {
                    await Toolchain.Run(encoder!,["--version"],Path.Combine(work,"cjxl-version.txt"),cancellation);
                    await Toolchain.Run(decoder!,["--version"],Path.Combine(work,"djxl-version.txt"),cancellation);
                    string Version(string tool)=>File.ReadLines(Path.Combine(work,tool+"-version.txt")).FirstOrDefault(l=>!string.IsNullOrWhiteSpace(l))??tool;
                    jxlStatus=Version("cjxl")+" | "+Version("djxl")+" | distance 0, effort 7";
                }
                catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception)
                {jxl=false;jxlStatus="JPEG XL unavailable: "+e.Message;}
            }
            foreach(string sourcePath in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report($"Testing {rows.Count+1}/{sources.Count}: {Path.GetFileName(sourcePath)}");
                string directory=Path.Combine(work,rows.Count.ToString());Directory.CreateDirectory(directory);
                var source=Load(sourcePath);var indices=IndexedCodec.Convert(source,palette,threshold,cancellation);var canonical=indices.ToRaster(palette);
                var measurements=new List<LabMeasurement>();
                LabMeasurement Measure(string format,string file,Action write,Func<Raster> read,int? payload=null)
                {
                    var watch=Stopwatch.StartNew();write();double encode=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();var result=read();double decode=watch.Elapsed.TotalMilliseconds;
                    return new(format,new FileInfo(file).Length,payload,encode,decode,Verify(canonical,result));
                }
                string png=Path.Combine(directory,"canonical.png");
                measurements.Add(Measure("RGBA PNG",png,()=>canonical.Save(png),()=>Load(png)));
                string indexed=Path.Combine(directory,"indexed.png");
                measurements.Add(Measure("Indexed PNG",indexed,()=>SaveIndexedPng(indices,palette,indexed),()=>Load(indexed)));
                Raster? reconstructed=null;
                foreach(var method in Methods)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path=Path.Combine(directory,method+".pfi");int payload=0;
                    var measurement=Measure(method.ToString(),path,()=>payload=IndexedCodec.Write(path,snapshotRoot,reference,indices,method).Payload,
                        ()=>{var read=IndexedCodec.Read(path,snapshotRoot);return reconstructed=read.Image.ToRaster(read.Palette);});
                    measurements.Add(measurement with {PayloadBytes=payload});
                }
                if(jxl)
                {
                    string encoded=Path.Combine(directory,"image.jxl"), decoded=Path.Combine(directory,"decoded.png");
                    try
                    {
                        var watch=Stopwatch.StartNew();
                        await Toolchain.Run(encoder!,[png,encoded,"-d","0","-e","7"],Path.Combine(directory,"cjxl.txt"),cancellation);
                        double encode=watch.Elapsed.TotalMilliseconds;watch.Restart();
                        await Toolchain.Run(decoder!,[encoded,decoded],Path.Combine(directory,"djxl.txt"),cancellation);
                        double decode=watch.Elapsed.TotalMilliseconds;
                        measurements.Add(new("JPEG XL",new FileInfo(encoded).Length,null,encode,decode,Verify(canonical,Load(decoded))));
                    }
                    catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception)
                    {
                        string ReadLog(string tool)=>File.Exists(Path.Combine(directory,tool+".txt"))?File.ReadAllText(Path.Combine(directory,tool+".txt")).Trim():"";
                        string detail=(ReadLog("cjxl")+" "+ReadLog("djxl")).Trim();
                        measurements.Add(new("JPEG XL",0,null,0,0,"FAIL: "+(detail.Length>0?detail[..Math.Min(300,detail.Length)]:e.Message)));
                    }
                }
                var used=indices.Indices.Distinct().ToArray();
                rows.Add(new(sourcePath,source.Width,source.Height,used.Count(n=>n!=0),used.Contains((byte)0),indices.PartialAlpha,indices.ChangedPixels,
                    new FileInfo(sourcePath).Length,measurements));
                first??=new(source,canonical,reconstructed!,indices);
            }
            return new(rows,first,jxlStatus);
        }
        finally
        {
            Directory.Delete(work,true);
        }
    }

    private static void SaveIndexedPng(IndexedPixels image,SharedPalette palette,string path)
    {
        var colors=new List<Color>{Color.FromArgb(0,0,0,0)};
        colors.AddRange(palette.Colors.Select(c=>Color.FromRgb(c.R,c.G,c.B)));
        var bitmap=BitmapSource.Create(image.Width,image.Height,96,96,PixelFormats.Indexed8,
            new BitmapPalette(colors),image.Indices,image.Width);
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new FileStream(path,FileMode.CreateNew);
        encoder.Save(stream);
    }
}
