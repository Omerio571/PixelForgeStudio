using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;
public partial class MainWindow
{
    private CancellationTokenSource? inspectionCancellation;
    private PlaybackTimeline? playback;
    private BitmapImage[] playbackFrames=[];
    private DispatcherTimer? playbackTimer;
    private readonly Stopwatch playbackClock=new();
    private bool updatingTimeline;
    private int playbackFps;
    private bool changingPreviewFps;
    private bool timingDirty;

    private async void InspectRange(object sender,RoutedEventArgs e)=>await InspectSelectedModel();
    private async Task InspectSelectedModel()
    {
        inspectionCancellation?.Cancel();var tokenSource=new CancellationTokenSource();inspectionCancellation=tokenSource;
        GenerateButton.IsEnabled=false;ModelInfo.Text="Inspecting Blender scene and animation range…";InspectionInfo.Text="Inspecting source range…";
        try
        {
            var model=ModelPath.Text;session?.ProtectInput(model);var info=await ModelInspection.Read(model,tools,tokenSource.Token,session?.DirectoryPath);
            if(info.WorkingDirectory is not null)session?.Snapshot(info.WorkingDirectory);if(model!=ModelPath.Text)return;
            StartFrameInput.Text=info.Start.ToString();EndFrameInput.Text=info.End.ToString();SingleFrameInput.Text=info.Current.ToString();FpsInput.Text=Math.Clamp((int)Math.Round(info.Fps),1,120).ToString();
            InspectionInfo.Text=$"Source frames {info.Start}–{info.End} · {info.Fps:0.##} FPS";
            ModelInfo.Text=$"{Path.GetFileName(model)} · frames {info.Start}–{info.End} · {info.Fps:0.##} source FPS\n"+(info.HasAnimation?"Animation data detected.":"No object animation detected; the scene frame range is available.");
        }
        catch(OperationCanceledException){}
        catch(Exception ex){Error(ex);}
        finally{if(inspectionCancellation==tokenSource){inspectionCancellation=null;GenerateButton.IsEnabled=true;}tokenSource.Dispose();}
    }
    private void AnimationModeChanged(object sender,RoutedEventArgs e)
    {
        if(AnimationSettingsPanel is null)return;
        AnimationSettingsPanel.Visibility=AnimationInput.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        SingleFramePanel.Visibility=AnimationInput.IsChecked==true?Visibility.Collapsed:Visibility.Visible;
    }
    private AssetSettings WithAnimationSettings(AssetSettings settings)
    {
        static int Number(TextBox input,string name){if(!int.TryParse(input.Text,out var value))throw new ArgumentException($"Enter a whole number for {name}.");return value;}
        bool animated=AnimationInput.IsChecked==true;
        int? frame=animated?(int.TryParse(SingleFrameInput.Text,out var remembered)?remembered:null):string.IsNullOrWhiteSpace(SingleFrameInput.Text)?null:Number(SingleFrameInput,"the Blender frame");
        int Read(TextBox box,string label,int fallback)=>animated?Number(box,label):int.TryParse(box.Text,out var value)?value:fallback;
        return settings with{Animation=animated,Frame=frame,StartFrame=Read(StartFrameInput,"start frame",1),EndFrame=Read(EndFrameInput,"end frame",12),FrameStep=Read(FrameStepInput,"frame step",1),Fps=Read(FpsInput,"playback FPS",12),AnimationTag=TagInput.Text};
    }
    private void LoadPlayback(ConversionOutput generated,int fps)
    {
        PausePlayback();UpdateCanvasInfo(generated);playbackFps=fps;timingDirty=false;
        changingPreviewFps=true;PreviewFpsInput.Text=fps.ToString();changingPreviewFps=false;
        ApplyTimingButton.IsEnabled=false;TimingInfo.Text="Playback timing is independent of Blender frame sampling.";
        playbackFrames=generated.Frames.Select(frame=>
        {
            using var stream=File.OpenRead(frame.Png);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }).ToArray();
        LoadNormalPreview(generated);
        playback=new PlaybackTimeline(generated.Frames.Select(f=>f.DurationMs).ToArray());
        TimelinePanel.Visibility=playbackFrames.Length>1?Visibility.Visible:Visibility.Collapsed;
        updatingTimeline=true;TimelineInput.Maximum=Math.Max(1,playbackFrames.Length-1);TimelineInput.Value=0;updatingTimeline=false;ShowPlaybackFrame();
    }
    private void ShowPlaybackFrame()
    {
        if(playback is null||playbackFrames.Length==0)return;
        PreviewImage.Source=LightingPreview(playback.CurrentFrame);
        updatingTimeline=true;TimelineInput.Value=playback.CurrentFrame;updatingTimeline=false;
        FrameInfo.Text=$"{playback.CurrentFrame+1} / {playbackFrames.Length} · {playbackFps} FPS · Blender {output!.Frames[playback.CurrentFrame].SourceFrame}";
        DurationInfo.Text=$"{playback.TotalDurationMs/1000.0:0.##} s total";
        UpdateZoom();
    }
    private void PausePlayback(){playbackTimer?.Stop();playbackClock.Stop();if(PlayButton is not null)PlayButton.Content="Play";}
    private void PlayPause(object sender,RoutedEventArgs e)
    {
        if(playbackTimer?.IsEnabled==true){PausePlayback();return;}if(playback is null)return;
        if(playback.Completed)playback.Seek(0);
        playbackTimer??=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(8)};
        playbackTimer.Tick-=PlaybackTick;playbackTimer.Tick+=PlaybackTick;playbackClock.Restart();playbackTimer.Start();PlayButton.Content="Pause";
    }
    private void PlaybackTick(object? sender,EventArgs e)
    {
        double elapsed=playbackClock.Elapsed.TotalMilliseconds;playbackClock.Restart();playback?.Advance(elapsed,LoopInput.IsChecked==true);ShowPlaybackFrame();if(playback?.Completed==true)PausePlayback();
    }
    private void PreviousFrame(object sender,RoutedEventArgs e){PausePlayback();playback?.Seek((playback.CurrentFrame-1+playbackFrames.Length)%playbackFrames.Length);ShowPlaybackFrame();}
    private void NextFrame(object sender,RoutedEventArgs e){PausePlayback();playback?.Seek((playback.CurrentFrame+1)%playbackFrames.Length);ShowPlaybackFrame();}
    private void ScrubFrame(object sender,RoutedPropertyChangedEventArgs<double> e){if(updatingTimeline||playback is null)return;PausePlayback();playback.Seek((int)Math.Round(TimelineInput.Value));ShowPlaybackFrame();}
    private void SaveSheet(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(output is null||!EnsureTimingApplied())return;var dialog=new SaveFileDialog{Filter="PNG spritesheet|*.png",FileName=ExportStem()+".sheet.png",OverwritePrompt=true};
        Prepare(dialog,"sheets");if(dialog.ShowDialog()!=true)return;
        if(File.Exists(Path.ChangeExtension(dialog.FileName,"json"))&&ThemedMessage("Replace the existing companion JSON metadata too?","Spritesheet metadata",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        AssetExport.SaveSpritesheet(output,dialog.FileName);RememberFile("sheets",dialog.FileName);Status.Text="Spritesheet PNG and JSON saved: "+dialog.FileName;
    });
    private void SaveFrames(object sender,RoutedEventArgs e)=>Try(()=>
    {
        if(output is null||!EnsureTimingApplied())return;var dialog=new OpenFolderDialog{Title="Choose a folder for a new PNG-frame export directory"};dialog.InitialDirectory=directories.Initial("frames");if(dialog.ShowDialog()==true){if(session?.IsManaged(dialog.FolderName)==true)throw new ArgumentException("Export frames outside temporary session storage.");Status.Text="PNG frames saved: "+AssetExport.SaveFrames(output,dialog.FolderName);directories.SelectedFolder("frames",dialog.FolderName);SavePreferences();}
    });
    private async void ReloadEdits(object sender,RoutedEventArgs e)
    {
        if(output is null)return;int pendingFps=playbackFps;bool pendingTiming=timingDirty;PausePlayback();ExportPanel.IsEnabled=false;GenerateButton.IsEnabled=false;
        try
        {
            Status.Text="Reloading saved native frames; the native file is not modified…";
            if(session?.IsManaged(output.Directory)==true)
            {
                if(Choose("Preserve editable render","Reloading native edits preserves this full editable render in the permanent application Library. External unsaved Aseprite changes cannot be detected.","Preserve and reload","Cancel")!=0)return;
                PreserveCurrentCore();if(session.IsManaged(output.Directory))throw new IOException("Preserve the edited render before reloading it.");
            }
            var previous=output;output=await NativeEdits.Read(output,tools);var settings=AssetSettings.Load(Path.Combine(output.Directory,"asset.settings.json"));LoadPlayback(output,settings.Fps);
            GeneratedAsset.Save(output);
            var entry=history?.Entries.FirstOrDefault(r=>r.Directory==previous.Directory);
            if(entry is not null)history!.Relocate(entry,output);
            TrackPreserved(previous,output);RefreshHistory();
            if(pendingTiming)PreviewFpsInput.Text=pendingFps.ToString();
            OutputInfo.Text=$"{settings.Width} × {settings.Height} · {output.Frames.Length} saved native frame(s) · palette/alpha validated"
                +(output.Warnings.Length>0?"\n"+string.Join("\n",output.Warnings):"");
            Status.Text="Saved Aseprite edits loaded. Preview and all PNG/sheet exports now include these corrections.";
        }
        catch(Exception ex){Error(ex);}
        finally{ExportPanel.IsEnabled=true;GenerateButton.IsEnabled=true;}
    }
    private void PreviewFpsChanged(object sender,TextChangedEventArgs e)
    {
        if(changingPreviewFps||playback is null||output is null)return;
        if(!int.TryParse(PreviewFpsInput.Text,out int fps)||fps is < 1 or > 120)
        {TimingInfo.Text="Enter a whole number from 1–120 FPS. The last valid speed is still playing.";ApplyTimingButton.IsEnabled=false;return;}
        if(fps==playbackFps){ApplyTimingButton.IsEnabled=timingDirty;TimingInfo.Text=timingDirty?"Preview timing is not saved yet. Use Apply / Save timing.":"Playback timing is independent of Blender frame sampling.";return;}
        output=AnimationTiming.Preview(output,fps);
        playback.SetDurations(output.Frames.Select(f=>f.DurationMs).ToArray());
        playbackFps=fps;FpsInput.Text=fps.ToString();timingDirty=true;ApplyTimingButton.IsEnabled=true;
        if(playbackTimer?.IsEnabled==true)playbackClock.Restart();
        TimingInfo.Text="Preview updated immediately. Apply / Save timing to persist durations; no frames are rendered.";
        ShowPlaybackFrame();
    }
    private bool EnsureTimingApplied()
    {
        if(output is null||output.Frames.Length<2)return true;
        if(!int.TryParse(PreviewFpsInput.Text,out int fps)||fps is < 1 or > 120)throw new ArgumentException("Enter playback FPS from 1–120 before saving.");
        if(!timingDirty)return true;
        const string notice="Save and close this animation in Aseprite first. Unsaved external changes cannot be detected or preserved.\n\nApply timing to the latest SAVED native file? Only duration fields change; every cel, layer, tag and artwork byte is preserved, with a backup. PNG pixels are untouched. Reopen the file in Aseprite afterward to see the new speed.\n\nUse Reload saved edits first if saved pixel corrections should also appear in the preview and PNG exports.";
        if(ThemedMessage(notice,"Apply animation timing",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return false;
        if(session?.IsManaged(output.Directory)==true&&!session.Unchanged(output.Directory))
        {PreserveCurrentCore();if(session.IsManaged(output.Directory))throw new IOException("Preserve the edited render before applying timing.");}
        output=AnimationTiming.Apply(output,fps);
        if(session?.IsManaged(output.Directory)==true)session.AcceptTimingChanges(output.Directory);
        var entry=history?.Entries.FirstOrDefault(r=>r.Directory==output.Directory);if(entry is not null){history!.Relocate(entry,output);RefreshHistory();}
        timingDirty=false;ApplyTimingButton.IsEnabled=false;
        TimingInfo.Text="Timing saved to the latest native animation and metadata. Artwork and PNG pixels were not changed.";
        Status.Text=$"Timing saved · {fps} FPS · {playback!.TotalDurationMs/1000.0:0.##} s · Blender was not started.";
        return true;
    }
    private void ApplyTiming(object sender,RoutedEventArgs e)=>Try(()=>EnsureTimingApplied());
    private void OpenGeneratedAsset(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var dialog=new OpenFileDialog{Title="Open a generated animation without rendering",Filter="Generated asset|asset.output.json;asset.settings.json;asset.sheet.json;*.aseprite|JSON files|*.json"};
        Prepare(dialog,"assets");if(dialog.ShowDialog()!=true)return;
        var selected=GeneratedAsset.Open(dialog.FileName);RememberFile("assets",dialog.FileName);var settings=AssetSettings.Load(Path.Combine(selected.Directory,"asset.settings.json"));
        directionalBatch=[];UpdateDirectionalControls();
        CloseComparison();ApplySettings(settings);output=selected;LoadPlayback(output,settings.Fps);EmptyPreview.Visibility=Visibility.Collapsed;ExportPanel.IsEnabled=true;
        OutputInfo.Text=$"{settings.Width} × {settings.Height} · {output.Frames.Length} existing frame(s) · no rendering";
        Status.Text="Generated asset reopened with saved playback timing. Blender was not started.";
    });
}
