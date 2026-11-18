using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

internal sealed class PfaViewerWindow : Window
{
    private readonly ListBox files=new(){MinWidth=190};
    private readonly Image image=new(){Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private readonly TextBlock details=new(){TextWrapping=TextWrapping.Wrap},pixel=new();
    private readonly Slider frame=new(){IsSnapToTickEnabled=true,TickFrequency=1,Minimum=0,Width=130};
    private readonly DispatcherTimer timer=new();
    private readonly Button play=new(){Content="Play"};
    private readonly string[] paths;
    private string? root;
    private PfaImage? selected;
    private readonly SemaphoreSlim readGate=new(1);
    private int request;
    private bool exporting;

    public PfaViewerWindow(string path,string? rootOverride=null)
    {
        root=rootOverride;
        paths=File.Exists(path)?[path]:Directory.EnumerateFiles(path,"*.pfa")
            .Concat(Directory.EnumerateDirectories(path).Where(d=>!File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint))
                .SelectMany(d=>Directory.EnumerateFiles(d,"*.pfa"))).Order().Take(257).ToArray();
        if(paths.Length is 0 or >256)throw new InvalidDataException("Choose a PFA file, direction folder or asset folder containing 1–256 PFA layers.");
        Title="Asset Viewer · PFA";Width=1020;Height=720;MinWidth=600;MinHeight=400;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Window");SetResourceReference(ForegroundProperty,"Text");
        var body=new DockPanel{Margin=new Thickness(12)};
        var controls=new DockPanel();DockPanel.SetDock(controls,Dock.Top);body.Children.Add(controls);
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Top);controls.Children.Add(actions);
        actions.Children.Add(play);play.Click+=(_,_)=>{if(timer.IsEnabled)Stop();else if(selected?.Frames.Length>1){play.Content="Pause";Schedule();}};
        actions.Children.Add(frame);
        var zoom=new ComboBox{ItemsSource=new[]{"Fit","1×","2×","4×","8×"},SelectedIndex=0,Width=65};actions.Children.Add(zoom);
        zoom.SelectionChanged+=(_,_)=>{
            image.Stretch=zoom.SelectedIndex==0?Stretch.Uniform:Stretch.None;
            int factor=zoom.SelectedIndex==0?1:1<<(zoom.SelectedIndex-1);
            image.LayoutTransform=new ScaleTransform(factor,factor);
            viewport.HorizontalScrollBarVisibility=viewport.VerticalScrollBarVisibility=zoom.SelectedIndex==0?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
        };
        Button Add(string label,RoutedEventHandler handler){var b=new Button{Content=label};b.Click+=handler;actions.Children.Add(b);return b;}
        Add("Locate Assets root…",(_,_)=>{
            var folder=new OpenFolderDialog{InitialDirectory=root??"",Title="Locate shared GPL palettes"};
            if(folder.ShowDialog(this)==true){root=folder.FolderName;LoadSelected();}
        });
        Add("Export current PNG…",(_,_)=>Save(false,false));
        Add("Export layer…",(_,_)=>Save(true,false));
        Add("Export direction…",(_,_)=>Save(true,true));
        Add("Export all…",(_,_)=>Save(false,true));
        var footer=new StackPanel();footer.Children.Add(details);footer.Children.Add(pixel);
        DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        files.SetResourceReference(BackgroundProperty,"Input");files.SetResourceReference(ForegroundProperty,"Text");
        DockPanel.SetDock(files,Dock.Left);body.Children.Add(files);
        viewport.Content=image;viewport.Background=Checker();
        body.Children.Add(viewport);Content=body;
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        files.ItemsSource=paths.Select(Path.GetFileName).ToArray();
        files.SelectionChanged+=(_,_)=>LoadSelected();
        frame.ValueChanged+=(_,_)=>ShowFrame();
        timer.Tick+=(_,_)=>{if(selected is null){Stop();return;}frame.Value=((int)frame.Value+1)%selected.Frames.Length;Schedule();};
        image.MouseMove+=Inspect;
        Closed+=(_,_)=>{request++;Stop();};
        files.SelectedIndex=0;
    }

    private readonly ScrollViewer viewport=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private static Brush Checker()
    {
        var group=new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(56,59,66)),null,new RectangleGeometry(new Rect(0,0,16,16))));
        var light=new SolidColorBrush(Color.FromRgb(83,87,95));
        group.Children.Add(new GeometryDrawing(light,null,new RectangleGeometry(new Rect(0,0,8,8))));
        group.Children.Add(new GeometryDrawing(light,null,new RectangleGeometry(new Rect(8,8,8,8))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,16,16)};
    }

    private async void LoadSelected()
    {
        int index=files.SelectedIndex;
        if(index<0)return;
        int revision=++request;Stop();selected=null;image.Source=null;details.Text="Reading and verifying PFA…";
        await readGate.WaitAsync();
        try
        {
            if(revision!=request)return;
            string path=paths[index];string? assets=root;
            var result=await Task.Run(()=>Pfa.Read(path,assets));
            if(revision!=request)return;
            selected=result;frame.Maximum=result.Frames.Length-1;frame.Value=0;ShowFrame();
        }
        catch(Exception e){if(revision==request)details.Text=e.Message;}
        finally{readGate.Release();}
    }

    private void Stop(){timer.Stop();play.Content="Play";}
    private void Schedule(){if(selected is null)return;timer.Interval=TimeSpan.FromMilliseconds(selected.Info.Durations[(int)frame.Value]);timer.Start();}
    private void ShowFrame()
    {
        if(selected is null)return;
        var info=selected.Info;int index=(int)frame.Value;
        image.Source=IndexedLabWindow.Bitmap(selected.Frames[index]);
        string pivot=info.Placement.TryGetProperty("pivot",out var p)?p.ToString():"";
        string scale=info.Placement.TryGetProperty("worldUnitsPerPixel",out var s)?s.ToString():"";
        details.Text=$"PASS · {info.Asset} / {info.Direction} / {info.Layer} · {info.Width}×{info.Height} · {selected.Storage} · {selected.Codec}\n"+
            $"Frame {index+1}/{selected.Frames.Length} · {info.Durations[index]} ms · {selected.FileBytes:N0} bytes on disk · {info.Width*(long)info.Height*selected.Frames.Length*4:N0} decoded bytes\n"+
            $"Palette: {(info.Palette.Length>0?info.Palette:"No external palette")} · Pivot: {pivot} · World units/pixel: {scale}";
    }
    private void Inspect(object sender,MouseEventArgs e)
    {
        if(selected is null||image.ActualWidth<=0||image.ActualHeight<=0)return;
        var raster=selected.Frames[(int)frame.Value];var pt=e.GetPosition(image);
        int x=(int)(pt.X*raster.Width/image.ActualWidth),y=(int)(pt.Y*raster.Height/image.ActualHeight);
        if(x<0||y<0||x>=raster.Width||y>=raster.Height)return;
        int i=(y*raster.Width+x)*4;
        string index=selected.Indices is {} indices?$" · index {indices[(int)frame.Value*raster.Width*raster.Height+i/4]}":"";
        pixel.Text=selected.Info.Layer==PfaLayer.Emission?
            $"({x}, {y}) · intensity {raster.Pixels[i+2]}/255 · alpha {raster.Pixels[i+3]}":
            $"({x}, {y}) · RGB({raster.Pixels[i+2]}, {raster.Pixels[i+1]}, {raster.Pixels[i]}) · alpha {raster.Pixels[i+3]}{index}";
    }
    private async void Save(bool scoped,bool collection)
    {
        if(selected is null||exporting)return;
        var chosen=selected;int current=(int)frame.Value;
        try
        {
            exporting=true;
            if(!scoped&&!collection)
            {
                var dialog=new SaveFileDialog{Filter="PNG|*.png",FileName=$"{chosen.Info.Asset}-{chosen.Info.Layer}-{current+1:000}.png"};
                if(dialog.ShowDialog(this)==true)await Task.Run(()=>chosen.Frames[current].Save(dialog.FileName));
                return;
            }
            var folder=new OpenFolderDialog{Title="Choose where to create a decoded PNG folder"};
            if(folder.ShowDialog(this)!=true)return;
            var subset=scoped?paths.Where(p=>Path.GetDirectoryName(p)==Path.GetDirectoryName(paths[files.SelectedIndex])).ToArray():paths;
            string destination=Path.Combine(folder.FolderName,"decoded-"+Guid.NewGuid().ToString("N")[..8]);
            string? assets=root;
            await Task.Run(()=>{
                Directory.CreateDirectory(destination);
                var layers=collection?subset.Select(path=>Pfa.Read(path,assets)):[chosen];
                foreach(var layer in layers)
                {
                    string stem=Pfa.Segment(layer.Info.Asset)+"-"+Pfa.Segment(layer.Info.Direction)+"-"+layer.Info.Layer;
                    for(int i=0;i<layer.Frames.Length;i++)layer.Frames[i].Save(Path.Combine(destination,$"{stem}-{i+1:000}.png"));
                }
            });
            details.Text="Decoded PNGs saved: "+destination;
        }
        catch(Exception ex){details.Text=ex.Message;}
        finally{exporting=false;}
    }
}
