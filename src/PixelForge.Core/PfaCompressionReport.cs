using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using PixelForge.Core.IndexedLab;

namespace PixelForge.Core;

internal sealed class PfaCompressionReport(string root)
{
    private readonly StringBuilder detail=new();
    private readonly HashSet<string> oldPalettes=new(StringComparer.OrdinalIgnoreCase),newPalettes=new(StringComparer.OrdinalIgnoreCase);
    private long baseline,optimized,png,webp,jxl,indexed,pngManifests,webpManifests,jxlManifests;
    private int layers,webpCount,jxlCount,indexedCount;

    private long priorBase,priorPfa,priorPng,priorWebp,priorJxl;
    private int priorLayers,priorWebpLayers,priorJxlLayers;

    public void AddDirection(string name,long pngJson,long webpJson,long jxlJson)
    {
        pngManifests+=pngJson;webpManifests+=webpJson;jxlManifests+=jxlJson;
        int count=layers-priorLayers;
        detail.AppendLine($"{name} total · {baseline-priorBase:N0} previous PFA · {optimized-priorPfa:N0} optimized PFA, shared dependencies counted below");
        detail.AppendLine($"  PNG {png-priorPng+pngJson:N0} bytes including JSON; "+
            (webpCount-priorWebpLayers==count?$"WebP {webp-priorWebp+webpJson:N0} bytes including JSON":"WebP unavailable for this direction"));
        if(jxlCount-priorJxlLayers==count)detail.AppendLine($"  JPEG XL {jxl-priorJxl+jxlJson:N0} bytes including JSON");
        detail.AppendLine();
        priorBase=baseline;priorPfa=optimized;priorPng=png;priorWebp=webp;priorJxl=jxl;
        priorLayers=layers;priorWebpLayers=webpCount;priorJxlLayers=jxlCount;
    }

