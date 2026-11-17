using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;

namespace PixelForge.Core;

public static class LightingMaps
{
    public static byte Encode(float value)=> (byte)Math.Clamp((int)MathF.Round((value+1)*127.5f),0,255);

    internal static Raster CreateFrame(string samples,Raster color,int[] ids)
    {
        int count=color.Width*color.Height;
        var normals=new Vector3[count];
        using var reader=new BinaryReader(File.OpenRead(samples));
        for(int i=0;i<count;i++)
        {
            int candidates=reader.ReadByte();
            if(candidates>9)throw new InvalidDataException("Invalid normal sample count.");
            for(int n=0;n<candidates;n++)
            {
                int id=reader.ReadInt32();
                var normal=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                if(id==ids[i])normals[i]=normal;
            }
        }
        if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Geometry normals do not match the sprite grid.");

        var data=new byte[color.Pixels.Length];
        for(int i=0;i<count;i++)
        {
            if(color.Pixels[i*4+3]==0)continue;
            Vector3 normal=normals[i];
            if(!Valid(normal))
            {
                // Renderer coverage and analytic ray centers can disagree at the silhouette.
                // Borrow the closest sample from the same visible object only.
                int x=i%color.Width,y=i/color.Width,best=10;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                {
                    int xx=x+dx,yy=y+dy,distance=dx*dx+dy*dy;
                    if(xx<0||xx>=color.Width||yy<0||yy>=color.Height||distance>=best)continue;
                    int j=yy*color.Width+xx;
                    if(ids[j]!=ids[i]||!Valid(normals[j]))continue;
                    normal=normals[j];best=distance;
                }
            }
            if(!Valid(normal))throw new InvalidDataException($"No matching geometry normal at pixel ({i%color.Width}, {i/color.Width}). Try coverage sampling or a larger canvas.");
            normal=Vector3.Normalize(normal);
            data[i*4]=Encode(normal.Z);data[i*4+1]=Encode(normal.Y);
            data[i*4+2]=Encode(normal.X);data[i*4+3]=255;
        }
        return new(color.Width,color.Height,data);
    }

    private static bool Valid(Vector3 n)=>float.IsFinite(n.LengthSquared())&&n.LengthSquared()>.01f;

    internal static string? CreateAtlas(AssetSettings settings,string directory,FrameOutput[] frames)
    {
        if(!settings.DynamicLighting)return null;
        var (cols,rows)=AnimationExport.Grid(settings.Width,settings.Height,frames.Length);
        int width=cols*settings.Width,height=rows*settings.Height;
        var pixels=new byte[width*height*4];
        for(int i=0;i<frames.Length;i++)
        {
            var color=Raster.Read(frames[i].Png);
            var normal=Raster.Read(frames[i].Normal!);
            if(color.Width!=normal.Width|| color.Height!=normal.Height)
                throw new InvalidDataException("Color and normal dimensions differ.");
            for(int p=3;p<color.Pixels.Length;p+=4)
                if(color.Pixels[p]!=normal.Pixels[p])throw new InvalidDataException("Color and normal alpha masks differ.");
            for(int y=0;y<normal.Height;y++)
                Array.Copy(normal.Pixels,y*normal.Width*4,pixels,
                    ((i/cols*settings.Height+y)*width+i%cols*settings.Width)*4,normal.Width*4);
        }
        string path=Path.Combine(directory,"asset.normal.sheet.png");
        new Raster(width,height,pixels).Save(path);
        return path;
    }

    public static string Export(ConversionOutput output,string destination)
    {
        if(output.NormalSheet is null||output.Metadata is null||output.Spritesheet is null)
            throw new InvalidOperationException("Generate a Dynamic Lighting Ready asset with valid maps first.");
        var settings=AssetSettings.Load(Path.Combine(output.Directory,"asset.settings.json"));
        var stem=Path.GetFileNameWithoutExtension(settings.Model)+(settings.Camera is {} camera?"_"+camera.Direction:"");
        string folder=Path.Combine(destination,stem+"-lighting-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string suffix=output.Frames.Length>1?".sheet.png":".png";
        string color=stem+".color"+suffix,normal=stem+".normal"+suffix;
        File.Copy(output.Spritesheet,Path.Combine(folder,color));
        File.Copy(output.NormalSheet,Path.Combine(folder,normal));
        File.Copy(output.Aseprite,Path.Combine(folder,stem+".aseprite"));
        var metadata=JsonNode.Parse(File.ReadAllText(output.Metadata))!.AsObject();
        metadata["image"]=color;
        var lighting=metadata["lighting"]!;
        lighting["color"]=color;lighting["normal"]=normal;
        File.WriteAllText(Path.Combine(folder,stem+".lighting.json"),metadata.ToJsonString(AssetSettings.JsonOptions));
        return folder;
    }
}
