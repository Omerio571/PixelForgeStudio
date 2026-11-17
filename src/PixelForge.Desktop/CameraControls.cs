using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private AssetSettings? legacyCamera;
    private ConversionOutput[] directionalBatch = [];

    private void ApplyCamera(AssetSettings settings)
    {
        legacyCamera=settings.Camera is null?settings:null;
        var camera=settings.Camera??CameraSelection.FromLegacy(settings.View,settings.Projection);
        CameraFamilyInput.SelectedItem=camera.Family;
        ViewInput.SelectedValue=camera.Direction;
        ProjectionInput.SelectedValue=camera.Perspective;
        CameraElevationInput.Value=camera.Elevation??60;
        AllDirectionsInput.IsChecked=settings.RenderAllDirections;
    }

    private AssetSettings ReadCamera(AssetSettings settings)
    {
        var preset=(CameraPerspective)ProjectionInput.SelectedValue;
        var camera=new CameraSelection((CameraFamily)CameraFamilyInput.SelectedItem,
            (CameraDirection)ViewInput.SelectedValue,preset,
            preset==CameraPerspective.TopDownThreeQuarter?CameraElevationInput.Value:null);
        bool all=AllDirectionsInput.IsChecked==true;
        if(!all && legacyCamera is {} legacy && camera==CameraSelection.FromLegacy(legacy.View,legacy.Projection))
            return settings with {View=legacy.View,Projection=legacy.Projection};
        return settings with {Camera=camera,RenderAllDirections=all};
    }

    private void CameraFamilyChanged(object sender,SelectionChangedEventArgs e)
    {
        if(CameraFamilyInput.SelectedItem is not CameraFamily family || ViewInput is null || ProjectionInput is null)return;
        ViewInput.DisplayMemberPath="Value";
        ViewInput.SelectedValuePath="Key";
        ViewInput.ItemsSource=CameraSelection.Directions(family).Select(direction=>KeyValuePair.Create(direction,direction switch
        {
            CameraDirection.NE=>"North-East (NE)",
            CameraDirection.SE=>"South-East (SE)",
            CameraDirection.SW=>"South-West (SW)",
            CameraDirection.NW=>"North-West (NW)",
            CameraDirection.N=>"North",
            CameraDirection.E=>"East",
            CameraDirection.S=>"South",
            _=>"West"
        })).ToArray();
        ViewInput.SelectedValue=family==CameraFamily.Isometric?CameraDirection.SE:CameraDirection.N;
        ProjectionInput.DisplayMemberPath="Value";
        ProjectionInput.SelectedValuePath="Key";
        ProjectionInput.ItemsSource=CameraSelection.Perspectives(family).Select(p=>KeyValuePair.Create(p,p switch
        {
            CameraPerspective.Dimetric2To1=>"Dimetric 2:1",
            CameraPerspective.TrueIsometric=>"True Isometric",
            CameraPerspective.DirectTop=>"Direct Top",
            CameraPerspective.TopDownThreeQuarter=>"Top-Down 3/4",
            CameraPerspective.HighTopDown=>"High Top-Down",
            CameraPerspective.DirectBottom=>"Direct Bottom",
            CameraPerspective.AngledUnderside=>"Angled Underside",
            CameraPerspective.StraightSide=>"Straight Side",
            _=>"Slightly Elevated Side"
        })).ToArray();
        ProjectionInput.SelectedIndex=0;
    }

    private void CameraPerspectiveChanged(object sender,SelectionChangedEventArgs e)
    {
        if(CameraElevationPanel is not null)
            CameraElevationPanel.Visibility=Equals(ProjectionInput.SelectedValue,CameraPerspective.TopDownThreeQuarter)?Visibility.Visible:Visibility.Collapsed;
    }

    private void AllDirectionsChanged(object sender,RoutedEventArgs e)
    {
        if(ViewInput is not null)ViewInput.IsEnabled=AllDirectionsInput.IsChecked!=true;
    }

    private string ExportStem()
    {
        var settings=AssetSettings.Load(Path.Combine(output!.Directory,"asset.settings.json"));
        var stem=Path.GetFileNameWithoutExtension(settings.Model);
        return settings.Camera is {} camera?stem+"_"+camera.Direction:stem;
    }

    private bool updatingDirections;
    private void UpdateDirectionalControls()
    {
        updatingDirections=true;
        try
        {
            RenderedDirectionInput.ItemsSource=directionalBatch.Select(item=>
                AssetSettings.Load(Path.Combine(item.Directory,"asset.settings.json")).Camera!.Direction).ToArray();
            RenderedDirectionInput.SelectedIndex=Array.FindIndex(directionalBatch,item=>item.Directory==output?.Directory);
            RenderedDirectionInput.Visibility=DirectionalExportButton.Visibility=
                directionalBatch.Length>1?Visibility.Visible:Visibility.Collapsed;
        }
        finally { updatingDirections=false; }
    }

    private void SelectRenderedDirection(object sender,SelectionChangedEventArgs e)
    {
        if(updatingDirections || RenderedDirectionInput.SelectedIndex is not (>=0 and <4))return;
        Try(()=>
        {
            int index=RenderedDirectionInput.SelectedIndex;
            try
            {
                if(!EnsureTimingApplied())return;
                var item=GeneratedAsset.Open(directionalBatch[index].Directory);
                var settings=AssetSettings.Load(Path.Combine(item.Directory,"asset.settings.json"));
                CloseComparison();
                output=item;
                LoadPlayback(item,settings.Fps);
                OutputInfo.Text=$"{settings.Width} × {settings.Height} · {item.Frames.Length} frame(s) · Direction {settings.Camera!.Direction}\n"+
                    string.Join("\n",item.Warnings);
                Status.Text=$"Previewing rendered direction {settings.Camera.Direction}.";
            }
            finally { UpdateDirectionalControls(); }
        });
    }

    private void SaveDirectionalBatch(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(directionalBatch.Length==0 || !EnsureTimingApplied())return;
        var dialog=new OpenFolderDialog{Title="Choose where to save the directional batch",InitialDirectory=directories.Initial("sheets")};
        if(dialog.ShowDialog()!=true)return;
        if(session?.IsManaged(dialog.FolderName)==true)throw new ArgumentException("Choose a folder outside temporary session storage.");
        var folder=AssetExport.SaveDirections(directionalBatch.Select(item=>GeneratedAsset.Open(item.Directory)).ToArray(),dialog.FolderName);
        directories.SelectedFolder("sheets",dialog.FolderName);
        Status.Text="Directional batch saved: "+folder;
        SavePreferences();
    });
}
