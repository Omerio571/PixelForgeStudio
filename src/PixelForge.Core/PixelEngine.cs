using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;
public sealed record Raster(int Width, int Height, byte[] Pixels)
{
    public static Raster Read(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth*4, 0);
        return new(image.PixelWidth, image.PixelHeight, bytes);
    }
    public void Save(string path)
    {
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width*4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.CreateNew); encoder.Save(stream);
    }
    public Raster Enlarge(int zoom)
    {
        var result = new byte[Width*Height*zoom*zoom*4];
        for (int y=0;y<Height*zoom;y++) for(int x=0;x<Width*zoom;x++) Array.Copy(Pixels, ((y/zoom)*Width+x/zoom)*4, result, (y*Width*zoom+x)*4,4);
        return new(Width*zoom,Height*zoom,result);
    }
}
public sealed record PixelResult(Raster Image, int[] ObjectIds, string[] Warnings, Raster? OutlineLayer=null, Raster? BaseImage=null, Raster? HighlightLayer=null, int[]? OutlineIds=null, int[]? GroupKeys=null)
{
    public int[] ContourIds=>OutlineIds??ObjectIds;
}
public static class PixelEngine
{
    public static PixelResult Convert(Raster source, Raster ids, AssetSettings settings, IReadOnlyList<int>? knownIds=null, SurfaceFrame? surfaces=null, IReadOnlyDictionary<int,int>? outlineGroups=null)
    {
        settings.Validate(); int w=settings.Width,h=settings.Height,s=settings.SamplesPerPixel;
        if(source.Width!=w*s || source.Height!=h*s || ids.Width!=source.Width || ids.Height!=source.Height) throw new InvalidDataException("Render dimensions do not match the conversion job.");
        var palette=settings.Colors.Select(Palette.ParseColor).ToArray(); var lab=palette.Select(c=>Lab(c.R,c.G,c.B)).ToArray();
        var pixels=new byte[w*h*4]; var labels=new int[w*h]; var used=new HashSet<uint>();
        var idCache=new Dictionary<int,int>();
        int Identify(int color)
        {
            if(knownIds is null || knownIds.Count==0)return color;
            if(idCache.TryGetValue(color,out var value))return value;
            static int Distance(int a,int b) => ((a&255)-(b&255))*((a&255)-(b&255))+(((a>>8)&255)-((b>>8)&255))*(((a>>8)&255)-((b>>8)&255))+((a>>16)-(b>>16))*((a>>16)-(b>>16));
            value=knownIds.MinBy(id=>Distance(id,color));idCache[color]=value;return value;
        }
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            var samples=new List<(int Pos,int Id)>();
            for(int dy=0;dy<s;dy++) for(int dx=0;dx<s;dx++)
            {
                int p=((y*s+dy)*source.Width+x*s+dx)*4;
                if(source.Pixels[p+3]>=128 && ids.Pixels[p+3]>=128) samples.Add((p,Identify(ids.Pixels[p+2] | ids.Pixels[p+1]<<8 | ids.Pixels[p]<<16)));
            }
            if(samples.Count*2<s*s) continue;
            int id=samples.GroupBy(v=>v.Id).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).First().Key;
            var chosen=samples.Where(v=>v.Id==id).ToArray();
            double r=0,g=0,b=0,weight=0;
            foreach(var item in chosen) { var p=item.Pos; double a=source.Pixels[p+3]/255.0; r+=source.Pixels[p+2]*a; g+=source.Pixels[p+1]*a; b+=source.Pixels[p]*a; weight+=a; }
            r/=weight;g/=weight;b/=weight;
            // Restrained stepped luminance before perceptual palette mapping.
            var target=Lab(r,g,b); target.L=Math.Round(target.L*12)/12;
            int best=0; double distance=double.MaxValue;
            for(int i=0;i<lab.Length;i++) { double d=2*Math.Pow(target.L-lab[i].L,2)+Math.Pow(target.A-lab[i].A,2)+Math.Pow(target.B-lab[i].B,2); if(d<distance){distance=d;best=i;} }
            int dest=(y*w+x)*4; var c=palette[best]; pixels[dest]=c.B;pixels[dest+1]=c.G;pixels[dest+2]=c.R;pixels[dest+3]=255;labels[y*w+x]=id;
        }
        // Only correct isolated interior shading pixels within the same visible object.
        var clean=(byte[])pixels.Clone();
        for(int y=1;y<h-1;y++) for(int x=1;x<w-1;x++)
        {
            int n=y*w+x,p=n*4; if(pixels[p+3]==0) continue;
            var neighbors=new List<uint>(); bool interior=true;
            for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++) { if(dx==0&&dy==0)continue; int j=(y+dy)*w+x+dx; if(pixels[j*4+3]==0||labels[j]!=labels[n]){interior=false;break;} neighbors.Add(ColorAt(pixels,j*4)); }
            if(!interior)continue;
            var group=neighbors.GroupBy(v=>v).OrderByDescending(v=>v.Count()).First();
            if(group.Count()>=7 && !neighbors.Contains(ColorAt(pixels,p))) {clean[p]=(byte)group.Key;clean[p+1]=(byte)(group.Key>>8);clean[p+2]=(byte)(group.Key>>16);}
        }
        var warnings=new List<string>();
        bool clipped=false;
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) { int p=(y*w+x)*4; if(clean[p+3]==0)continue; used.Add(ColorAt(clean,p)); if(x==0||y==0||x==w-1||y==h-1)clipped=true; }
        if(used.Count==0&&!settings.Animation)throw new InvalidDataException("The render produced no visible pixels. Check geometry, camera and scale.");
        if(clipped) warnings.Add("Opaque pixels touch the canvas edge; increase padding and review clipping.");
        if(used.Count<3)warnings.Add("Fewer than three palette shades are used; shading or material separation may be limited.");
        var grouped=settings.RespectOutlineGroups&&outlineGroups is not null?labels.Select(id=>outlineGroups.TryGetValue(id,out int group)?group:id).ToArray():null;
        var result=new PixelResult(new(w,h,clean),labels,warnings.ToArray(),OutlineIds:grouped,GroupKeys:grouped is null?null:outlineGroups!.Values.Distinct().ToArray());
        if (!(settings.Outlines && settings.OutlineType==OutlineStyle.Selective)) result=OutlineEngine.Apply(result,settings);
        return EdgeEffects.Apply(result,settings,surfaces);
    }
    public static uint ColorAt(byte[] bytes,int p) => (uint)(bytes[p+2]<<16|bytes[p+1]<<8|bytes[p]);
    public static void Validate(Raster image, AssetSettings settings)
    {
        if(image.Width!=settings.Width||image.Height!=settings.Height)throw new InvalidDataException("Wrong output dimensions.");
        var allowed=settings.Colors.Select(Palette.ParseColor).Select(c=>c.Packed).ToHashSet();
        for(int p=0;p<image.Pixels.Length;p+=4) { if(image.Pixels[p+3] is not (0 or 255))throw new InvalidDataException("Non-binary alpha."); if(image.Pixels[p+3]==255&&!allowed.Contains(ColorAt(image.Pixels,p)))throw new InvalidDataException("Color outside approved palette."); }
        if(settings.Padding>0&&!Enumerable.Range(0,image.Width*image.Height).Any(i=>image.Pixels[i*4+3]==0))throw new InvalidDataException("Output has no transparent background. Increase padding.");
    }
    private static (double L,double A,double B) Lab(double r,double g,double b)
    {
        static double Linear(double c){c/=255;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        r=Linear(r);g=Linear(g);b=Linear(b);
        double l=Math.Cbrt(.4122214708*r+.5363325363*g+.0514459929*b),m=Math.Cbrt(.2119034982*r+.6806995451*g+.1073969566*b),s=Math.Cbrt(.0883024619*r+.2817188376*g+.6299787005*b);
        return (.2104542553*l+.793617785*m-.0040720468*s,1.9779984951*l-2.428592205*m+.4505937099*s,.0259040371*l+.7827717662*m-.808675766*s);
    }
}
