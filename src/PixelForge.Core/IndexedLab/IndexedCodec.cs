using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PixelForge.Core.IndexedLab;

public enum EncodingMethod : byte { Packed, Runs, Brotli, Auto }
public sealed record SharedPalette(Guid Id, Rgb[] Colors)
{
    public const int Size=115;
    public static SharedPalette Create(IEnumerable<Rgb> colors)
    {
        var entries=colors.ToArray();
        if(entries.Length!=31||entries.Distinct().Count()!=31)throw new InvalidDataException("Use exactly 31 distinct opaque colors.");
        return new(new Guid(SHA256.HashData(entries.SelectMany(c=>new[]{c.R,c.G,c.B}).ToArray()).AsSpan(0,16)),entries);
    }
    public byte[] Serialize()
    {
        Create(Colors);
        return [.. "PFP1"u8,1,0,..Id.ToByteArray(),..Colors.SelectMany(c=>new[]{c.R,c.G,c.B})];
    }
    public static SharedPalette Read(string path)
    {
        using var stream=File.OpenRead(path);
        if(stream.Length!=Size)throw new InvalidDataException("Invalid PFPAL length.");
        var bytes=new byte[Size];stream.ReadExactly(bytes);
        if(!bytes.AsSpan(0,6).SequenceEqual(new byte[]{80,70,80,49,1,0}))throw new InvalidDataException("Unsupported PFPAL header.");
        var colors=Enumerable.Range(0,31).Select(i=>new Rgb(bytes[22+i*3],bytes[23+i*3],bytes[24+i*3])).ToArray();
        Create(colors);
        return new(new Guid(bytes.AsSpan(6,16)),colors);
    }
}
public sealed record IndexedPixels(int Width,int Height,byte[] Indices,int PartialAlpha=0,int ChangedPixels=0)
{
    public Raster ToRaster(SharedPalette palette)
    {
        var data=new byte[Indices.Length*4];
        for(int i=0;i<Indices.Length;i++)
        {
            int index=Indices[i];
            if(index==0)continue;
            var c=palette.Colors[index-1];int p=i*4;
            data[p]=c.B;data[p+1]=c.G;data[p+2]=c.R;data[p+3]=255;
        }
        return new(Width,Height,data);
    }
}
public static class IndexedCodec
{
    public const int MaxDimension=4096;
    private const int MaxPixels=4096*4096;
    private static readonly UTF8Encoding Utf8=new(false,true);

    public static double PaletteDistance(Rgb a,Rgb b)
    {
        var left=PixelEngine.Lab(a.R,a.G,a.B);var right=PixelEngine.Lab(b.R,b.G,b.B);
        return 2*Math.Pow(left.L-right.L,2)+Math.Pow(left.A-right.A,2)+Math.Pow(left.B-right.B,2);
    }

    private static int Count(int w,int h)
    {
        if(w<1||h<1||w>MaxDimension||h>MaxDimension)throw new InvalidDataException("PFI dimensions must be 1–4096 pixels.");
        return w*h;
    }

