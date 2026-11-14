using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;
public partial class MainWindow : Window
{
    private Rgb[] sourcePalette=Palette.Default.ToArray(),selectedPalette=Palette.Default.ToArray();
    private ConversionOutput? output;
    private int selectedSwatch=-1;
    private CancellationTokenSource? cancellation;
    private AssetSettings? lastSettings;
    private readonly Dictionary<TextBox,(string Text,double Value)> preciseTexts=[];
    private readonly HashSet<TextBox> preciseEdits=[];
    private bool settingPreciseText;
    private void PreciseTextChanged(object sender,TextChangedEventArgs e){if(!settingPreciseText)preciseEdits.Add((TextBox)sender);}
    private void SetPreciseText(TextBox box,double value){string text=UiNumbers.Format(value);settingPreciseText=true;box.Text=text;settingPreciseText=false;preciseTexts[box]=(text,value);preciseEdits.Remove(box);}
    private bool TryPreciseText(TextBox box,out double value)
    {
        if(!preciseEdits.Contains(box)&&preciseTexts.TryGetValue(box,out var saved)&&box.Text==saved.Text){value=saved.Value;return true;}
        return UiNumbers.TryParse(box.Text,2,out value);
    }
    private readonly Toolchain tools=Toolchain.Discover();
    private static string ProfileDirectory=>Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PixelForgeStudio");
    private readonly string preferencePath=Path.Combine(ProfileDirectory,"preferences.json");
    private string outputRoot=Environment.GetEnvironmentVariable("PIXELFORGE_OUTPUT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"PixelForge Studio");
    public MainWindow()
    {
        InitializeComponent();ViewInput.ItemsSource=Enum.GetValues<CameraView>();ViewInput.SelectedItem=CameraView.IsometricSE;
        ProjectionInput.ItemsSource=Enum.GetValues<Projection>();ProjectionInput.SelectedItem=Projection.Dimetric2To1;
        OutlineTypeInput.ItemsSource=Enum.GetValues<OutlineStyle>();OutlineTypeInput.SelectedItem=OutlineStyle.OuterSilhouette;
        FitBoundsInput.ItemsSource=new Dictionary<FitBoundsSource,string>{{FitBoundsSource.ObjectOnly,"Object Only"},{FitBoundsSource.ObjectAndGroundShadow,"Object + Ground Shadow"}};FitBoundsInput.SelectedValue=FitBoundsSource.ObjectOnly;
        GroupedModeInput.ItemsSource=Enum.GetValues<GroupedOutlineMode>();GroupedModeInput.SelectedItem=GroupedOutlineMode.Unified;
        ShowPalette();SetChecker();session=new RenderSession(ProfileDirectory);
        try{if(File.Exists(preferencePath)){var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencePath),AssetSettings.JsonOptions);if(p is not null){outputRoot=p.OutputRoot;ZoomInput.SelectedIndex=Math.Clamp(p.Zoom,0,5);GameGroundInput.IsChecked=p.Ground;CheckerInput.IsChecked=!p.Ground;LoopInput.IsChecked=p.Loop;if(p.LastSettings is not null)ApplySettings(p.LastSettings);if(p.DialogPaths is not null)directories.Locations=p.DialogPaths;ManageColors.IsExpanded=p.Manage;paletteName=p.PaletteName??"Palette";if(p.PaletteColors is not null){sourcePalette=p.PaletteColors.Select(Palette.ParseColor).ToArray();selectedPalette=sourcePalette.ToArray();Indices.Clear();}if(p.OutlineDraft is not null)OutlineColorInput.Text=p.OutlineDraft;ShowPalette();foreach(var control in NumericControls(true))if(p.NumericModes?.TryGetValue(control.Name,out bool mode)==true)control.NumericMode=mode;}}}catch(Exception e)when(e is IOException or JsonException or ArgumentException){Status.Text="Preferences could not be loaded; using defaults.";}
        InitializeHistory();UpdatePresetSelection();
        workflowReady=true;Closing+=WorkflowClosing;PreviewKeyDown+=PaletteCopyKey;
        ManageColors.Expanded+=(_,_)=>SavePreferences();ManageColors.Collapsed+=(_,_)=>SavePreferences();
        if(session.Retained.Count>0){Status.Text=$"Recovery: {session.Retained.Count} changed or unrecognized session(s) retained for safety. See Technical details.";ErrorDetails.Text=string.Join(Environment.NewLine,session.Retained);}

    }
    private sealed record Preferences(string OutputRoot,int Zoom,bool Checker,bool Ground=false,bool Loop=true,AssetSettings? LastSettings=null,Dictionary<string,bool>? NumericModes=null,Dictionary<string,string>? DialogPaths=null,bool Manage=false,string? PaletteName=null,string? OutlineDraft=null,string[]? PaletteColors=null);
    private void Try(Action action){try{action();}catch(Exception e){Error(e);}}
    private void Error(Exception e){Status.Text=e.Message;ErrorDetails.Text=e.ToString();MessageBox.Show(this,e.Message,"PixelForge Studio",MessageBoxButton.OK,MessageBoxImage.Warning);}
    private async void BrowseModel(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Blender model|*.blend"};Prepare(d,"models");if(d.ShowDialog()==true){ModelPath.Text=d.FileName;session?.ProtectInput(d.FileName);RememberFile("models",d.FileName);await InspectSelectedModel();}}
    private async void ImportPalette(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="Palettes, PNG and Aseprite files|*.gpl;*.pal;*.png;*.aseprite;*.ase|PNG swatch image|*.png|GPL / JASC PAL|*.gpl;*.pal|Aseprite project|*.aseprite;*.ase"};Prepare(dialog,"palettes");if(dialog.ShowDialog()!=true)return;
        try
        {
            Rgb[] imported;
            if(Path.GetExtension(dialog.FileName).Equals(".png",StringComparison.OrdinalIgnoreCase))imported=await Task.Run(()=>Palette.LoadPng(dialog.FileName));
            else if(Path.GetExtension(dialog.FileName).Equals(".gpl",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(dialog.FileName).Equals(".pal",StringComparison.OrdinalIgnoreCase))imported=Palette.Load(dialog.FileName);
            else
            {
                imported=await Palette.LoadNative(dialog.FileName,tools.Aseprite,session?.DirectoryPath);
            }
            if(imported.Length>256&&MessageBox.Show(this,$"This image or project contains {imported.Length} distinct opaque colors and may be photographic. Import all colors? Output uses RGB Aseprite layers; indexed mode is not used.","Large palette",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            session?.ProtectInput(dialog.FileName);paletteName=Path.GetFileNameWithoutExtension(dialog.FileName);RememberFile("palettes",dialog.FileName);sourcePalette=imported;selectedPalette=sourcePalette.ToArray();selectedSwatch=-1;Indices.Text="";ShowPalette();Status.Text="Palette imported. PNGs use every distinct fully opaque color in row-major order; partial alpha is ignored.";
        }catch(Exception ex){Error(ex);}
    }
    private void ShowPalette()
    {
        Swatches.Children.Clear();for(int i=0;i<selectedPalette.Length;i++)
        {
            int index=i;var c=selectedPalette[i];var swatch=new Border{Width=21,Height=21,Margin=new Thickness(2),Background=new SolidColorBrush(Color.FromRgb(c.R,c.G,c.B)),ToolTip=$"{i}: {c.Hex}",BorderBrush=i==selectedSwatch?Brushes.White:Brushes.Gray,BorderThickness=new Thickness(i==selectedSwatch?2:.5)};
            swatch.Focusable=true;swatch.MouseLeftButtonDown+=(_,_)=>{selectedSwatch=index;ShowPalette();((Border)Swatches.Children[index]).Focus();};Swatches.Children.Add(swatch);
        }
        PaletteInfo.Text=$"{paletteName} · {selectedPalette.Length} colors · strict palette";
        OutlineSwatches.Children.Clear();foreach(var color in selectedPalette){var button=new Button{Width=23,Height=23,Padding=new Thickness(0),Margin=new Thickness(2),Background=ColorBrush(color),ToolTip=color.Hex};button.Click+=(_,_)=>OutlineColorInput.Text=color.Hex;OutlineSwatches.Children.Add(button);}
        UpdateSelectedColor();UpdateOutlineColor();

    }
    private void EditPalette(Action<List<Rgb>> edit)=>Try(()=>{if(selectedSwatch<0||selectedSwatch>=selectedPalette.Length)throw new ArgumentException("Click a palette swatch first.");var colors=selectedPalette.ToList();edit(colors);if(!colors.SequenceEqual(selectedPalette))MarkPaletteEdited();sourcePalette=colors.ToArray();selectedPalette=sourcePalette.ToArray();Indices.Clear();selectedSwatch=Math.Min(selectedSwatch,colors.Count-1);ShowPalette();});
    private void RemoveColor(object sender,RoutedEventArgs e){if(selectedSwatch<0||selectedSwatch>=selectedPalette.Length)return;var color=selectedPalette[selectedSwatch];if(Choose("Remove palette color",$"Are you sure you want to remove {color.Hex} from this palette?","Remove","Cancel",color:color)==0)EditPalette(c=>c.RemoveAt(selectedSwatch));}
    private void MoveColorLeft(object sender,RoutedEventArgs e)=>EditPalette(c=>{if(selectedSwatch>0){(c[selectedSwatch-1],c[selectedSwatch])=(c[selectedSwatch],c[selectedSwatch-1]);selectedSwatch--;}});
    private void MoveColorRight(object sender,RoutedEventArgs e)=>EditPalette(c=>{if(selectedSwatch<c.Count-1){(c[selectedSwatch+1],c[selectedSwatch])=(c[selectedSwatch],c[selectedSwatch+1]);selectedSwatch++;}});
    private void ScaleModeChanged(object sender,RoutedEventArgs e){if(ScaleHelp is not null)ScaleHelp.Text=WorldScaleInput.IsChecked==true?"Fixed density: increasing px/unit makes the model larger. Framing is checked before rendering.":"Fit to Canvas preserves proportions; pixel density is inactive.";}
    private void ResetPalette(object sender,RoutedEventArgs e){paletteName="Project default";selectedSwatch=-1;sourcePalette=Palette.Default.ToArray();selectedPalette=sourcePalette.ToArray();Indices.Text="";ShowPalette();}
    private void ClearPalette(object sender,RoutedEventArgs e){if(selectedPalette.Length>0&&Choose("Clear palette","Remove all colors from the working palette? The imported palette file is not modified.","Clear","Cancel")!=0)return;paletteName="Empty palette";selectedSwatch=-1;sourcePalette=[];selectedPalette=[];Indices.Text="";ShowPalette();Status.Text="Palette cleared. Import or add colors before generating.";}
    private void ApplyIndices(object sender,RoutedEventArgs e)=>Try(()=>{selectedPalette=Palette.SelectIndices(sourcePalette,Indices.Text);ShowPalette();});
    private void AddColors(object sender,RoutedEventArgs e)=>Try(()=>{var added=Palette.ParseList(DirectColors.Text);var combined=selectedPalette.Concat(added).Distinct().ToArray();if(combined.Length>Palette.MaximumColors)throw new ArgumentException("Use at most 4096 approved colors.");if(combined.Length!=selectedPalette.Length)MarkPaletteEdited();sourcePalette=combined;selectedPalette=sourcePalette.ToArray();Indices.Text="";DirectColors.Clear();ShowPalette();});
    private void Preset(object sender,RoutedEventArgs e){string size=((Button)sender).Tag.ToString()!;WidthInput.Text=size;HeightInput.Text=size;}
    private AssetSettings ReadSettings()
    {
        foreach(var control in NumericControls())if(!control.Commit())throw new ArgumentException(control.ValidationMessage);
        selectedPalette=Palette.SelectIndices(sourcePalette,Indices.Text);
        if(!int.TryParse(WidthInput.Text,out var width)||!int.TryParse(HeightInput.Text,out var height))throw new ArgumentException("Enter whole numbers for width and height.");
        double padding=PaddingInput.Value;
        bool validDensity=TryPreciseText(PixelsPerUnitInput,out var ppu)&&double.IsFinite(ppu)&&ppu>0&&ppu<=2048;
        if(!validDensity&&WorldScaleInput.IsChecked==true)throw new ArgumentException("Enter pixels per unit greater than zero and at most 2048.");
        if(!validDensity)ppu=16;
        var settings=new AssetSettings{Model=ModelPath.Text,Width=width,Height=height,View=(CameraView)ViewInput.SelectedItem,Projection=(Projection)ProjectionInput.SelectedItem,Padding=padding,WorldScale=WorldScaleInput.IsChecked==true,FitBounds=(FitBoundsSource)FitBoundsInput.SelectedValue,PixelsPerUnit=ppu,SamplesPerPixel=SamplingInput.SelectedIndex==0?3:1,Colors=selectedPalette.Select(c=>c.Hex).ToArray(),LightAzimuth=LightAzimuthInput.Value,LightElevation=LightElevationInput.Value,LightIntensity=LightIntensityInput.Value,AmbientIntensity=AmbientInput.Value,ShadowIntensity=ShadowInput.Value,CastShadows=CastShadowsInput.IsChecked==true,GroundShadows=GroundShadowsInput.IsChecked==true,GroundShadowStrength=GroundStrengthInput.Value,GroundShadowSoftness=GroundSoftnessInput.Value,Outlines=OutlineInput.IsChecked==true,RespectOutlineGroups=RespectGroupsInput.IsChecked==true,GroupedOutlines=(GroupedOutlineMode)GroupedModeInput.SelectedItem,OutlineType=(OutlineStyle)OutlineTypeInput.SelectedItem,OutlineThickness=OutlineThicknessInput.SelectedIndex+1,OutlineColor=OutlineColorInput.Text,EdgeHighlights=EdgeHighlightsInput.IsChecked==true,EdgeHighlightStrength=EdgeStrengthInput.Value};settings=WithAnimationSettings(settings);settings.Validate();return settings;
    }
    private async void Generate(object sender,RoutedEventArgs e)
    {
        try
        {
            var settings=ReadSettings();session?.ProtectInput(settings.Model);CloseComparison();lastSettings=settings;PausePlayback();cancellation=new();SettingsPanel.IsEnabled=false;GenerateButton.IsEnabled=false;CompareButton.IsEnabled=false;CancelButton.IsEnabled=true;ExportPanel.IsEnabled=false;Progress.IsIndeterminate=true;
            output=await new ConversionPipeline(tools).Generate(settings,session!.DirectoryPath,new Progress<string>(s=>Status.Text=s),cancellation.Token);
            LoadPlayback(output,settings.Fps);EmptyPreview.Visibility=Visibility.Collapsed;UpdateZoom();ExportPanel.IsEnabled=true;
            GeneratedAsset.Save(output);sessionOutputs.Add(output);await Task.Run(()=>session!.Snapshot(output.Directory));RememberCompleted(output);
            using var model=JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Directory,"model.json")));ModelInfo.Text=$"{model.RootElement.GetProperty("objects").EnumerateObject().Count()} objects · {model.RootElement.GetProperty("materials").GetArrayLength()} materials";
            OutputInfo.Text=$"{settings.Width} × {settings.Height} · {output.Frames.Length} frame(s) · temporary session · native cels verified\n"+(output.Warnings.Length>0?string.Join("\n",output.Warnings):"Review visual quality and motion; technical validation passed.");
        }catch(OperationCanceledException){Status.Text="Generation canceled. Earlier assets are safe.";}catch(Exception ex){Error(ex);}finally{SettingsPanel.IsEnabled=true;GenerateButton.IsEnabled=true;CompareButton.IsEnabled=true;CancelButton.IsEnabled=false;Progress.IsIndeterminate=false;cancellation?.Dispose();cancellation=null;}
    }
    private void Cancel(object sender,RoutedEventArgs e)=>cancellation?.Cancel();
    private void SavePng(object sender,RoutedEventArgs e)=>Try(()=>{if(output is null)return;var d=new SaveFileDialog{Filter="PNG sprite|*.png",FileName=Path.GetFileNameWithoutExtension(ModelPath.Text)+".png",OverwritePrompt=true};Prepare(d,"png");if(d.ShowDialog()==true){AssetExport.SaveFrame(output,playback?.CurrentFrame??0,d.FileName);RememberFile("png",d.FileName);Status.Text="Current PNG frame saved: "+d.FileName;}});
    private void SaveNative(object sender,RoutedEventArgs e)=>Try(()=>{if(output is null||!EnsureTimingApplied())return;var d=new SaveFileDialog{Filter="Aseprite project|*.aseprite",FileName=Path.GetFileNameWithoutExtension(ModelPath.Text)+".aseprite",OverwritePrompt=true};Prepare(d,"native");if(d.ShowDialog()==true){AssetExport.SaveNative(output,d.FileName);RememberFile("native",d.FileName);Status.Text="Aseprite file saved: "+d.FileName;}});
    private void OpenAseprite(object sender,RoutedEventArgs e)=>Try(()=>{if(output is null||!EnsureTimingApplied())return;if(session?.IsManaged(output.Directory)==true){if(Choose("Preserve editable render","Opening Aseprite preserves this full editable render in the permanent application Library. Use Save As in Aseprite for your chosen destination. External unsaved edits cannot be detected.","Preserve and open","Cancel")!=0)return;PreserveCurrent(sender,e);if(session.IsManaged(output.Directory))return;}if(!File.Exists(tools.Aseprite))throw new FileNotFoundException("Aseprite was not found. Set PIXELFORGE_ASEPRITE to its executable path.");var p=new ProcessStartInfo(tools.Aseprite){UseShellExecute=false};p.ArgumentList.Add(output.Aseprite);Process.Start(p);});
    private void ShowOutput(object sender,RoutedEventArgs e)=>Try(()=>{if(output is not null)Process.Start(new ProcessStartInfo("explorer.exe",output.Directory){UseShellExecute=true});});
    private void SaveSettings(object sender,RoutedEventArgs e)=>Try(()=>{var s=ReadSettings();var d=new SaveFileDialog{Filter="Asset settings|*.json",FileName="asset.settings.json"};Prepare(d,"settings");if(d.ShowDialog()==true){s.Save(d.FileName);RememberFile("settings",d.FileName);}});
    private void LoadSettings(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var d=new OpenFileDialog{Filter="Asset settings|*.json"};Prepare(d,"settings");if(d.ShowDialog()!=true)return;
        ApplySettings(AssetSettings.Load(d.FileName));RememberFile("settings",d.FileName);
        Status.Text="Asset settings loaded, including lighting, shadows and animation.";
    });
    private void ApplySettings(AssetSettings s)
    {
        s.Validate();lastSettings=s;ModelPath.Text=s.Model;WidthInput.Text=s.Width.ToString();HeightInput.Text=s.Height.ToString();
        ViewInput.SelectedItem=s.View;ProjectionInput.SelectedItem=s.Projection;PaddingInput.Value=s.Padding;
        FitBoundsInput.SelectedValue=s.FitBounds;GroupedModeInput.SelectedItem=s.GroupedOutlines;
        WorldScaleInput.IsChecked=s.WorldScale;SetPreciseText(PixelsPerUnitInput,s.PixelsPerUnit);SamplingInput.SelectedIndex=s.SamplesPerPixel==3?0:1;
        LightAzimuthInput.Value=s.LightAzimuth;LightElevationInput.Value=s.LightElevation;LightIntensityInput.Value=s.LightIntensity;
        AmbientInput.Value=s.AmbientIntensity;ShadowInput.Value=s.ShadowIntensity;CastShadowsInput.IsChecked=s.CastShadows;
        GroundShadowsInput.IsChecked=s.GroundShadows;GroundStrengthInput.Value=s.GroundShadowStrength;GroundSoftnessInput.Value=s.GroundShadowSoftness;
        AnimationInput.IsChecked=s.Animation;SingleFrameInput.Text=s.Frame?.ToString()??"";StartFrameInput.Text=s.StartFrame.ToString();EndFrameInput.Text=s.EndFrame.ToString();FrameStepInput.Text=s.FrameStep.ToString();FpsInput.Text=s.Fps.ToString();TagInput.Text=s.AnimationTag;
        EdgeHighlightsInput.IsChecked=s.EdgeHighlights;EdgeStrengthInput.Value=s.EdgeHighlightStrength;RespectGroupsInput.IsChecked=s.RespectOutlineGroups;OutlineInput.IsChecked=s.Outlines;OutlineTypeInput.SelectedItem=s.OutlineType;OutlineThicknessInput.SelectedIndex=s.OutlineThickness-1;
        paletteName="Asset palette";selectedSwatch=-1;sourcePalette=s.Colors.Select(Palette.ParseColor).ToArray();selectedPalette=sourcePalette.ToArray();Indices.Clear();ShowPalette();
        OutlineColorInput.Text=s.OutlineColor;
    }
    private void ZoomChanged(object sender,SelectionChangedEventArgs e)=>UpdateZoom();
    private void PreviewSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateZoom();
    private void UpdateZoom(){UpdateComparisonZoom();if(PreviewImage?.Source is not BitmapSource bitmap)return;double zoom=ZoomInput.SelectedIndex==0?Math.Max(1,Math.Floor(Math.Min(Math.Max(1,PreviewScroll.ActualWidth-20)/bitmap.PixelWidth,Math.Max(1,PreviewScroll.ActualHeight-20)/bitmap.PixelHeight))):Math.Pow(2,ZoomInput.SelectedIndex-1);PreviewImage.Width=bitmap.PixelWidth*zoom;PreviewImage.Height=bitmap.PixelHeight*zoom;}
    private void CheckerChanged(object sender,RoutedEventArgs e){if(PreviewBorder is not null){SetChecker();SavePreferences();}}
    private void SetChecker(){if(GameGroundInput?.IsChecked==true){PreviewBorder.Background=new SolidColorBrush(Color.FromRgb(62,75,55));if(ComparisonBackground is not null)ComparisonBackground.Background=PreviewBorder.Background;return;}var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(37,42,51)),null,new RectangleGeometry(new Rect(0,0,16,16))));if(CheckerInput.IsChecked==true){var brush=new SolidColorBrush(Color.FromRgb(47,53,64));group.Children.Add(new GeometryDrawing(brush,null,new RectangleGeometry(new Rect(0,0,8,8))));group.Children.Add(new GeometryDrawing(brush,null,new RectangleGeometry(new Rect(8,8,8,8))));}PreviewBorder.Background=new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,16,16),ViewportUnits=BrushMappingMode.Absolute};if(ComparisonBackground is not null)ComparisonBackground.Background=PreviewBorder.Background;}
}
