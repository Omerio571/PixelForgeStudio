using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private RenderHistory? history;
    private ConversionOutput? compareA,compareB;
    private ComparisonLayout? comparisonLayout;
    private int frameA,frameB;
    private string? imagePathA,imagePathB;
    private bool refreshingHistory,updatingCompareFrame;
    private double comparisonZoom=1;
    private Point? panStart;
    private Point panOffsets;
    private IEnumerable<NumericSlider> NumericControls(bool includeDivider=false)
    {
        foreach(var c in new[]{PaddingInput,LightAzimuthInput,LightElevationInput,LightIntensityInput,AmbientInput,ShadowInput,GroundStrengthInput,GroundSoftnessInput,EdgeStrengthInput})if(c is not null)yield return c;
        if(includeDivider&&DividerInput is not null)yield return DividerInput;
    }
    private void DimensionsChanged(object sender,TextChangedEventArgs e)=>UpdatePresetSelection();
    private void UpdatePresetSelection()
    {
        if(ResolutionPresets is null||WidthInput is null||HeightInput is null)return;
        foreach(Button button in ResolutionPresets.Children)
        {
            bool selected=int.TryParse(WidthInput.Text,out int w)&&int.TryParse(HeightInput.Text,out int h)&&w==h&&w.ToString()==button.Tag.ToString();
            if(selected){button.Background=new SolidColorBrush(Color.FromRgb(140,216,189));button.FontWeight=FontWeights.Bold;}
            else{button.ClearValue(Control.BackgroundProperty);button.ClearValue(Control.FontWeightProperty);}
            System.Windows.Automation.AutomationProperties.SetHelpText(button,selected?"Selected square resolution":"Set width and height; Generate applies the change");
        }
    }
    private void InitializeHistory()
    {
        try{history=new RenderHistory(Path.Combine(ProfileDirectory,"render-history.json"),path=>session?.IsManaged(path)==true);RefreshHistory();}
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {ComparisonInfo.Text="History could not be loaded. Existing history and artwork were left untouched. "+e.Message;}
    }
    private void RememberCompleted(ConversionOutput completed)
    {
        try{history?.AddCompleted(completed);RefreshHistory();}
        catch(Exception e){Status.Text="Render completed, but recent history could not be saved: "+e.Message;}
    }
    private void RefreshHistory(string? selectA=null,string? selectB=null)
    {
        if(history is null)return;
        selectA??=(RenderAInput.SelectedItem as RenderEntry)?.Directory;selectB??=(RenderBInput.SelectedItem as RenderEntry)?.Directory;
        var entries=history.Entries.ToArray();refreshingHistory=true;
        RenderAInput.ItemsSource=entries;RenderBInput.ItemsSource=entries;
        RenderAInput.SelectedItem=entries.FirstOrDefault(e=>e.Directory==selectA)??entries.FirstOrDefault();
        RenderBInput.SelectedItem=entries.FirstOrDefault(e=>e.Directory==selectB)??entries.Skip(1).FirstOrDefault()??entries.FirstOrDefault();
        refreshingHistory=false;LoadComparisonSelection();
    }
    private void ToggleCompare(object sender,RoutedEventArgs e)
    {
        if(ComparisonPanel.Visibility==Visibility.Visible){CloseComparison();return;}
        PausePlayback();PlaybackTools.Visibility=Visibility.Collapsed;PreviewBorder.Visibility=Visibility.Collapsed;TimelinePanel.Visibility=Visibility.Collapsed;OutputInfo.Visibility=Visibility.Collapsed;ExportPanel.Visibility=Visibility.Collapsed;
        ComparisonPanel.Visibility=Visibility.Visible;CompareButton.Content="Back to preview";RefreshHistory();SetChecker();UpdateComparisonZoom();
    }
    private void CloseComparison()
    {
        if(ComparisonPanel is null)return;
        ComparisonPanel.Visibility=Visibility.Collapsed;PlaybackTools.Visibility=Visibility.Visible;CompareButton.Content="Compare";PreviewBorder.Visibility=Visibility.Visible;
        TimelinePanel.Visibility=playbackFrames.Length>1?Visibility.Visible:Visibility.Collapsed;OutputInfo.Visibility=Visibility.Visible;ExportPanel.Visibility=Visibility.Visible;
    }
    private void ComparisonSelectionChanged(object sender,SelectionChangedEventArgs e)
    {if(!refreshingHistory){if(sender==RenderAInput)frameA=0;else frameB=0;LoadComparisonSelection();}}
    private void LoadComparisonSelection()
    {
        if(RenderAInput is null||RenderBInput is null||ComparisonInfo is null)return;
        compareA=ReadComparison(RenderAInput.SelectedItem as RenderEntry,RenderAInfo);
        compareB=ReadComparison(RenderBInput.SelectedItem as RenderEntry,RenderBInfo);
        FavoriteA.Content=RenderAInput.SelectedItem is RenderEntry ea&&history?.IsFavorite(ea)==true?"★":"☆";
        FavoriteB.Content=RenderBInput.SelectedItem is RenderEntry eb&&history?.IsFavorite(eb)==true?"★":"☆";
        ShowComparisonFrames();
    }
    private static ConversionOutput? ReadComparison(RenderEntry? entry,TextBlock info)
    {
        if(entry is null){info.Text="No saved render selected";return null;}
        if(entry.UnavailableReason is {} missing){info.Text=missing;return null;}
        try
        {
            var output=GeneratedAsset.Open(entry.Directory);var s=entry.Settings;
            info.Text=$"{s.Width}×{s.Height} · {s.View} · {s.Projection}\n{output.Frames.Length} frame(s) · {s.Colors.Length} palette colors · {entry.Completed.ToLocalTime():g}";
            info.ToolTip=$"{entry.Directory}\nLight {s.LightAzimuth:0.##}° / {s.LightElevation:0.##}° · key {s.LightIntensity:0.##} · ambient {s.AmbientIntensity:0.##}\nSelf shadows {s.CastShadows} · ground {s.GroundShadows} / {s.GroundShadowStrength:P0} · softness {s.GroundShadowSoftness:0.##} rad\nOutlines {s.Outlines} / {s.OutlineType} · collections {s.RespectOutlineGroups} · highlights {s.EdgeHighlights} / {s.EdgeHighlightStrength:P0}";
            return output;
        }
        catch(Exception e)when(e is IOException or JsonException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {info.Text="Unavailable: "+e.Message;return null;}
    }
    private void ShowComparisonFrames()
    {
        if(CompareFrameA is null||CompareFrameB is null)return;
        frameA=Math.Clamp(frameA,0,Math.Max(0,(compareA?.Frames.Length??1)-1));frameB=Math.Clamp(frameB,0,Math.Max(0,(compareB?.Frames.Length??1)-1));
        updatingCompareFrame=true;CompareFrameA.Text=(frameA+1).ToString();CompareFrameB.Text=(frameB+1).ToString();updatingCompareFrame=false;
        try
        {
            LoadComparisonImage(compareA,frameA,CompareImageA,ref imagePathA);LoadComparisonImage(compareB,frameB,CompareImageB,ref imagePathB);
            if(compareA is null||compareB is null){comparisonLayout=null;ComparisonCanvas.Visibility=Visibility.Collapsed;ComparisonInfo.Text="Choose two available saved renders. Add saved render can import a previously completed output. History keeps four recent renders plus favorites; files are never deleted.";return;}
            var a=(BitmapSource)CompareImageA.Source;var b=(BitmapSource)CompareImageB.Source;
            comparisonLayout=ComparisonLayout.Create(a.PixelWidth,a.PixelHeight,b.PixelWidth,b.PixelHeight,ComparisonLayout.ReadAnchor(compareA.Metadata),ComparisonLayout.ReadAnchor(compareB.Metadata));
            ComparisonCanvas.Visibility=Visibility.Visible;
            long elapsedA=compareA.Frames.Take(frameA).Sum(f=>(long)f.DurationMs),elapsedB=compareB.Frames.Take(frameB).Sum(f=>(long)f.DurationMs);
            ComparisonInfo.Text=$"A {frameA+1}/{compareA.Frames.Length} · source {compareA.Frames[frameA].SourceFrame} · {elapsedA} ms   |   B {frameB+1}/{compareB.Frames.Length} · source {compareB.Frames[frameB].SourceFrame} · {elapsedB} ms\n"+(comparisonLayout.GroundAligned?"Shared ground-anchor alignment.":"Centered canvases (shared ground anchors unavailable).")+" Frames are independent. Middle-drag or scrollbars pan; zoom is shared. Comparison is read-only.";
            UpdateComparisonZoom();
        }
        catch(Exception e)when(e is IOException or NotSupportedException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {comparisonLayout=null;ComparisonCanvas.Visibility=Visibility.Collapsed;ComparisonInfo.Text="Saved preview unavailable: "+e.Message;}
    }
    private static void LoadComparisonImage(ConversionOutput? output,int index,Image image,ref string? loadedPath)
    {
        string? path=output?.Frames[index].Png;
        if(path is null){image.Source=null;loadedPath=null;return;}
        var info=new FileInfo(path);string key=path+"|"+info.LastWriteTimeUtc.Ticks+"|"+info.Length;
        if(key==loadedPath)return;
        using var stream=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;loadedPath=key;
    }
    private void ToggleFavorite(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var entry=(((Button)sender).Tag.ToString()=="A"?RenderAInput:RenderBInput).SelectedItem as RenderEntry;
        if(entry is null||history is null)return;
        if(output?.Directory==entry.Directory&&!EnsureTimingApplied())return;
        if(!history.IsFavorite(entry)&&session?.IsManaged(entry.Directory)==true)entry=PreserveEntry(entry);
        history.ToggleFavorite(entry);RefreshHistory(entry.Directory);
    });
    private void AddSavedRender(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var dialog=new OpenFileDialog{Title="Add a successfully completed saved render",Filter="Generated asset|asset.output.json;asset.settings.json;*.aseprite"};Prepare(dialog,"assets");if(dialog.ShowDialog()!=true)return;
        if(history is null)throw new IOException("History is unavailable. Check the existing render-history.json before adding renders.");
        var output=GeneratedAsset.Open(dialog.FileName);var entry=history.AddCompleted(output);RememberFile("assets",dialog.FileName);
        if(!history.Entries.Any(e=>e.Directory==entry.Directory))history.ToggleFavorite(entry);
        RefreshHistory(entry.Directory);
    });
    private void ComparisonModeChanged(object sender,SelectionChangedEventArgs e)=>UpdateComparisonZoom();
    private void ComparisonSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateComparisonZoom();
    private void UpdateComparisonZoom()
    {
        if(comparisonLayout is null||ComparisonCanvas is null||ZoomInput is null||ComparisonScroll is null)return;
        var layout=comparisonLayout;bool side=CompareModeInput.SelectedIndex==1;
        double gap=side?24:0;double logicalWidth=layout.Width*(side?2:1);
        double fit=Math.Min(Math.Max(1,ComparisonScroll.ActualWidth-gap-20)/logicalWidth,Math.Max(1,ComparisonScroll.ActualHeight-20)/layout.Height);
        comparisonZoom=ZoomInput.SelectedIndex==0?(fit>=1?Math.Floor(fit):Math.Max(.01,fit)):Math.Pow(2,ZoomInput.SelectedIndex-1);
        double w=layout.Width*comparisonZoom,h=layout.Height*comparisonZoom;
        ComparisonCanvas.Width=w*(side?2:1)+gap;ComparisonCanvas.Height=h;
        CompareHostA.Width=CompareHostB.Width=w;CompareHostA.Height=CompareHostB.Height=h;Canvas.SetLeft(CompareHostA,0);Canvas.SetLeft(CompareHostB,side?w+gap:0);
        Place(CompareImageA,layout.A,comparisonZoom);Place(CompareImageB,layout.B,comparisonZoom);
        DividerPanel.Visibility=side?Visibility.Collapsed:Visibility.Visible;CompareDivider.Visibility=DividerThumb.Visibility=side?Visibility.Collapsed:Visibility.Visible;
        UpdateDivider();
        static void Place(Image image,ImagePlacement p,double zoom){image.Width=p.Width*zoom;image.Height=p.Height*zoom;Canvas.SetLeft(image,p.X*zoom);Canvas.SetTop(image,p.Y*zoom);}
    }
    private void DividerChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>UpdateDivider();
    private void UpdateDivider()
    {
        if(comparisonLayout is null||CompareHostA is null||DividerInput is null)return;
        double width=comparisonLayout.Width*comparisonZoom,height=comparisonLayout.Height*comparisonZoom,split=width*DividerInput.Value/100;
        bool side=CompareModeInput.SelectedIndex==1;
        CompareHostA.Clip=side?null:new RectangleGeometry(new(0,0,split,height));CompareHostB.Clip=side?null:new RectangleGeometry(new(split,0,Math.Max(0,width-split),height));
        Canvas.SetLeft(CompareDivider,split-10);CompareDivider.Height=height;Canvas.SetLeft(DividerThumb,split-10);DividerThumb.Height=height;
    }
    private void DragDivider(object sender,DragDeltaEventArgs e)
    {if(comparisonLayout is not null)DividerInput.Value=Math.Clamp(DividerInput.Value+e.HorizontalChange/(comparisonLayout.Width*comparisonZoom)*100,0,100);}
    private void CommitComparisonFrame(object sender,KeyboardFocusChangedEventArgs e)=>ReadComparisonFrame((TextBox)sender);
    private bool ReadComparisonFrame(TextBox box)
    {
        if(updatingCompareFrame)return true;bool a=box==CompareFrameA;var output=a?compareA:compareB;
        if(output is null)return true;
        if(!int.TryParse(box.Text,out int frame)||frame<1||frame>output.Frames.Length){ComparisonInfo.Text=$"Enter a frame from 1–{output.Frames.Length}. Last valid frame remains visible; Escape cancels.";box.BorderBrush=Brushes.Salmon;return false;}
        box.ClearValue(Control.BorderBrushProperty);if(a)frameA=frame-1;else frameB=frame-1;ShowComparisonFrames();return true;
    }
    private void ComparisonFrameKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter){ReadComparisonFrame((TextBox)sender);e.Handled=true;}
        else if(e.Key==Key.Escape){((TextBox)sender).ClearValue(Control.BorderBrushProperty);ShowComparisonFrames();e.Handled=true;}
    }
    private void StepComparison(object sender,RoutedEventArgs e)
    {
        var parts=((Button)sender).Tag.ToString()!.Split(',');int delta=int.Parse(parts[1]);if(parts[0]=="A")frameA+=delta;else frameB+=delta;ShowComparisonFrames();
    }
    private void MatchSourceFrame(object sender,RoutedEventArgs e)
    {
        if(compareA is null||compareB is null||RenderAInput.SelectedItem is not RenderEntry a||RenderBInput.SelectedItem is not RenderEntry b)return;
        int source=compareA.Frames[frameA].SourceFrame;
        int match=StringComparer.OrdinalIgnoreCase.Equals(a.Settings.Model,b.Settings.Model)&&source!=-1?Array.FindIndex(compareB.Frames,f=>f.SourceFrame==source):-1;
        if(match<0){ComparisonInfo.Text="No identical source-model/frame match. Select frames independently or use Match time.";return;}
        frameB=match;ShowComparisonFrames();
    }
    private void MatchTimeFrame(object sender,RoutedEventArgs e)
    {if(compareA is null||compareB is null)return;frameB=ComparisonLayout.FrameAtTime(compareB.Frames,compareA.Frames.Take(frameA).Sum(f=>(long)f.DurationMs));ShowComparisonFrames();}
    private void ComparisonPanStart(object sender,MouseButtonEventArgs e)
    {if(e.ChangedButton!=MouseButton.Middle)return;panStart=e.GetPosition(ComparisonScroll);panOffsets=new(ComparisonScroll.HorizontalOffset,ComparisonScroll.VerticalOffset);ComparisonScroll.CaptureMouse();e.Handled=true;}
    private void ComparisonPanMove(object sender,MouseEventArgs e)
    {if(panStart is not {} start)return;var current=e.GetPosition(ComparisonScroll);ComparisonScroll.ScrollToHorizontalOffset(panOffsets.X+start.X-current.X);ComparisonScroll.ScrollToVerticalOffset(panOffsets.Y+start.Y-current.Y);e.Handled=true;}
    private void ComparisonPanEnd(object sender,MouseButtonEventArgs e)
    {if(panStart is null)return;panStart=null;ComparisonScroll.ReleaseMouseCapture();e.Handled=true;}
}
