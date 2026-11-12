namespace PixelForge.Core;

public static class OutlineEngine
{
    // Inward contours preserve the exact canvas, holes, silhouette and ground anchor.
    // A component seam is drawn on only one side, avoiding doubled internal borders.
    public static PixelResult Apply(PixelResult source, AssetSettings settings)
    {
        if (!settings.Outlines) return source;
        settings.Validate();
        int w=source.Image.Width,h=source.Image.Height;
        var internalEdges=GroupSilhouette.InternalEdges(source,settings);
        var mask=new bool[w*h];
        for (int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            int index=y*w+x;
            if(source.Image.Pixels[index*4+3]==0)continue;
            foreach(var (dx,dy) in new[]{(-1,0),(1,0),(0,-1),(0,1)})
            {
                int nx=x+dx,ny=y+dy;
                if(nx<0||ny<0||nx>=w||ny>=h){mask[index]=true;break;}
                int neighbor=ny*w+nx;
                if(source.Image.Pixels[neighbor*4+3]==0 ||
                   settings.OutlineType==OutlineStyle.VisibleComponents && source.ContourIds[index]>source.ContourIds[neighbor])
                {mask[index]=internalEdges is null||!internalEdges[index];break;}
            }
        }
        if(settings.OutlineThickness==2)
        {
            var expanded=(bool[])mask.Clone();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int n=y*w+x;if(!mask[n])continue;
                foreach(var (dx,dy) in new[]{(-1,0),(1,0),(0,-1),(0,1)})
                {
                    int nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=w||ny>=h)continue;int j=ny*w+nx;
                    if(source.Image.Pixels[j*4+3]!=0&&source.ContourIds[j]==source.ContourIds[n])expanded[j]=true;
                }
            }
            mask=expanded;
        }
        var color=Palette.ParseColor(settings.OutlineColor);
        var layer=new byte[w*h*4];var composite=(byte[])source.Image.Pixels.Clone();int count=0;
        for(int n=0;n<mask.Length;n++)if(mask[n])
        {
            int p=n*4;layer[p]=color.B;layer[p+1]=color.G;layer[p+2]=color.R;layer[p+3]=255;
            Array.Copy(layer,p,composite,p,4);count++;
        }
        var warnings=source.Warnings.ToList();
        int opaque=Enumerable.Range(0,w*h).Count(i=>source.Image.Pixels[i*4+3]!=0);
        if(count>opaque*.35)warnings.Add("Outlines cover more than 35% of the visible sprite. Try 1 pixel or outer silhouettes to retain material detail.");
        return source with { Image=new(w,h,composite),OutlineLayer=new(w,h,layer),BaseImage=source.Image,Warnings=warnings.ToArray() };
    }
}
