using System.IO;

namespace PixelForge.Core;
public static class GroundShadow
{
    public static Raster? Create(Raster shaded,Raster clear,Raster model,AssetSettings settings,ICollection<string>? warnings=null)
    {
        if(!settings.GroundShadows||settings.GroundShadowStrength==0)return null;
        int sampling=settings.SamplesPerPixel,w=settings.Width,h=settings.Height;
        if(shaded.Width!=w*sampling||shaded.Height!=h*sampling||clear.Width!=shaded.Width||clear.Height!=shaded.Height)throw new InvalidDataException("Ground receiver dimensions do not match the job.");
        var palette=settings.Colors.Select(Palette.ParseColor).ToArray();
        // Binary-alpha game shadows trade opacity for a restrained palette shade and
        // nested coverage. No dithering, alpha blending or unauthorized colors.
        double target=24+70*(1-settings.GroundShadowStrength);
        Rgb Shade(double luminance)=>palette.MinBy(c=>Math.Pow((.2126*c.R+.7152*c.G+.0722*c.B)-luminance,2)+2*(Math.Pow(c.R-c.G,2)+Math.Pow(c.B-c.G,2)));
        var color=Shade(target);
        var bytes=new byte[w*h*4];var coverage=new double[w*h];double peak=0;
        static double Linear(byte v){double c=v/255.0;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        static double Luma(byte[] b,int p)=>.2126*Linear(b[p+2])+.7152*Linear(b[p+1])+.0722*Linear(b[p]);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            double occlusion=0;
            for(int dy=0;dy<sampling;dy++)for(int dx=0;dx<sampling;dx++)
            {
                int p=((y*sampling+dy)*shaded.Width+x*sampling+dx)*4;
                if(clear.Pixels[p+3]<128||shaded.Pixels[p+3]<128)continue;
                double baseline=Luma(clear.Pixels,p);if(baseline<.005)continue;
                occlusion+=Math.Clamp(1-Luma(shaded.Pixels,p)/baseline,0,1);
            }
            occlusion/=sampling*sampling;
            coverage[y*w+x]=occlusion;peak=Math.Max(peak,occlusion);
        }
        // Ambient fill reduces even fully blocked key light to partial raw
        // contrast. Estimate key coverage from the whole receiver, including
        // contact regions under the model, so ambient cannot erase the penumbra.
        if(peak<.005){warnings?.Add("No measurable ground-shadow contrast in this lighting configuration.");return new(w,h,bytes);}
        for(int n=0;n<w*h;n++)
        {
            int dest=n*4;if(model.Pixels[dest+3]!=0)continue;
            double occlusion=Math.Clamp(coverage[n]/peak,0,1);
            if(occlusion*settings.GroundShadowStrength<.18)continue;
            // Retain penumbra coverage as restrained palette steps rather than
            // collapsing every surviving sample to one hard-edged solid shade.
            var pixel=settings.GroundShadowSoftness==0?color:Shade(target+30*(1-occlusion));
            bytes[dest]=pixel.B;bytes[dest+1]=pixel.G;bytes[dest+2]=pixel.R;bytes[dest+3]=255;
        }
        if(settings.GroundShadowSoftness>0)
        {
            var clean=(byte[])bytes.Clone();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int p=(y*w+x)*4;if(bytes[p+3]==0)continue;bool connected=false;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    int nx=x+dx,ny=y+dy;if(dx==0&&dy==0||nx<0||ny<0||nx>=w||ny>=h)continue;
                    int q=(ny*w+nx)*4;if(bytes[q+3]!=0||model.Pixels[q+3]!=0)connected=true;
                }
                if(!connected)Array.Clear(clean,p,4);
            }
            bytes=clean;
            var shades=Enumerable.Range(0,w*h).Where(i=>bytes[i*4+3]!=0).Select(i=>PixelEngine.ColorAt(bytes,i*4)).Distinct().Count();
            if(shades==0)warnings?.Add("No ground-shadow pixels survive this view, resolution and strength. Increase strength or resolution to see a ground shadow.");
            else if(shades==1)warnings?.Add("Ground-shadow softness is sampled in 3D, but this resolution/palette produces only one shadow shade. Small angle changes may be invisible; use a larger canvas or additional approved dark shades for visible penumbra steps.");
        }
        bool clipped=false;
        for(int x=0;x<w;x++)clipped|=bytes[x*4+3]!=0||bytes[((h-1)*w+x)*4+3]!=0;
        for(int y=0;y<h;y++)clipped|=bytes[y*w*4+3]!=0||bytes[(y*w+w-1)*4+3]!=0;
        if(clipped)warnings?.Add("Ground shadow reaches the canvas edge and may be clipped. Object Only keeps the machine scale fixed; increase padding/canvas space or explicitly choose Object + Ground Shadow fit bounds.");
        return new(w,h,bytes);
    }
    public static Raster Composite(Raster model,Raster? shadow)
    {
        if(shadow is null)return model;
        var bytes=(byte[])shadow.Pixels.Clone();for(int p=0;p<bytes.Length;p+=4)if(model.Pixels[p+3]!=0)Array.Copy(model.Pixels,p,bytes,p,4);
        return new(model.Width,model.Height,bytes);
    }
}
