namespace PixelForge.Core;

// Classifies contours only. Never fills artwork, changes alpha, or merges cels.
public static class GroupSilhouette
{
    public static bool[]? InternalEdges(PixelResult source,AssetSettings settings)
    {
        if(!settings.RespectOutlineGroups||settings.GroupedOutlines!=GroupedOutlineMode.Unified||source.GroupKeys is not {Length:>0})return null;
        int w=source.Image.Width,h=source.Image.Height;var internalEdges=new bool[w*h];
        foreach(int group in source.GroupKeys)
        {
            var members=Enumerable.Range(0,w*h).Select(n=>source.Image.Pixels[n*4+3]!=0&&source.ContourIds[n]==group).ToArray();
            if(!members.Any(value=>value))continue;
            // Close only one-pixel ports in the classification envelope. Actual
            // transparent gaps and disconnected component pixels are never filled.
            var dilated=new bool[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(members[y*w+x])
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {int nx=x+dx,ny=y+dy;if(nx>=0&&ny>=0&&nx<w&&ny<h)dilated[ny*w+nx]=true;}
            var envelope=(bool[])members.Clone();
            for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)
            {
                bool filled=true;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)filled&=dilated[(y+dy)*w+x+dx];
                if(filled)envelope[y*w+x]=true;
            }
            var exterior=new bool[w*h];var queue=new Queue<int>();
            void Visit(int n){if(!exterior[n]&&!envelope[n]){exterior[n]=true;queue.Enqueue(n);}}
            for(int x=0;x<w;x++){Visit(x);Visit((h-1)*w+x);}
            for(int y=0;y<h;y++){Visit(y*w);Visit(y*w+w-1);}
            while(queue.TryDequeue(out int n))
            {
                int x=n%w,y=n/w;
                if(x>0)Visit(n-1);if(x<w-1)Visit(n+1);if(y>0)Visit(n-w);if(y<h-1)Visit(n+w);
            }
            for(int n=0;n<w*h;n++)if(members[n])
            {
                int x=n%w,y=n/w;bool outside=x==0||x==w-1||y==0||y==h-1,transparent=false,otherGroup=false;
                foreach(int j in Neighbors(n,x,y,w,h))
                {
                    if(source.Image.Pixels[j*4+3]==0){transparent=true;outside|=exterior[j];}
                    else if(source.ContourIds[j]!=group)otherGroup=true;
                }
                internalEdges[n]=transparent&&!outside&&!otherGroup;
            }
        }
        return internalEdges;
    }
    private static IEnumerable<int> Neighbors(int n,int x,int y,int w,int h)
    {if(x>0)yield return n-1;if(x<w-1)yield return n+1;if(y>0)yield return n-w;if(y<h-1)yield return n+w;}
}
