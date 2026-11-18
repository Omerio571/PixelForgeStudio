using System.Buffers;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PixelForge.Core.IndexedLab;

namespace PixelForge.Core;

public enum PfaLayer { Base, Normal, Emission }
public enum PfaCompression { Auto, Raw, Brotli }
public sealed record AssetExportProfile(string Name="Hollow Core Default",string Root="",string Category="Machines",
    string BasePalette="",string NormalPalette="",string EmissionPalette="",bool Base=true,bool Normal=true,bool Emission=true,
    bool AllDirections=true,PfaCompression Compression=PfaCompression.Auto,bool PngPreviews=false,string BasePng="",string NormalPng="",string EmissionPng="",bool ExportPfa=true,bool ExportPng=false,bool ExportWebp=false,string Cwebp="",string Dwebp="",string Cjxl="",string Djxl="",bool DetailedReport=false,bool IncludeGameMetadata=false,int WebpEffort=9);
public sealed record PfaInfo(string Asset,PfaLayer Layer,string Direction,int Width,int Height,int[] Durations,
    Guid RootId,string Palette,string PaletteHash,JsonElement Placement);
public sealed record PfaTrial(string Storage,string Codec,int StorageBytes,int PayloadBytes,int FileBytes);
public sealed record PfaImage(PfaInfo Info,Raster[] Frames,string Storage,string Codec,long FileBytes,byte[]? Indices=null,int MetadataBytes=0,int PayloadBytes=0,int StorageBytes=0,double EncodeMs=0,double DecodeMs=0,IReadOnlyList<PfaTrial>? Trials=null);

public static class Pfa
{
    private const string Marker=".pixelforge-assets.json";
    private const int Limit=16_777_216;
    private enum Storage : byte { Bgra, GrayAlpha, Index5, IndexAlpha, Index8, PlanarBgra, PlanarGrayAlpha }

    public static Guid RootId(string root,bool create=false)
    {
        string path=Path.Combine(root,Marker);
        if(!File.Exists(path)&&create)
        {
            Directory.CreateDirectory(root);
            using var stream=new FileStream(path,FileMode.CreateNew);
            JsonSerializer.Serialize(stream,new {Id=Guid.NewGuid()});
        }
        if(!File.Exists(path))throw new FileNotFoundException("Assets root marker is missing: "+path);
        using var input=File.OpenRead(path);
        if(input.Length>4096)throw new InvalidDataException("Assets root marker is too large.");
        using var json=JsonDocument.Parse(input);
        return json.RootElement.GetProperty("Id").GetGuid();
    }

