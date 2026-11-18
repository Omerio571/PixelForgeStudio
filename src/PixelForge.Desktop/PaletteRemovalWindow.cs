using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelForge.Core;
using PixelForge.Core.IndexedLab;

namespace PixelForge.Desktop;

internal sealed class PaletteRemovalWindow : Window
{
    public int Removed {get;private set;}
    public PaletteRemovalWindow(Rgb[] colors,Raster? source,byte threshold)
    {
        Title="Choose the experimental transparent slot";Width=900;Height=640;MinWidth=620;MinHeight=400;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Window");SetResourceReference(ForegroundProperty,"Text");
        static double Distance(Rgb a,Rgb b)=>IndexedCodec.PaletteDistance(a,b);
        var bins=new int[32];Raster? original=null;
        if(source is not null)
        {
            var pixels=(byte[])source.Pixels.Clone();
            var cache=new Dictionary<Rgb,int>();
            for(int p=0;p<pixels.Length;p+=4)
            {
                if(pixels[p+3]<threshold){Array.Clear(pixels,p,4);continue;}
                var rgb=new Rgb(pixels[p+2],pixels[p+1],pixels[p]);
                if(!cache.TryGetValue(rgb,out int index))
                {
                    index=Enumerable.Range(0,32).MinBy(i=>Distance(rgb,colors[i]));
                    if(cache.Count<65536)cache[rgb]=index;
                }
                bins[index]++;
                var c=colors[index];pixels[p]=c.B;pixels[p+1]=c.G;pixels[p+2]=c.R;pixels[p+3]=255;
            }
            original=new(source.Width,source.Height,pixels);
        }
        var replacements=Enumerable.Range(0,32).Select(i=>
            Enumerable.Range(0,32).Where(j=>j!=i).MinBy(j=>Distance(colors[i],colors[j]))).ToArray();
        double Luma(int i)=>.2126*colors[i].R+.7152*colors[i].G+.0722*colors[i].B;
        int darkest=Enumerable.Range(0,32).MinBy(Luma),lightest=Enumerable.Range(0,32).MaxBy(Luma);
        var ranked=Enumerable.Range(0,32).OrderBy(i=>i==darkest||i==lightest).ThenBy(i=>bins[i]).ThenBy(i=>Distance(colors[i],colors[replacements[i]])).ToArray();
        var outer=new DockPanel{Margin=new Thickness(16)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);outer.Children.Add(top);
        top.Children.Add(new TextBlock{Text=source is null?"Provisional recommendation from perceptual color similarity. No image usage was measured.":
            "Recommendation uses the first selected image: least-used color, then closest perceptual substitute. Darkest and lightest are protected in the recommendation. Review outline and ramp importance before accepting.",TextWrapping=TextWrapping.Wrap});
        var choices=new ComboBox{ItemsSource=ranked.Select(i=>$"{colors[i].Hex} → {colors[replacements[i]].Hex}"+(source is null?"":$" · {bins[i]:N0} pixel(s)")).ToArray()};
        top.Children.Add(choices);
        var accept=new Button{Content="Use selected removal",HorizontalAlignment=HorizontalAlignment.Left,IsDefault=true};
        accept.Click+=(_,_)=>{Removed=ranked[choices.SelectedIndex];DialogResult=true;};top.Children.Add(accept);
        var panels=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};
        outer.Children.Add(panels);Content=outer;
        Image Pane(string label)
        {
            var dock=new DockPanel{Margin=new Thickness(5)};
            var title=new TextBlock{Text=label};DockPanel.SetDock(title,Dock.Top);dock.Children.Add(title);
            var image=new Image{Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
            dock.Children.Add(new ScrollViewer{Content=image,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panels.Children.Add(dock);
            return image;
        }
        var originalView=Pane("32-color reference");var reduced=Pane("31 colors + transparency");var diff=Pane("Changed pixels");
        if(original is not null)originalView.Source=IndexedLabWindow.Bitmap(original);
        choices.SelectionChanged+=(_,_)=>{
            if(original is null||choices.SelectedIndex<0)return;
            int removed=ranked[choices.SelectedIndex];var replacement=colors[replacements[removed]];
            var converted=(byte[])original.Pixels.Clone();var highlight=new byte[converted.Length];
            for(int p=0;p<converted.Length;p+=4)
            {
                if(converted[p+3]==0||PixelEngine.ColorAt(converted,p)!=colors[removed].Packed)continue;
                converted[p]=replacement.B;converted[p+1]=replacement.G;converted[p+2]=replacement.R;
                highlight[p+2]=255;highlight[p+3]=255;
            }
            reduced.Source=IndexedLabWindow.Bitmap(new(original.Width,original.Height,converted));
            diff.Source=IndexedLabWindow.Bitmap(new(original.Width,original.Height,highlight));
        };
        choices.SelectedIndex=0;
    }
}
