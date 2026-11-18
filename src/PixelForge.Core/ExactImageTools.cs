using System.IO;
using System.Diagnostics;
using PixelForge.Core.IndexedLab;

namespace PixelForge.Core;

internal sealed class ExactImageTools(string work,CancellationToken token)
{
    private readonly Dictionary<string,string> versions=new();
    public static string? Resolve(string configured,string name)
    {
        if(configured.Length>0)return Path.IsPathFullyQualified(configured)&&File.Exists(configured)?configured:null;
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            var trimmed=directory.Trim('"');
            if(!Path.IsPathFullyQualified(trimmed))continue;
            string candidate=Path.Combine(trimmed,name+".exe");
            if(File.Exists(candidate))return candidate;
        }
        return null;
    }

    private void Run(string exe,string log,params string[] args)=>
        Toolchain.Run(exe,args,Path.Combine(work,log),token).GetAwaiter().GetResult();

    private string Version(string exe,string flag)
    {
        if(!versions.TryGetValue(exe,out string? version))
        {
            Run(exe,"version.log",flag);
            version=File.ReadLines(Path.Combine(work,"version.log")).FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s))?.Trim()??"Version not reported";
            versions[exe]=version;
        }
        return version;
    }

    public (string Versions,double Encode,double Decode) Webp(string encoder,string decoder,string png,string output,Raster atlas,int effort)
    {
        string version=Version(encoder,"-version")+" / "+Version(decoder,"-version");
        var watch=Stopwatch.StartNew();
        Run(encoder,"cwebp.log","-lossless","-z",effort.ToString(System.Globalization.CultureInfo.InvariantCulture),"-exact","-metadata","none",png,"-o",output);
        double encode=watch.Elapsed.TotalMilliseconds;
        ValidateWebp(output,atlas.Width,atlas.Height);
        watch.Restart();
        string decoded=Path.Combine(work,"decoded-webp.png");
        if(File.Exists(decoded))File.Delete(decoded);
        Run(decoder,"dwebp.log",output,"-o",decoded);
        if(IndexedBenchmark.Verify(atlas,ReadAtlas(decoded)) is not "PASS")throw new InvalidDataException("WebP decoded pixels differ from the canonical atlas.");
        return(version,encode,watch.Elapsed.TotalMilliseconds);
    }

    public (string Versions,double Encode,double Decode) Jxl(string encoder,string decoder,string png,string output,Raster atlas)
    {
        string version=Version(encoder,"--version")+" / "+Version(decoder,"--version");
        var watch=Stopwatch.StartNew();
        Run(encoder,"cjxl.log",png,output,"-d","0","-e","7","--keep_invisible=1");
        double encode=watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        string decoded=Path.Combine(work,"decoded-jxl.png");
        if(File.Exists(decoded))File.Delete(decoded);
        Run(decoder,"djxl.log",output,decoded);
        if(IndexedBenchmark.Verify(atlas,ReadAtlas(decoded)) is not "PASS")throw new InvalidDataException("JPEG XL decoded pixels differ from the canonical atlas.");
        return(version,encode,watch.Elapsed.TotalMilliseconds);
    }

    private static Raster ReadAtlas(string png)=>IndexedBenchmark.Load(png,8192);

    public static void ValidateExternalArtwork(string path)
    {
        using var input=File.OpenRead(path);
        Span<byte> tag=stackalloc byte[8];input.ReadExactly(tag);
        if(!tag.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("External base artwork must be a PNG.");
        while(input.Position<input.Length)
        {
            input.ReadExactly(tag);
            uint size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tag);
            long next=input.Position+size+4;
            if(next>input.Length)throw new InvalidDataException("Truncated external PNG chunk.");
            if(tag[4..].SequenceEqual("iCCP"u8)||tag[4..].SequenceEqual("cICP"u8))
                throw new InvalidDataException("External base PNG has a color profile. Convert it to sRGB before metadata-free WebP export.");
            if(tag[4..].SequenceEqual("gAMA"u8))
            {
                if(size!=4)throw new InvalidDataException("Invalid external PNG gamma chunk.");
                input.ReadExactly(tag[..4]);
                if(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tag)!=45455)
                    throw new InvalidDataException("External base PNG uses non-sRGB gamma. Normalize it before exporting WebP.");
            }
            if(tag[4..].SequenceEqual("cHRM"u8))
            {
                if(size!=32)throw new InvalidDataException("Invalid external PNG chromaticities.");
                foreach(uint expected in new uint[]{31270,32900,64000,33000,30000,60000,15000,6000})
                {
                    input.ReadExactly(tag[..4]);
                    if(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tag)!=expected)
                        throw new InvalidDataException("External base PNG uses non-sRGB chromaticities. Normalize it before exporting WebP.");
                }
            }
            input.Position=next;
        }
    }

    public static void ValidateWebp(string path,int width,int height)
    {
        using var reader=new BinaryReader(File.OpenRead(path));
        if(!reader.ReadBytes(4).SequenceEqual("RIFF"u8.ToArray())||
            reader.ReadUInt32()+8L!=reader.BaseStream.Length||
            !reader.ReadBytes(4).SequenceEqual("WEBP"u8.ToArray()))
            throw new InvalidDataException("Invalid WebP RIFF header.");
        bool lossless=false;
        while(reader.BaseStream.Position<reader.BaseStream.Length)
        {
            string id=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            long size=reader.ReadUInt32(),start=reader.BaseStream.Position;
            long next=start+size+(size&1);
            if(next>reader.BaseStream.Length)throw new InvalidDataException("Truncated WebP chunk.");
            if(id=="VP8L"&&!lossless&&size>=5)
            {
                if(reader.ReadByte()!=0x2f)throw new InvalidDataException("Invalid lossless WebP signature.");
                uint bits=reader.ReadUInt32();
                if((bits&16383)+1!=width||((bits>>14)&16383)+1!=height||bits>>29!=0)
                    throw new InvalidDataException("Incorrect WebP dimensions or version.");
                lossless=true;
            }
            else if(id=="VP8X"&&size==10&&!lossless)
            {
                if((reader.ReadByte()&~0x10)!=0)throw new InvalidDataException("Unexpected WebP features or metadata.");
            }
            else throw new InvalidDataException("Unexpected WebP chunk: "+id);
            reader.BaseStream.Position=next;
        }
        if(!lossless)throw new InvalidDataException("A lossless VP8L chunk is required.");
    }
}