    public static string Segment(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>120||value.Any(c=>c<32||"<>:\"/\\|?*".Contains(c))||
           value is "." or ".."||value.EndsWith('.')||value.EndsWith(' '))
            throw new InvalidDataException("Use a simple file or folder name: "+value);
        return value;
    }

    public static string PalettePath(string root,string reference)
    {
        var parts=reference.Split('/');
        if(parts.Length is <1 or >2||(parts.Length==2&&parts[0]!="Palettes")||
           !reference.EndsWith(".gpl",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use a GPL palette in the Assets root or its Palettes folder.");
        foreach(var part in parts)Segment(part);
        string path=Path.GetFullPath(root);
        foreach(var part in parts)
        {
            path=Path.Combine(path,part);
            if(Path.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Linked palette paths are unsupported.");
        }
        return path;
    }

    private static (Rgb[] Colors,string Hash) ReadPalette(string root,string reference)
    {
        string path=PalettePath(root,reference);
        if(!File.Exists(path))throw new FileNotFoundException("Required GPL palette: "+reference,path);
        if(new FileInfo(path).Length>262144)throw new InvalidDataException("GPL palette exceeds 256 KiB.");
        var colors=Palette.Load(path);
        return(colors,Convert.ToHexString(SHA256.HashData(colors.SelectMany(c=>new[]{c.R,c.G,c.B}).ToArray())));
    }

    private static int PixelCount(PfaInfo info)
    {
        if(info.Durations is null||info.Asset is null||info.Palette is null||info.Direction is null||info.Placement.ValueKind!=JsonValueKind.Object||info.Width is <1 or >4096||info.Height is <1 or >4096||info.Durations.Length is <1 or >256||
           info.Durations.Any(t=>t is <1 or >600000)||!Enum.IsDefined(info.Layer)||
           (long)info.Width*info.Height*info.Durations.Length>Limit)
            throw new InvalidDataException("Unsupported PFA dimensions, timing or layer.");
        return info.Width*info.Height*info.Durations.Length;
    }

    public static PfaImage Write(string path,string root,PfaInfo info,Raster[] frames,PfaCompression mode,CancellationToken token=default,bool baseline=false)
    {
        var clock=Stopwatch.StartNew();
        int count=PixelCount(info);
        if(frames.Length!=info.Durations.Length||frames.Any(f=>f.Width!=info.Width||f.Height!=info.Height||f.Pixels.Length!=f.Width*f.Height*4))
            throw new InvalidDataException("Layer frames do not match PFA metadata.");
        if(!Enum.IsDefined(mode))throw new InvalidDataException("Unsupported compression mode.");
        var rgba=frames.SelectMany(f=>f.Pixels).ToArray();
        var candidates=new List<(Storage Kind,byte[] Data,string Palette,string Fingerprint)>{(Storage.Bgra,rgba,"","")};
        if(!baseline&&! (info.Layer==PfaLayer.Base))
            candidates.Add((Storage.PlanarBgra,Plane(rgba,count,4),"",""));
        if(info.Layer==PfaLayer.Emission)
        {
            var gray=new byte[count*2];bool exact=true;
            for(int i=0;i<count;i++)
            {
                int p=i*4;if(rgba[p]!=rgba[p+1]||rgba[p]!=rgba[p+2]){exact=false;break;}
                gray[i*2]=rgba[p];gray[i*2+1]=rgba[p+3];
            }
            if(exact)
            {
                candidates.Add((Storage.GrayAlpha,gray,"",""));
                if(!baseline)candidates.Add((Storage.PlanarGrayAlpha,Plane(gray,count,2),"",""));
            }
        }
        if(info.Palette.Length>0&&(info.Layer==PfaLayer.Base||File.Exists(PalettePath(root,info.Palette))))
        {
            var (colors,hash)=ReadPalette(root,info.Palette);
            bool basic=info.Layer==PfaLayer.Base;
            if(basic&&colors.Length!=32)throw new InvalidDataException("Base GPL must have 32 ordered entries, including the transparent placeholder.");
            if(colors.Length<=256)
            {
                var indices=new byte[count*(basic?1:2)];bool exact=true;
                var lookup=new Dictionary<uint,byte>();
                for(int n=basic?1:0;n<colors.Length;n++)lookup.TryAdd(colors[n].Packed,(byte)n);
                for(int i=0;i<count;i++)
                {
                    int p=i*4;uint rgb=PixelEngine.ColorAt(rgba,p);
                    if(basic&&rgba[p+3]==0&&rgb==0)continue;
                    if((basic&&rgba[p+3]!=255)||!lookup.TryGetValue(rgb,out byte index)){exact=false;break;}
                    indices[i*(basic?1:2)]=index;
                    if(!basic)indices[i*2+1]=rgba[p+3];
                }
                if(exact)
                {
                    candidates.Add((basic?Storage.Index5:Storage.IndexAlpha,basic?IndexedCodec.Pack(indices):indices,info.Palette,hash));
                    if(basic&&!baseline)candidates.Add((Storage.Index8,indices,info.Palette,hash));
                }
            }
        }
        byte[]? best=null;byte[]? header=null;Storage storage=Storage.Bgra;byte codec=0;
        var trials=new List<PfaTrial>();
        foreach(var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            byte[] metadata=JsonSerializer.SerializeToUtf8Bytes(info with{Palette=candidate.Palette,PaletteHash=candidate.Fingerprint});
            if(metadata.Length>65536)throw new InvalidDataException("PFA metadata is too large.");
            void Consider(byte[] payload,byte method)
            {
                trials.Add(new(candidate.Kind.ToString(),method switch {0=>"Raw",1=>"Brotli",_=>"FrameXorBrotli"},candidate.Data.Length,payload.Length,48+payload.Length+metadata.Length));
                if(best is not null&&best.Length+header!.Length<=payload.Length+metadata.Length)return;
                best=payload;header=metadata;storage=candidate.Kind;codec=method;
            }
            if(mode!=PfaCompression.Brotli)Consider(candidate.Data,0);
            if(mode!=PfaCompression.Raw)
            {
                byte[] Compress(byte[] data)
                {
                    using var buffer=new MemoryStream();
                    using(var brotli=new BrotliStream(buffer,CompressionLevel.Optimal,true))
                        for(int i=0;i<data.Length;i+=65536)
                        {
                            token.ThrowIfCancellationRequested();
                            brotli.Write(data.AsSpan(i,Math.Min(65536,data.Length-i)));
                        }
                    return buffer.ToArray();
                }
                Consider(Compress(candidate.Data),1);
                if(!baseline&&frames.Length>1&&FrameAligned(candidate.Kind))
                {
                    var xor=(byte[])candidate.Data.Clone();
                    int stride=xor.Length/frames.Length;
                    for(int i=xor.Length-1;i>=stride;i--)xor[i]^=xor[i-stride];
                    Consider(Compress(xor),2);
                }
            }
        }
        using(var output=new BinaryWriter(new FileStream(path,FileMode.CreateNew)))
        {
            output.Write("PFA1"u8);output.Write((byte)1);output.Write((byte)storage);output.Write(codec);output.Write((byte)1);
            output.Write(header!.Length);output.Write(best!.Length);
            output.Write(Digest(header,rgba));output.Write(header);output.Write(best);
        }
        double encode=clock.Elapsed.TotalMilliseconds;
        var verified=Read(path,root);
        if(!verified.Frames.SelectMany(f=>f.Pixels).SequenceEqual(rgba))
            throw new InvalidDataException("PFA round-trip verification failed.");
        return verified with {EncodeMs=encode,Trials=trials};
    }

    private static bool FrameAligned(Storage storage)=>storage is Storage.Bgra or Storage.GrayAlpha or Storage.IndexAlpha or Storage.Index8;

    private static byte[] Plane(byte[] interleaved,int count,int channels)
    {
        var result=new byte[interleaved.Length];
        for(int i=0;i<count;i++)for(int c=0;c<channels;c++)result[c*count+i]=interleaved[i*channels+c];
        return result;
    }

    private static byte[] Digest(byte[] header,byte[] rgba)
    {
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(header);digest.AppendData(rgba);return digest.GetHashAndReset();
    }

    public static PfaImage Read(string path,string? rootOverride=null)
    {
        var clock=Stopwatch.StartNew();
        using var input=new BinaryReader(File.OpenRead(path));
        if(!input.ReadBytes(5).SequenceEqual("PFA1\x01"u8))throw new InvalidDataException("Unsupported PFA container version.");
        var storage=(Storage)input.ReadByte();byte codec=input.ReadByte(),version=input.ReadByte();
        int headerLength=input.ReadInt32(),payloadLength=input.ReadInt32();
        if(!Enum.IsDefined(storage)||codec>2||version!=1||headerLength is <1 or >65536||payloadLength is <1 or >Limit*4+65536||
           input.BaseStream.Length!=48L+headerLength+payloadLength)
            throw new InvalidDataException("Invalid PFA codec or lengths.");
        byte[] checksum=input.ReadBytes(32),header=input.ReadBytes(headerLength);
        var info=JsonSerializer.Deserialize<PfaInfo>(header)??throw new InvalidDataException("Missing PFA metadata.");
        int count=PixelCount(info),length=storage switch { Storage.Bgra or Storage.PlanarBgra=>count*4,Storage.Index5=>(count*5+7)/8,Storage.Index8=>count,_=>count*2 };
        if(codec==0&&payloadLength!=length)throw new InvalidDataException("Incorrect PFA payload length.");
        var payload=input.ReadBytes(payloadLength);
        if(codec==2&&(!FrameAligned(storage)||info.Durations.Length<2))throw new InvalidDataException("Frame XOR requires frame-aligned animated storage.");
        if(codec!=0)
        {
            var unpacked=new byte[length];using var decoder=new BrotliDecoder();
            if(decoder.Decompress(payload,unpacked,out int read,out int wrote)!=OperationStatus.Done||
               read!=payload.Length||wrote!=length)throw new InvalidDataException("Invalid Brotli payload.");
            payload=unpacked;
            if(codec==2)
            {
                int stride=payload.Length/info.Durations.Length;
                for(int i=stride;i<payload.Length;i++)payload[i]^=payload[i-stride];
            }
        }
        byte[] rgba=payload;
        byte[]? indices=storage is Storage.Index5 or Storage.IndexAlpha or Storage.Index8?new byte[count]:null;
        if(storage==Storage.Index5&&count*5%8!=0&&payload[^1]>>(count*5%8)!=0)
            throw new InvalidDataException("Invalid packed-index padding.");
        if(storage==Storage.PlanarBgra)
        {
            rgba=new byte[count*4];
            for(int i=0;i<count;i++)for(int c=0;c<4;c++)rgba[i*4+c]=payload[c*count+i];
        }
        else if(storage!=Storage.Bgra)
        {
            Rgb[] colors=[];
            if(storage is not (Storage.GrayAlpha or Storage.PlanarGrayAlpha))
            {
                string? root=rootOverride;
                if(root is null)
                    for(var dir=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);dir is not null;dir=dir.Parent)
                        if(File.Exists(Path.Combine(dir.FullName,Marker))&&RootId(dir.FullName)==info.RootId){root=dir.FullName;break;}
                if(root is null)throw new InvalidDataException("Locate the Assets root for palette "+info.Palette+".");
                var palette=ReadPalette(root,info.Palette);
                if(palette.Hash!=info.PaletteHash)throw new InvalidDataException("GPL palette colors or ordering changed: "+info.Palette);
                colors=palette.Colors;
                if((storage is Storage.Index5 or Storage.Index8)?colors.Length!=32:colors.Length>256)
                    throw new InvalidDataException("GPL entry count does not match indexed PFA storage.");
            }
            rgba=new byte[count*4];
            for(int i=0;i<count;i++)
            {
                int p=i*4;
                if(storage==Storage.GrayAlpha)
                {rgba[p]=rgba[p+1]=rgba[p+2]=payload[i*2];rgba[p+3]=payload[i*2+1];continue;}
                if(storage==Storage.PlanarGrayAlpha)
                {rgba[p]=rgba[p+1]=rgba[p+2]=payload[i];rgba[p+3]=payload[count+i];continue;}
                int bit=i*5,shift=bit%8;
                int index=storage==Storage.IndexAlpha?payload[i*2]:storage==Storage.Index8?payload[i]:
                    ((payload[bit/8]>>shift)|(shift>3?payload[bit/8+1]<<(8-shift):0))&31;
                indices![i]=(byte)index;
                if((storage is Storage.Index5 or Storage.Index8)&&index==0)continue;
                if(index>=colors.Length)throw new InvalidDataException("Invalid palette index.");
                var color=colors[index];rgba[p]=color.B;rgba[p+1]=color.G;rgba[p+2]=color.R;
                rgba[p+3]=storage==Storage.IndexAlpha?payload[i*2+1]:(byte)255;
            }
        }
        if(!Digest(header,rgba).SequenceEqual(checksum))throw new InvalidDataException("PFA image or metadata checksum mismatch.");
        int frameSize=info.Width*info.Height*4;
        return new(info,Enumerable.Range(0,info.Durations.Length).Select(i=>new Raster(info.Width,info.Height,rgba.AsSpan(i*frameSize,frameSize).ToArray())).ToArray(),
            storage.ToString(),codec switch {0=>"Raw v1",1=>"Brotli v1",_=>"FrameXorBrotli v1"},input.BaseStream.Length,indices,headerLength,payloadLength,length,0,clock.Elapsed.TotalMilliseconds);
    }
}
