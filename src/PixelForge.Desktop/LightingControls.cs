using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Numerics;
using System.Text.Json;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private BitmapImage[] normalPreview=[],emissionPreview=[];
    private bool updatingMapControls;
    private BitmapSource? renderedLightingPreview;
    private (int Frame,int Mode,Rgb Tint,double Intensity,Vector3 Light,double Ambient) previewKey;
    private Vector3[] cameraBasis=[];

    private BitmapSource LightingPreview(int frame)
    {
        int mode=LightingMapInput.SelectedIndex;
        if(mode==1&&normalPreview.Length>0)return normalPreview[frame];
        var outputFrame=output!.Frames[frame];
        if(mode==2&&emissionPreview.Length>0)return emissionPreview[frame];
        if(mode==3&&outputFrame.Emission is {} emission&&EmissionEnabled.IsChecked==true)
        {
            Rgb tint;
            try{tint=Palette.ParseColor(EmissionTint.Text);}
            catch(FormatException){return playbackFrames[frame];}
            catch(ArgumentException){return playbackFrames[frame];}
            var key=(frame,mode,tint,EmissionIntensity.Value,Vector3.Zero,0d);
            if(renderedLightingPreview is null||key!=previewKey)
            {
                var color=Raster.Read(outputFrame.Png);
                var map=Raster.Read(emission);
                for(int p=0;p<color.Pixels.Length;p+=4)
                {
                    if(color.Pixels[p+3]==0)continue;
                    double strength=map.Pixels[p]/255d*EmissionIntensity.Value;
                    color.Pixels[p]=(byte)Math.Min(255,Math.Round(color.Pixels[p]+tint.B*strength));
                    color.Pixels[p+1]=(byte)Math.Min(255,Math.Round(color.Pixels[p+1]+tint.G*strength));
                    color.Pixels[p+2]=(byte)Math.Min(255,Math.Round(color.Pixels[p+2]+tint.R*strength));
                }
                renderedLightingPreview=ToBitmap(color);previewKey=key;
            }
            return renderedLightingPreview;
        }
        if(mode==4&&outputFrame.Normal is {} normal&&StudioLightingInput.IsChecked==true&&cameraBasis.Length==3)
        {
            double az=LightAzimuthInput.Value*Math.PI/180,el=LightElevationInput.Value*Math.PI/180;
            var screen=new Vector3((float)(Math.Cos(az)*Math.Cos(el)),(float)(Math.Sin(az)*Math.Cos(el)),(float)Math.Sin(el));
            var light=new Vector3(Vector3.Dot(cameraBasis[0],screen),Vector3.Dot(cameraBasis[1],screen),Vector3.Dot(cameraBasis[2],screen));
            var key=(frame,mode,default(Rgb),LightIntensityInput.Value,light,AmbientInput.Value);
            if(renderedLightingPreview is null||key!=previewKey)
            {
                var color=Raster.Read(outputFrame.Png);
                var map=Raster.Read(normal);
                for(int p=0;p<color.Pixels.Length;p+=4)
                {
                    if(color.Pixels[p+3]==0)continue;
                    var n=Vector3.Normalize(new Vector3(map.Pixels[p+2],map.Pixels[p+1],map.Pixels[p])/127.5f-Vector3.One);
                    double shade=Math.Clamp(AmbientInput.Value*.5+Math.Max(0,Vector3.Dot(n,light))*LightIntensityInput.Value*.4,0,2);
                    for(int c=0;c<3;c++)color.Pixels[p+c]=(byte)Math.Min(255,Math.Round(color.Pixels[p+c]*shade));
                }
                renderedLightingPreview=ToBitmap(color);previewKey=key;
            }
            return renderedLightingPreview;
        }
        return playbackFrames[frame];
    }

    private static BitmapImage LoadMap(string path)
    {
        using var stream=File.OpenRead(path);
        var image=new BitmapImage();
        image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        image.StreamSource=stream;image.EndInit();image.Freeze();
        return image;
    }

    private static BitmapSource ToBitmap(Raster image)
    {
        var bitmap=BitmapSource.Create(image.Width,image.Height,96,96,PixelFormats.Bgra32,null,image.Pixels,image.Width*4);
        bitmap.Freeze();return bitmap;
    }

    private void EmissionTintChanged(object sender,TextChangedEventArgs e)=>EmissionPreviewChanged(sender,e);
    private void EmissionGainChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>EmissionPreviewChanged(sender,e);
    private void StudioValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>StudioPreviewChanged(sender,e);

    private void EmissionPreviewChanged(object sender,RoutedEventArgs e)
    {
        if(LightingMapInput?.SelectedIndex==3&&playback is not null)ShowPlaybackFrame();
    }

    private void StudioPreviewChanged(object sender,RoutedEventArgs e)
    {
        if(LightingMapInput?.SelectedIndex==4&&playback is not null)
        {
            if(StudioLightingInput.IsChecked!=true)LightingMapInput.SelectedIndex=0;
            else ShowPlaybackFrame();
        }
    }

    private static int CompareMap(CheckBox normal,CheckBox emission)=> emission.IsChecked==true?2:normal.IsChecked==true?1:0;

    private void LoadNormalPreview(ConversionOutput asset)
    {
        updatingMapControls=true;
        try
        {
            normalPreview=asset.NormalSheet is null?[]:asset.Frames.Select(frame=>LoadMap(frame.Normal!)).ToArray();
            emissionPreview=asset.EmissionSheet is null?[]:asset.Frames.Select(frame=>LoadMap(frame.Emission!)).ToArray();
            renderedLightingPreview=null;
            EmissionMaskChoice.IsEnabled=EmissionPreviewChoice.IsEnabled=asset.EmissionSheet is not null;
            cameraBasis=[];
            if(asset.NormalSheet is not null&&asset.Metadata is not null)
            {
                using var metadata=JsonDocument.Parse(File.ReadAllText(asset.Metadata));
                if(metadata.RootElement.TryGetProperty("lighting",out var lighting)&&lighting.ValueKind==JsonValueKind.Object&&lighting.TryGetProperty("cameraToWorld",out var basis))
                    cameraBasis=basis.EnumerateArray().Select(row=>new Vector3(row[0].GetSingle(),row[1].GetSingle(),row[2].GetSingle())).ToArray();
            }
            LightingMapInput.SelectedIndex=0;
            EmissionEnabled.Visibility=EmissionTint.Visibility=EmissionIntensity.Visibility=Visibility.Collapsed;
            LightingMapTools.Visibility=normalPreview.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        finally {updatingMapControls=false;}
    }

    private void LightingMapChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updatingMapControls||EmissionIntensity is null)return;
        EmissionEnabled.Visibility=EmissionTint.Visibility=EmissionIntensity.Visibility=LightingMapInput.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;
        if(playback is not null)ShowPlaybackFrame();
    }

    private void SetCompareMapAvailability()
    {
        updatingMapControls=true;
        try
        {
            foreach(var (toggle,asset) in new[]{(CompareNormalA,compareA?.NormalSheet),(CompareNormalB,compareB?.NormalSheet),(CompareEmissionA,compareA?.EmissionSheet),(CompareEmissionB,compareB?.EmissionSheet)})
            {
                toggle.Visibility=asset is not null?Visibility.Visible:Visibility.Collapsed;
                if(toggle.Visibility==Visibility.Collapsed)toggle.IsChecked=false;
            }
        }
        finally {updatingMapControls=false;}
    }

    private void CompareMapChanged(object sender,RoutedEventArgs e)
    {
        if(updatingMapControls||CompareEmissionB is null)return;
        if(sender is CheckBox {IsChecked:true})
        {
            updatingMapControls=true;
            if(sender==CompareEmissionA)CompareNormalA.IsChecked=false;
            if(sender==CompareEmissionB)CompareNormalB.IsChecked=false;
            if(sender==CompareNormalA)CompareEmissionA.IsChecked=false;
            if(sender==CompareNormalB)CompareEmissionB.IsChecked=false;
            updatingMapControls=false;
        }
        ShowComparisonFrames();
    }

    private void CompareLightingMaps(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(output?.NormalSheet is null)return;
        int frame=playback?.CurrentFrame??0;
        ToggleCompare(sender,e);
        var entry=RenderAInput.Items.OfType<RenderEntry>().FirstOrDefault(item=>item.Directory==output.Directory);
        refreshingHistory=true;
        RenderAInput.SelectedItem=entry;RenderBInput.SelectedItem=entry;
        refreshingHistory=false;
        // A reopened render may not be in recent history. Compare its current
        // saved maps directly without adding or preserving anything on disk.
        compareA=output;compareB=output;
        RenderAInfo.Text="Current asset · base color";RenderBInfo.Text="Current asset · technical map";
        SetCompareMapAvailability();
        updatingMapControls=true;
        CompareNormalA.IsChecked=false;CompareEmissionA.IsChecked=false;
        CompareNormalB.IsChecked=LightingMapInput.SelectedIndex is not (2 or 3);
        CompareEmissionB.IsChecked=!CompareNormalB.IsChecked;
        updatingMapControls=false;
        frameA=frameB=frame;
        CompareModeInput.SelectedIndex=1;
        ShowComparisonFrames();
    });

    private void SaveLightingAsset(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(output?.NormalSheet is null || !EnsureTimingApplied())return;
        var dialog=new OpenFolderDialog {Title="Choose a folder for the lighting asset",InitialDirectory=directories.Initial("sheets")};
        if(dialog.ShowDialog()!=true)return;
        if(session?.IsManaged(dialog.FolderName)==true)throw new ArgumentException("Choose a folder outside temporary session storage.");
        var saved=LightingMaps.Export(output,dialog.FolderName);
        directories.SelectedFolder("sheets",dialog.FolderName);
        SavePreferences();Status.Text="Lighting asset saved: "+saved;
    });
}
