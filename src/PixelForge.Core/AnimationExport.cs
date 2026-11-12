using System.IO;
using System.Text.Json;

namespace PixelForge.Core;
public static class AnimationExport
{
    public static (int Columns,int Rows) Grid(int width,int height,int count)
    {
        int columns=Math.Min((int)Math.Ceiling(Math.Sqrt(count)),8192/width),rows=(count+columns-1)/columns;
        if(rows*height>8192)throw new ArgumentException("The fixed-grid spritesheet would exceed 8192 pixels. Reduce dimensions or frame count.");
        return(columns,rows);
    }
    internal static (string Sheet,string Metadata) Create(AssetSettings settings,string directory,FrameOutput[] frames,JsonElement manifest)
    {
        var (columns,rows)=Grid(settings.Width,settings.Height,frames.Length);
        int w=columns*settings.Width,h=rows*settings.Height;var bytes=new byte[w*h*4];
        for(int n=0;n<frames.Length;n++)
        {
            var image=Raster.Read(frames[n].Png);int x=n%columns*settings.Width,y=n/columns*settings.Height;
            for(int row=0;row<image.Height;row++)Array.Copy(image.Pixels,row*image.Width*4,bytes,((y+row)*w+x)*4,image.Width*4);
        }
        string sheet=Path.Combine(directory,"asset.sheet.png"),metadata=Path.Combine(directory,"asset.sheet.json");new Raster(w,h,bytes).Save(sheet);
        var first=manifest.GetProperty("frames")[0];double scale=first.GetProperty("camera_scale").GetDouble();
        var shift=first.GetProperty("camera_shift");
        var pivot=new { x=settings.Width*.5-shift[0].GetDouble()*settings.Width,y=settings.Height*.5+shift[1].GetDouble()*settings.Width };
        File.WriteAllText(metadata,JsonSerializer.Serialize(new
        {
            version=1,image="asset.sheet.png",columns,rows,width=w,height=h,canvas=new{width=settings.Width,height=settings.Height},
            fps=settings.Fps,pivot,anchor=pivot,groundWorld=manifest.GetProperty("ground_pivot"),
            worldUnitsPerPixel=scale/settings.Height,trimmed=false,alpha="binary",palette=settings.Colors,
            tags=settings.Animation?new[]{new{name=string.IsNullOrWhiteSpace(settings.AnimationTag)?"Animation":settings.AnimationTag,from=0,to=frames.Length-1,direction="forward"}}:[],
            frames=frames.Select((f,i)=>new{index=i,sourceFrame=f.SourceFrame,x=i%columns*settings.Width,y=i/columns*settings.Height,width=settings.Width,height=settings.Height,durationMs=f.DurationMs,pivot})
        },AssetSettings.JsonOptions));
        return(sheet,metadata);
    }
}
