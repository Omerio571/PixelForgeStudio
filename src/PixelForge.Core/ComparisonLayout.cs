using System.IO;
using System.Text.Json;

namespace PixelForge.Core;

public readonly record struct ImagePlacement(double X,double Y,int Width,int Height);
public sealed record ComparisonLayout(double Width,double Height,ImagePlacement A,ImagePlacement B,bool GroundAligned)
{
    public static ComparisonLayout Create(int aw,int ah,int bw,int bh,(double X,double Y)? anchorA,(double X,double Y)? anchorB)
    {
        bool grounded=anchorA is not null&&anchorB is not null;
        var aa=grounded?anchorA!.Value:(aw/2.0,ah/2.0);var bb=grounded?anchorB!.Value:(bw/2.0,bh/2.0);
        double left=Math.Max(aa.Item1,bb.Item1),top=Math.Max(aa.Item2,bb.Item2);
        var a=new ImagePlacement(left-aa.Item1,top-aa.Item2,aw,ah);var b=new ImagePlacement(left-bb.Item1,top-bb.Item2,bw,bh);
        return new(Math.Max(a.X+aw,b.X+bw),Math.Max(a.Y+ah,b.Y+bh),a,b,grounded);
    }
    public static (double X,double Y)? ReadAnchor(string? metadata)
    {
        if(metadata is null)return null;
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(metadata));
            if(!json.RootElement.TryGetProperty("anchor",out var a)&&!json.RootElement.TryGetProperty("pivot",out a))return null;
            double x=a.GetProperty("x").GetDouble(),y=a.GetProperty("y").GetDouble();
            return double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x)<1000000&&Math.Abs(y)<1000000?(x,y):null;
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or UnauthorizedAccessException){return null;}
    }
    public static int FrameAtTime(IReadOnlyList<FrameOutput> frames,long elapsed)
    {
        long end=0;for(int n=0;n<frames.Count;n++){end+=frames[n].DurationMs;if(elapsed<end)return n;}return Math.Max(0,frames.Count-1);
    }
}
