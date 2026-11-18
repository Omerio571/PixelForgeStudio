using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;
public partial class MainWindow
{
    private readonly DialogDirectories directories=new();
    private RenderSession? session;
    private readonly List<ConversionOutput> sessionOutputs=[];
    private string paletteName="Project default";
    private bool workflowReady;
    private void MarkPaletteEdited(){if(!paletteName.EndsWith(" (edited)",StringComparison.Ordinal))paletteName+=" (edited)";}
    private void Prepare(FileDialog dialog,string category)
    {
        dialog.InitialDirectory=directories.Initial(category);
        dialog.RestoreDirectory=true;
        if(dialog is SaveFileDialog)dialog.FileOk+=(_,e)=>
        {
            if(session?.IsManaged(dialog.FileName)==true){e.Cancel=true;ThemedMessage("Choose a save destination outside temporary session storage. Preserve the editable render to keep its complete working folder.","Permanent save destination",MessageBoxButton.OK,MessageBoxImage.Information);}
        };
    }
    private void RememberFile(string category,string file){directories.SelectedFile(category,file);SavePreferences();}
    private void SavePreferences()
    {
        if(!workflowReady)return;
        try{lastSettings=ReadSettings();}catch(Exception e)when(e is ArgumentException or FormatException){}
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);
            string staged=preferencePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(staged,JsonSerializer.Serialize(new Preferences(outputRoot,ZoomInput.SelectedIndex,CheckerInput.IsChecked==true,GameGroundInput.IsChecked==true,LoopInput.IsChecked==true,lastSettings,NumericControls(true).ToDictionary(c=>c.Name,c=>c.NumericMode),directories.Locations,ManageColors.IsExpanded,paletteName,OutlineColorInput.Text,selectedPalette.Select(c=>c.Hex).ToArray(),ThemeManager.Current,SettingsSections().ToDictionary(c=>c.Name,c=>c.IsExpanded),SettingsWidth.ActualWidth,pfaProfiles,activePfaProfile,recentPfa),AssetSettings.JsonOptions));
                File.Move(staged,preferencePath,true);
            }
            finally{try{File.Delete(staged);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Status.Text="Preferences could not be saved: "+e.Message;}
    }
    private void WorkflowClosing(object? sender,CancelEventArgs e)
    {
        if(cancellation is not null||inspectionCancellation is not null){e.Cancel=true;cancellation?.Cancel();inspectionCancellation?.Cancel();Status.Text="Stopping the active operation. Close again when it finishes.";return;}
        if(session is not null&&(sessionOutputs.Any(r=>session.IsManaged(r.Directory))||timingDirty))
        {
            int choice=Choose("Unsaved renders","Temporary renders and unapplied preview timing will be discarded when PixelForge closes. Exported files and preserved favorites remain safe. External unsaved Aseprite changes cannot be detected.","Discard temporary renders","Preserve renders","Cancel");
            if(choice==2){e.Cancel=true;return;}
            if(choice==1)try
            {
                if(!EnsureTimingApplied()){e.Cancel=true;return;}
                foreach(var render in sessionOutputs.Where(r=>session.IsManaged(r.Directory)).ToArray())
                {
                    var entry=history?.Entries.FirstOrDefault(r=>r.Directory==render.Directory);
                    if(entry is not null)PreserveEntry(entry);else TrackPreserved(render,session.Preserve(render));
                }
            }
            catch(Exception ex){e.Cancel=true;Error(ex);return;}
        }
        PausePlayback();SavePreferences();session?.Dispose();
        if(session?.Retained.Count>0)ThemedMessage("Changed or unrecognized session files were retained for safety. Review: "+string.Join("\n",session.Retained),"Session files preserved",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private int Choose(string title,string message,string first,string second,string? third=null,Rgb? color=null)
    {
        int result=third is null?1:2;
        var dialog=new Window{Owner=this,Title=title,Width=460,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=panel;
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap});
        if(color is Rgb c)panel.Children.Add(new Border{Height=34,Background=new SolidColorBrush(Color.FromRgb(c.R,c.G,c.B)),Margin=new Thickness(0,10,0,10)});
        var buttons=new WrapPanel();panel.Children.Add(buttons);
        string[] labels=third is null?[first,second]:[first,second,third];
        for(int n=0;n<labels.Length;n++){int value=n;var button=new Button{Content=labels[n],IsCancel=n==labels.Length-1,IsDefault=n==labels.Length-1};button.Click+=(_,_)=>{result=value;dialog.Close();};buttons.Children.Add(button);}
        dialog.ShowDialog();return result;
    }
    private void CopyColor(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(selectedSwatch<0||selectedSwatch>=selectedPalette.Length)return;
        Clipboard.SetText(selectedPalette[selectedSwatch].Hex);Status.Text="Copied "+selectedPalette[selectedSwatch].Hex;
    });
    private void PaletteCopyKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.C&&Keyboard.Modifiers==ModifierKeys.Control&&Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase&&(Swatches.IsKeyboardFocusWithin||ManageColors.IsKeyboardFocusWithin))
        {CopyColor(sender,e);e.Handled=true;}
    }
    private void UpdateSelectedColor()
    {
        bool selected=selectedSwatch>=0&&selectedSwatch<selectedPalette.Length;
        SelectedColorHex.Text=selected?selectedPalette[selectedSwatch].Hex:"Select a swatch";
        SelectedColorPreview.Background=selected?ColorBrush(selectedPalette[selectedSwatch]):Brushes.Transparent;
    }
    private static Brush ColorBrush(Rgb c)=>new SolidColorBrush(Color.FromRgb(c.R,c.G,c.B));
    private void OutlineColorChanged(object sender,TextChangedEventArgs e)=>UpdateOutlineColor();
    private void UpdateOutlineColor()
    {
        if(OutlineColorPreview is null||OutlineSwatches is null)return;
        try
        {
            var color=Palette.ParseColor(OutlineColorInput.Text);OutlineColorPreview.Background=ColorBrush(color);
            bool approved=selectedPalette.Contains(color);OutlineColorNotice.Text=approved?(.2126*color.R+.7152*color.G+.0722*color.B>140?"Approved palette color, but too light for outlines; choose a darker color.":"Approved palette color"):"Outside palette — add this color or select an approved swatch before generating outlines.";
            ApproveOutlineButton.Visibility=approved?Visibility.Collapsed:Visibility.Visible;
        }
        catch(Exception ex)when(ex is ArgumentException or FormatException){OutlineColorPreview.Background=Brushes.Transparent;OutlineColorNotice.Text="Enter a valid #RRGGBB color.";ApproveOutlineButton.Visibility=Visibility.Collapsed;}
    }
    private void ApproveOutlineColor(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var color=Palette.ParseColor(OutlineColorInput.Text);if(selectedPalette.Contains(color))return;
        if(selectedPalette.Length>=Palette.MaximumColors)throw new ArgumentException("Palette is full.");
        MarkPaletteEdited();sourcePalette=selectedPalette.Append(color).ToArray();selectedPalette=sourcePalette.ToArray();Indices.Clear();ShowPalette();
    });
    private void PickOutlineColor(object sender,RoutedEventArgs e)
    {
        Rgb initial;try{initial=Palette.ParseColor(OutlineColorInput.Text);}catch(Exception ex)when(ex is ArgumentException or FormatException){initial=new Rgb(23,25,35);}
        var dialog=new Window{Owner=this,Title="Outline color picker",Width=340,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=panel;var sample=new Border{Height=44};panel.Children.Add(sample);var value=new TextBlock();panel.Children.Add(value);
        var sliders=new[]{initial.R,initial.G,initial.B}.Select(v=>new Slider{Minimum=0,Maximum=255,Value=v,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,8)}).ToArray();
        Rgb Current()=>new((byte)sliders[0].Value,(byte)sliders[1].Value,(byte)sliders[2].Value);
        void Update(){sample.Background=ColorBrush(Current());value.Text=Current().Hex;}
        for(int n=0;n<3;n++){panel.Children.Add(new TextBlock{Text=new[]{"Red","Green","Blue"}[n]});panel.Children.Add(sliders[n]);sliders[n].ValueChanged+=(_,_)=>Update();}
        var buttons=new WrapPanel();panel.Children.Add(buttons);var apply=new Button{Content="Use color"};apply.Click+=(_,_)=>{OutlineColorInput.Text=Current().Hex;dialog.Close();};buttons.Children.Add(apply);buttons.Children.Add(new Button{Content="Cancel",IsCancel=true});Update();dialog.ShowDialog();
    }
    private RenderEntry PreserveEntry(RenderEntry entry)
    {
        var saved=session!.Preserve(entry.Output);
        TrackPreserved(entry.Output,saved);
        var replacement=history!.Relocate(entry,saved);
        Status.Text="Editable render preserved permanently: "+saved.Directory;return replacement;
    }
    private void TrackPreserved(ConversionOutput original,ConversionOutput saved)
    {
        if(output?.Directory==original.Directory){output=saved;if(session?.IsManaged(saved.Directory)==false)OutputInfo.Text=OutputInfo.Text.Replace("temporary session","preserved editable render");}
        if(compareA?.Directory==original.Directory)compareA=saved;if(compareB?.Directory==original.Directory)compareB=saved;
        for(int n=0;n<directionalBatch.Length;n++)
            if(directionalBatch[n].Directory==original.Directory)directionalBatch[n]=saved;
        for(int n=0;n<sessionOutputs.Count;n++)if(sessionOutputs[n].Directory==original.Directory)sessionOutputs[n]=saved;
    }
    private void PreserveCurrent(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(output is null||session is null||!EnsureTimingApplied())return;
        PreserveCurrentCore();
    });
    private void PreserveCurrentCore()
    {
        if(output is null||session is null)return;
        var entry=history?.Entries.FirstOrDefault(r=>r.Directory==output.Directory);
        if(entry is not null){PreserveEntry(entry);RefreshHistory();}
        else TrackPreserved(output,session.Preserve(output));
    }
}
