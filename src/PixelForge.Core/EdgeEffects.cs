using System.IO;
using System.Numerics;
using System.Text.Json;

namespace PixelForge.Core;

public readonly record struct SurfacePoint(int ObjectId,Vector3 Normal,double Depth,double Metallic,double Roughness,double KeyVisibility);
public sealed record SurfaceFrame(int Width,int Height,double WorldUnitsPerPixel,Vector3 Light,SurfacePoint?[] Pixels)
{
    public static SurfaceFrame Read(string path,AssetSettings settings)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
        var light=root.GetProperty("light").EnumerateArray().Select(n=>n.GetSingle()).ToArray();
        var pixels=root.GetProperty("pixels").EnumerateArray().Select(p=>
        {
            if(p.ValueKind==JsonValueKind.Null)return (SurfacePoint?)null;
            var a=p.EnumerateArray().Select(n=>n.GetDouble()).ToArray();
            if(a.Length!=8||a.Any(n=>!double.IsFinite(n)))throw new InvalidDataException("Invalid edge geometry sample.");
            return new SurfacePoint((int)a[0],new((float)a[1],(float)a[2],(float)a[3]),a[4],a[5],a[6],a[7]);
        }).ToArray();
        var result=new SurfaceFrame(root.GetProperty("width").GetInt32(),root.GetProperty("height").GetInt32(),root.GetProperty("worldUnitsPerPixel").GetDouble(),new(light[0],light[1],light[2]),pixels);
        if(result.Width!=settings.Width||result.Height!=settings.Height||pixels.Length!=settings.Width*settings.Height||!double.IsFinite(result.WorldUnitsPerPixel)||result.WorldUnitsPerPixel<=0)
            throw new InvalidDataException("Edge geometry does not match the final pixel grid.");
        return result;
    }
}

