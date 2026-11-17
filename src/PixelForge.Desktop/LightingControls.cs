using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private BitmapImage[] normalPreview=[];
    private bool updatingMapControls;

    private void LoadNormalPreview(ConversionOutput asset)
    {
        updatingMapControls=true;
        try
        {
            normalPreview=asset.NormalSheet is null?[]:asset.Frames.Select(frame=>
            {
                using var stream=File.OpenRead(frame.Normal!);
                var image=new BitmapImage();
                image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=stream;image.EndInit();image.Freeze();
                return image;
            }).ToArray();
            LightingMapInput.SelectedIndex=0;
            LightingMapTools.Visibility=normalPreview.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        finally {updatingMapControls=false;}
    }

    private void LightingMapChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!updatingMapControls&&playback is not null)ShowPlaybackFrame();
    }

    private void SetCompareMapAvailability()
    {
        updatingMapControls=true;
        try
        {
            foreach(var (toggle,asset) in new[]{(CompareNormalA,compareA),(CompareNormalB,compareB)})
            {
                toggle.Visibility=asset?.NormalSheet is not null?Visibility.Visible:Visibility.Collapsed;
                if(toggle.Visibility==Visibility.Collapsed)toggle.IsChecked=false;
            }
        }
        finally {updatingMapControls=false;}
    }

    private void CompareMapChanged(object sender,RoutedEventArgs e)
    {
        if(!updatingMapControls)ShowComparisonFrames();
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
        RenderAInfo.Text="Current asset · base color";RenderBInfo.Text="Current asset · normal map";
        SetCompareMapAvailability();
        updatingMapControls=true;
        CompareNormalA.IsChecked=false;CompareNormalB.IsChecked=true;
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