    public static IndexedPixels Convert(Raster source,SharedPalette palette,byte threshold,CancellationToken cancellation=default)
    {
        if(threshold==0)throw new ArgumentOutOfRangeException(nameof(threshold));
        int count=Count(source.Width,source.Height),partial=0,changed=0;
        var result=new byte[count];
        var labs=palette.Colors.Select(c=>PixelEngine.Lab(c.R,c.G,c.B)).ToArray();
        var cache=new Dictionary<uint,byte>();
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)cancellation.ThrowIfCancellationRequested();
            int p=i*4;byte a=source.Pixels[p+3];
            if(a is >0 and <255)partial++;
            if(a<threshold){if(a!=0||PixelEngine.ColorAt(source.Pixels,p)!=0)changed++;continue;}
            uint rgb=PixelEngine.ColorAt(source.Pixels,p);
            if(!cache.TryGetValue(rgb,out byte index))
            {
                var target=PixelEngine.Lab((rgb>>16)&255,(rgb>>8)&255,rgb&255);
                double best=double.PositiveInfinity;
                for(int j=0;j<labs.Length;j++)
                {
                    var c=labs[j];double d=2*Math.Pow(target.L-c.L,2)+Math.Pow(target.A-c.A,2)+Math.Pow(target.B-c.B,2);
                    if(d<best){best=d;index=(byte)(j+1);}
                }
                if(cache.Count<65536)cache[rgb]=index;
            }
            result[i]=index;
            if(a!=255||palette.Colors[index-1].Packed!=rgb)changed++;
        }
        return new(source.Width,source.Height,result,partial,changed);
    }

    public static byte[] Pack(ReadOnlySpan<byte> indices)
    {
        var packed=new byte[(indices.Length*5+7)/8];
        for(int i=0;i<indices.Length;i++)
        {
            if(indices[i]>31)throw new InvalidDataException("Pixel index exceeds 31.");
            int bit=i*5;
            packed[bit/8]|=(byte)(indices[i]<<(bit%8));
            if(bit%8>3)packed[bit/8+1]|=(byte)(indices[i]>>(8-bit%8));
        }
        return packed;
    }

    private static byte[] Unpack(byte[] data,int count)
    {
        if(data.Length!=(count*5+7)/8||(count*5%8!=0&&data[^1]>>(count*5%8)!=0))
            throw new InvalidDataException("Invalid packed length or padding.");
        var result=new byte[count];
        for(int i=0;i<count;i++)
        {
            int bit=i*5,value=data[bit/8]>>(bit%8);
            if(bit%8>3)value|=data[bit/8+1]<<(8-bit%8);
            result[i]=(byte)(value&31);
        }
        return result;
    }

    public static string PalettePath(string root,string reference)
    {
        var parts=reference.Split('/');
        if(parts.Length<2||parts[0]!="Palettes"||parts.Any(p=>p.Length==0||p is "." or ".."||
            p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||p.Contains('\\')||p.EndsWith('.')||p.EndsWith(' '))||
            !reference.EndsWith(".pfpal",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an Assets-relative Palettes/*.pfpal reference.");
        string path=Path.GetFullPath(Path.Combine(root,Path.Combine(parts)));
        for(string? current=path;current is not null&&!string.Equals(current,Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Palette references cannot follow links outside the Assets tree.");
        return path;
    }

    public static (EncodingMethod Method,int Payload) Write(string path,string root,string reference,IndexedPixels image,EncodingMethod method)
    {
        var palette=SharedPalette.Read(PalettePath(root,reference));
        if(image.Indices.Length!=Count(image.Width,image.Height))throw new InvalidDataException("Index count does not match dimensions.");
        var packed=Pack(image.Indices);
        byte[] Encode(EncodingMethod kind)
        {
            if(kind==EncodingMethod.Packed)return packed;
            using var output=new MemoryStream();
            if(kind==EncodingMethod.Brotli)
            {
                using(var brotli=new BrotliStream(output,CompressionLevel.SmallestSize,true))brotli.Write(packed);
            }
            else if(kind==EncodingMethod.Runs)
            {
                for(int i=0;i<image.Indices.Length;)
                {
                    int n=1;
                    while(n<256&&i+n<image.Indices.Length&&image.Indices[i+n]==image.Indices[i])n++;
                    output.WriteByte((byte)(n-1));output.WriteByte(image.Indices[i]);i+=n;
                }
            }
            else throw new InvalidDataException("Unsupported encoding.");
            return output.ToArray();
        }
        var options=method==EncodingMethod.Auto?new[]{EncodingMethod.Packed,EncodingMethod.Runs,EncodingMethod.Brotli}:new[]{method};
        var chosen=options.Select(kind=>(Kind:kind,Bytes:Encode(kind))).MinBy(item=>item.Bytes.Length);
        byte[] name=Utf8.GetBytes(reference);
        if(name.Length is <1 or >1024)throw new InvalidDataException("Palette reference is too long.");
        using var writer=new BinaryWriter(new FileStream(path,FileMode.CreateNew));
        writer.Write("PFI1"u8);writer.Write((byte)1);writer.Write((byte)chosen.Kind);
        writer.Write((ushort)image.Width);writer.Write((ushort)image.Height);
        writer.Write((ushort)name.Length);writer.Write(chosen.Bytes.Length);
        writer.Write(palette.Id.ToByteArray());writer.Write(SHA256.HashData(palette.Serialize()));writer.Write(SHA256.HashData(packed));
        writer.Write(name);writer.Write(chosen.Bytes);
        return(chosen.Kind,chosen.Bytes.Length);
    }

    public static (IndexedPixels Image,SharedPalette Palette) Read(string path,string root,string? replacementReference=null)
    {
        using var reader=new BinaryReader(File.OpenRead(path));
        if(reader.BaseStream.Length>2L*MaxPixels+2048 || !reader.ReadBytes(5).SequenceEqual(new byte[]{80,70,73,49,1}))
            throw new InvalidDataException("Invalid or unsupported PFI header.");
        var method=(EncodingMethod)reader.ReadByte();
        int w=reader.ReadUInt16(),h=reader.ReadUInt16(),count=Count(w,h);
        int nameSize=reader.ReadUInt16(),size=reader.ReadInt32(),packedSize=(count*5+7)/8;
        if(nameSize is <1 or >1024||size<1||size>count*2+64||reader.BaseStream.Length!=96L+nameSize+size||
           method is not (EncodingMethod.Packed or EncodingMethod.Runs or EncodingMethod.Brotli))
            throw new InvalidDataException("Invalid PFI lengths or encoding.");
        var id=new Guid(reader.ReadBytes(16));var hash=reader.ReadBytes(32);var checksum=reader.ReadBytes(32);
        string reference=Utf8.GetString(reader.ReadBytes(nameSize));
        string palettePath=PalettePath(root,reference);
        if(replacementReference is not null)palettePath=PalettePath(root,replacementReference);
        if(!File.Exists(palettePath))throw new FileNotFoundException("Required shared palette: "+(replacementReference??reference),palettePath);
        var palette=SharedPalette.Read(palettePath);
        if(replacementReference is null&&(palette.Id!=id||!SHA256.HashData(palette.Serialize()).SequenceEqual(hash)))
            throw new InvalidDataException("Shared palette changed: "+reference+". Use the original palette or explicitly rebind the indices.");
        var data=reader.ReadBytes(size);byte[] indices,packed;
        if(method==EncodingMethod.Runs)
        {
            indices=new byte[count];int cursor=0;
            if(data.Length%2!=0)throw new InvalidDataException("Truncated PFI run.");
            for(int i=0;i<data.Length;i+=2)
            {
                int run=data[i]+1;
                if(data[i+1]>31||run>count-cursor)throw new InvalidDataException("Invalid PFI run.");
                Array.Fill(indices,data[i+1],cursor,run);cursor+=run;
            }
            if(cursor!=count)throw new InvalidDataException("PFI runs do not fill the image.");
            packed=Pack(indices);
        }
        else
        {
            packed=data;
            if(method==EncodingMethod.Brotli)
            {
                packed=new byte[packedSize];
                using var decoder=new BrotliDecoder();
                if(decoder.Decompress(data,packed,out int consumed,out int written)!=System.Buffers.OperationStatus.Done||
                    consumed!=data.Length||written!=packed.Length)throw new InvalidDataException("Invalid PFI Brotli payload.");
            }
            indices=Unpack(packed,count);
        }
        if(!SHA256.HashData(packed).SequenceEqual(checksum))throw new InvalidDataException("PFI pixel checksum mismatch.");
        return(new(w,h,indices),palette);
    }
}