public static class EdgeEffects
{
    private static readonly (int X,int Y)[] Neighbors=[(-1,0),(1,0),(0,-1),(0,1)];
    public static PixelResult Apply(PixelResult source,AssetSettings settings,SurfaceFrame? geometry)
    {
        bool selective=settings.Outlines&&settings.OutlineType==OutlineStyle.Selective;
        bool highlights=!settings.DynamicLighting&&settings.EdgeHighlights&&settings.EdgeHighlightStrength>0;
        if(!selective&&!highlights)return source;
        if(geometry is null)throw new InvalidDataException("Selective outlines and highlights require evaluated Blender surface data. Generate a new asset; saved artwork is never modified.");
        int w=source.Image.Width,h=source.Image.Height,size=w*h;
        if(geometry.Width!=w||geometry.Height!=h||geometry.Pixels.Length!=size)throw new InvalidDataException("Wrong surface grid dimensions.");
        var original=source.BaseImage??source.Image;
        var composite=(byte[])source.Image.Pixels.Clone();var outlines=selective?new byte[size*4]:source.OutlineLayer?.Pixels;
        var accents=new byte[size*4];var outer=new bool[size];var seams=new bool[size];var highlightSeams=new bool[size];var facets=new bool[size];var facing=new double[size];var valid=new bool[size];
        var palette=settings.Colors.Select(Palette.ParseColor).ToArray();
        for(int n=0;n<size;n++)
            if(original.Pixels[n*4+3]!=0&&geometry.Pixels[n] is {} p&&p.ObjectId==source.ObjectIds[n])
            {valid[n]=true;facing[n]=settings.DynamicLighting?0:Math.Max(0,Vector3.Dot(p.Normal,geometry.Light))*p.KeyVisibility;}
        IEnumerable<int> Adjacent(int n)
        {
            int x=n%w,y=n/w;
            foreach(var (dx,dy) in Neighbors){int nx=x+dx,ny=y+dy;if(nx>=0&&nx<w&&ny>=0&&ny<h)yield return ny*w+nx;}
        }
        for(int n=0;n<size;n++)
        {
            if(!valid[n])continue;var p=geometry.Pixels[n]!.Value;int x=n%w,y=n/w;
            outer[n]=x==0||y==0||x==w-1||y==h-1;
            foreach(int j in Adjacent(n))
            {
                if(original.Pixels[j*4+3]==0){outer[n]=true;continue;}
                if(!valid[j])continue;var q=geometry.Pixels[j]!.Value;
                if(p.ObjectId!=q.ObjectId)
                {
                    // One foreground side only. Depth ties use stable component identity.
                    if(p.Depth<q.Depth-geometry.WorldUnitsPerPixel*.1 || Math.Abs(p.Depth-q.Depth)<=geometry.WorldUnitsPerPixel*.1&&p.ObjectId>q.ObjectId)
                    {
                        highlightSeams[n]=true;
                        if(source.ContourIds[n]!=source.ContourIds[j])seams[n]=true;
                    }
                }
                else if(Vector3.Dot(p.Normal,q.Normal)<.72 && facing[n]>facing[j]+.12)
                    facets[n]=true; // Creases, never every polygon edge or shading-band transition.
            }
        }
        var internalEdges=GroupSilhouette.InternalEdges(source,settings);
        var contour=outer.Zip(seams,(a,b)=>a||b).Select((edge,n)=>edge&&(internalEdges is null||!internalEdges[n])).ToArray();
        if(selective&&settings.OutlineThickness==2)
        {
            var expanded=(bool[])contour.Clone();
            for(int n=0;n<size;n++)if(contour[n])foreach(int j in Adjacent(n))if(valid[j]&&source.ContourIds[j]==source.ContourIds[n])expanded[j]=true;
            contour=expanded;
        }
        if(selective)
        {
            for(int n=0;n<size;n++)if(contour[n]&&valid[n])
            {
                // Leave the base visible only on a well-supported, strongly lit outer run.
                int support=Adjacent(n).Count(j=>outer[j]&&valid[j]&&source.ContourIds[j]==source.ContourIds[n]&&facing[j]>.82);
                if(outer[n]&&!seams[n]&&facing[n]>.92&&support>=2)continue;
                double factor=outer[n]?(.46+.48*facing[n]):(.66+.28*facing[n]);
                var color=Shade(original,n,palette,factor,false);Write(outlines!,n,color);Write(composite,n,color);
            }
        }
        if(highlights&&Math.Min(w,h)>32&&settings.LightIntensity>0)
        {
            var candidate=new bool[size];var colors=new Rgb[size];
            for(int n=0;n<size;n++)if(valid[n]&&(outer[n]||highlightSeams[n]||facets[n]))
            {
                var p=geometry.Pixels[n]!.Value;
                // Broad confidence bands, no noise/dithering/screen-space pattern or temporal state.
                // Small canvases require stronger incidence and a longer connected edge.
                double incidence=Math.Min(w,h)<=64?.8:.68;
                if(facing[n]<incidence)continue;
                int litNeighbors=Adjacent(n).Count(j=>valid[j]&&source.ObjectIds[j]==source.ObjectIds[n]&&facing[j]>incidence-.12);
                double materialGain=p.Metallic>=.45?1.3:.6;
                double factor=1+settings.EdgeHighlightStrength*materialGain*facing[n];
                colors[n]=Shade(original,n,palette,factor,true);
                candidate[n]=litNeighbors>=2&&colors[n].Packed!=PixelEngine.ColorAt(original.Pixels,n*4)
                    &&!(outlines is not null&&outlines[n*4+3]!=0&&outer[n]&&seams[n]);
            }
            // Connected edge runs eliminate isolated speckles without thickening the contour.
            var visited=new bool[size];
            for(int n=0;n<size;n++)if(candidate[n]&&!visited[n])
            {
                var cluster=new List<int>();var queue=new Queue<int>();queue.Enqueue(n);visited[n]=true;
                while(queue.TryDequeue(out int j))
                {
                    cluster.Add(j);int x=j%w,y=j/w;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=w||yy>=h)continue;int k=yy*w+xx;if(!visited[k]&&candidate[k]&&source.ObjectIds[k]==source.ObjectIds[n]){visited[k]=true;queue.Enqueue(k);}}
                }
                if(cluster.Count<(Math.Min(w,h)<=64?4:2))continue;
                foreach(int j in cluster)
                {
                    Write(accents,j,colors[j]);Write(composite,j,colors[j]);
                }
            }
        }
        return source with{Image=new(w,h,composite),BaseImage=original,OutlineLayer=outlines is null?null:new(w,h,outlines),HighlightLayer=highlights?new(w,h,accents):null};
    }
    private static void Write(byte[] image,int n,Rgb c){int p=n*4;image[p]=c.B;image[p+1]=c.G;image[p+2]=c.R;image[p+3]=255;}
    private static Rgb Shade(Raster image,int n,Rgb[] palette,double factor,bool brighter)
    {
        int p=n*4;var basis=new Rgb(image.Pixels[p+2],image.Pixels[p+1],image.Pixels[p]);
        static double L(Rgb c)=>.2126*c.R+.7152*c.G+.0722*c.B;
        static (double R,double G,double B) Hue(Rgb c){double sum=Math.Max(1,c.R+c.G+c.B);return(c.R/sum,c.G/sum,c.B/sum);}
        double lum=L(basis),target=Math.Clamp(lum*factor,0,255);var hue=Hue(basis);
        Rgb best=basis;double score=Math.Pow(lum-target,2);
        foreach(var c in palette)
        {
            double l=L(c);if(brighter?l<=lum:l>=lum)continue;
            var ch=Hue(c);double chroma=Math.Pow(hue.R-ch.R,2)+Math.Pow(hue.G-ch.G,2)+Math.Pow(hue.B-ch.B,2);
            // Preserve each material's palette ramp; neutral stone/metal stay neutral.
            if(chroma>.028)continue;
            double d=Math.Pow(l-target,2)+chroma*100000;
            if(d<score){best=c;score=d;}
        }
        return best;
    }
}