    public void Add(string name,Raster[] frames,PfaImage old,PfaImage current,string pngPath,string? webpPath,string webpState,string? jxlPath,string jxlState)
    {
        layers++;
        long pngSize=new FileInfo(pngPath).Length;
        long? webpSize=webpPath is null?null:new FileInfo(webpPath).Length;
        long? jxlSize=jxlPath is null?null:new FileInfo(jxlPath).Length;
        baseline+=old.FileBytes;optimized+=current.FileBytes;png+=pngSize;
        if(current.Info.Palette.Length>0)newPalettes.Add(current.Info.Palette);
        if(old.Info.Palette.Length>0)oldPalettes.Add(old.Info.Palette);
        if(webpSize is long w){webpCount++;webp+=w;}
        if(jxlSize is long j){jxlCount++;jxl+=j;}
        long total=0,transparent=0;
        var values=new HashSet<uint>();
        foreach(var f in frames)
            for(int p=0;p<f.Pixels.Length;p+=4)
            {
                total++;if(f.Pixels[p+3]==0)transparent++;
                values.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.Pixels.AsSpan(p,4)));
            }
        detail.AppendLine($"{name} | {current.Info.Layer} | {current.Info.Width}×{current.Info.Height}×{frames.Length} | {values.Count:N0} distinct BGRA values | {transparent/(double)total:P1} transparent");
        Describe("  Previous PFA",old,total);
        Describe("  Optimized PFA",current,total);
        if(old.PayloadBytes>0&&old.FileBytes>0)
            detail.AppendLine($"  Saved {old.PayloadBytes-current.PayloadBytes:N0} payload bytes ({(old.PayloadBytes-current.PayloadBytes)/(double)old.PayloadBytes:P1}), {old.FileBytes-current.FileBytes:N0} full bytes ({(old.FileBytes-current.FileBytes)/(double)old.FileBytes:P1})");
        foreach(var trial in current.Trials??[])
            detail.AppendLine($"    {trial.Storage} + {trial.Codec}: {trial.StorageBytes:N0} stored raw, {trial.PayloadBytes:N0} payload, {trial.FileBytes:N0} full bytes");
        long? indexedSize=TryIndexedPng(pngPath);
        if(indexedSize is long i){indexedCount++;indexed+=i;}
        detail.AppendLine($"  RGBA PNG atlas: {pngSize:N0} bytes · PASS");
        detail.AppendLine($"  Indexed PNG atlas: {(indexedSize is long indexedBytes?$"{indexedBytes:N0} bytes · PASS":"Unavailable or more than 256 exact RGBA states")}");
        detail.AppendLine($"  WebP atlas: {(webpSize is long wb?$"{wb:N0} bytes · ":"")}{webpState}");
        detail.AppendLine($"  JPEG XL atlas: {(jxlSize is long jb?$"{jb:N0} bytes · ":"")}{jxlState}");
        ProbeTransforms(frames,detail);
        detail.AppendLine();
    }

    private void Describe(string label,PfaImage image,long pixels)
    {
        detail.AppendLine($"{label}: {image.Storage} / {image.Codec}, {image.FileBytes:N0} bytes · {pixels*4:N0} decoded BGRA · {image.StorageBytes:N0} raw storage · {image.PayloadBytes:N0} payload · 48 prefix + {image.MetadataBytes:N0} metadata · {image.EncodeMs:F1}/{image.DecodeMs:F1} ms encode/decode · PASS");
        if(image.Info.Palette.Length>0)detail.AppendLine("    Shared palette: "+image.Info.Palette);
    }

    private static long? TryIndexedPng(string png)
    {
        var raster=IndexedBenchmark.Load(png,8192);
        var colors=new List<Color>();var entries=new Dictionary<uint,byte>();var data=new byte[raster.Pixels.Length/4];
        for(int i=0;i<data.Length;i++)
        {
            int p=i*4;uint value=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raster.Pixels.AsSpan(p,4));
            if(!entries.TryGetValue(value,out byte index))
            {
                if(colors.Count==256)return null;
                index=(byte)colors.Count;entries.Add(value,index);
                colors.Add(Color.FromArgb(raster.Pixels[p+3],raster.Pixels[p+2],raster.Pixels[p+1],raster.Pixels[p]));
            }
            data[i]=index;
        }
        string path=png+".indexed-"+Guid.NewGuid().ToString("N");
        try
        {
            WriteIndexedPng(path,raster.Width,raster.Height,colors,data);
            return IndexedBenchmark.Verify(raster,IndexedBenchmark.Load(path,8192))=="PASS"?new FileInfo(path).Length:null;
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    private static void WriteIndexedPng(string path,int width,int height,List<Color> colors,byte[] indices)
    {
        using var output=new FileStream(path,FileMode.CreateNew);
        output.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header,width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=3;
        Chunk("IHDR",header);
        Chunk("PLTE",colors.SelectMany(c=>new[]{c.R,c.G,c.B}).ToArray());
        Chunk("tRNS",colors.Select(c=>c.A).ToArray());
        using var idat=new MemoryStream();
        using(var deflate=new ZLibStream(idat,CompressionLevel.Optimal,true))
            for(int y=0;y<height;y++)
            {
                deflate.WriteByte(0);
                deflate.Write(indices.AsSpan(y*width,width));
            }
        Chunk("IDAT",idat.ToArray());
        Chunk("IEND",[]);

        void Chunk(string type,byte[] data)
        {
            Span<byte> number=stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number,data.Length);output.Write(number);
            byte[] name=Encoding.ASCII.GetBytes(type);output.Write(name);output.Write(data);
            uint crc=0xffffffff;
            void Update(byte[] bytes)
            {
                foreach(byte value in bytes)
                {
                    crc^=value;
                    for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);
                }
            }
            Update(name);Update(data);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number,~crc);output.Write(number);
        }
    }

    private static void ProbeTransforms(Raster[] frames,StringBuilder output)
    {
        byte[] original=frames.SelectMany(f=>f.Pixels).ToArray();
        // Diagnostic probes. They are never stored in a PFA unless a versioned decoder is added.
        int frameSize=frames[0].Pixels.Length;
        byte[] xor=(byte[])original.Clone();
        for(int i=xor.Length-1;i>=frameSize;i--)xor[i]^=xor[i-frameSize];
        Probe("frame XOR + Brotli",xor,data=>{for(int i=frameSize;i<data.Length;i++)data[i]^=data[i-frameSize];return data;});
        Probe("BGRA + Deflate",original,data=>data,deflate:true);
        foreach(int tile in new[]{8,16,32})
        {
            int w=frames[0].Width,h=frames[0].Height,cols=(w+tile-1)/tile,rows=(h+tile-1)/tile;
            int bitmap=(cols*rows*frames.Length+7)/8;
            var mask=new byte[bitmap];using var occupied=new MemoryStream();
            int index=0;
            foreach(var frame in frames)for(int top=0;top<h;top+=tile)for(int left=0;left<w;left+=tile,index++)
            {
                int bw=Math.Min(tile,w-left),bh=Math.Min(tile,h-top);
                bool nonzero=false;
                for(int row=0;row<bh&&!nonzero;row++)
                    nonzero=frame.Pixels.AsSpan(((top+row)*w+left)*4,bw*4).ContainsAnyExcept((byte)0);
                if(!nonzero)continue;
                mask[index/8]|=(byte)(1<<(index%8));
                for(int row=0;row<bh;row++)occupied.Write(frame.Pixels.AsSpan(((top+row)*w+left)*4,bw*4));
            }
            byte[] sparse=[..mask,..occupied.ToArray()];
            Probe($"sparse {tile}×{tile} + Brotli ({bitmap:N0} occupancy bytes)",sparse,data=>{
                var restored=new byte[original.Length];int cursor=bitmap,i=0;
                for(int f=0;f<frames.Length;f++)for(int top=0;top<h;top+=tile)for(int left=0;left<w;left+=tile,i++)
                {
                    if((data[i/8]&(1<<(i%8)))==0)continue;
                    int bw=Math.Min(tile,w-left),bh=Math.Min(tile,h-top);
                    for(int row=0;row<bh;row++)
                    {
                        data.AsSpan(cursor,bw*4).CopyTo(restored.AsSpan(f*frameSize+((top+row)*w+left)*4,bw*4));
                        cursor+=bw*4;
                    }
                }
                return restored;
            });
        }

        void Probe(string label,byte[] data,Func<byte[],byte[]> expand,bool deflate=false)
        {
            var watch=Stopwatch.StartNew();
            using var compressed=new MemoryStream();
            using(Stream encoder=deflate?new DeflateStream(compressed,CompressionLevel.Optimal,true):new BrotliStream(compressed,CompressionLevel.Optimal,true))
                encoder.Write(data);
            double enc=watch.Elapsed.TotalMilliseconds;
            compressed.Position=0;watch.Restart();
            var read=new byte[data.Length];
            using(Stream decoder=deflate?new DeflateStream(compressed,CompressionMode.Decompress,true):new BrotliStream(compressed,CompressionMode.Decompress,true))
            {decoder.ReadExactly(read);if(decoder.ReadByte()!=-1)throw new InvalidDataException("Compression probe overflow.");}
            if(!expand(read).SequenceEqual(original))throw new InvalidDataException("Compression probe changed pixels.");
            output.AppendLine($"    Experiment {label}: {data.Length:N0} raw, {compressed.Length:N0} payload bytes · {enc:F1}/{watch.Elapsed.TotalMilliseconds:F1} ms · PASS");
        }
    }

    public string Render()
    {
        var text=new StringBuilder("Compression report · exact decoded pixels\n");
        long marker=new FileInfo(Path.Combine(root,".pixelforge-assets.json")).Length;
        long Shared(HashSet<string> palettes)=>palettes.Sum(p=>new FileInfo(Pfa.PalettePath(root,p)).Length);
        long oldShared=Shared(oldPalettes),newShared=Shared(newPalettes);
        long oldTotal=baseline+oldShared+marker,newTotal=optimized+newShared+marker;
        text.AppendLine($"All {layers} layers and directions:");
        text.AppendLine($"  Previous PFA: {baseline:N0} + {oldShared:N0} shared GPL + {marker:N0} root bytes = {oldTotal:N0}");
        text.AppendLine($"  Optimized PFA: {optimized:N0} + {newShared:N0} shared GPL + {marker:N0} root bytes = {newTotal:N0}");
        if(oldTotal>0)text.AppendLine($"  Collection saving: {oldTotal-newTotal:N0} bytes ({(oldTotal-newTotal)/(double)oldTotal:P1})");
        string Total(int count,long bytes,long manifests)=>count==layers?$"{bytes:N0} + {manifests:N0} direction JSON = {bytes+manifests:N0} bytes":$"Unavailable for {layers-count} layer(s); partial totals excluded";
        text.AppendLine($"  RGBA PNG: {Total(layers,png,pngManifests)}");
        text.AppendLine($"  Indexed PNG: {Total(indexedCount,indexed,pngManifests)}");
        text.AppendLine($"  WebP Lossless: {Total(webpCount,webp,webpManifests)}");
        text.AppendLine($"  JPEG XL Lossless: {Total(jxlCount,jxl,jxlManifests)}");
        text.AppendLine("Atlas comparisons count the direction JSON that each standalone format would require.");
        text.AppendLine("Comparison totals exclude the optional report and PNG previews. PFA embeds timing and placement; atlas formats use direction JSON.");
        text.AppendLine("Experiment probes are payload-only and include sparse occupancy bytes. Zstd, dictionaries, and LZMA are not bundled.");
        text.AppendLine();text.Append(detail);
        return text.ToString();
    }
}
