using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;
using PixelForge.Core.IndexedLab;

namespace PixelForge.Desktop;

public partial class IndexedLabWindow : Window
{
    private sealed record Preferences(string Root="",string Reference="",string Encoder="",string Decoder="",string Alpha="128",
        EncodingMethod Method=EncodingMethod.Auto,string Directory="",int Zoom=2);
    private readonly string settings;
    private readonly Rgb[] workingPalette;
    private string root="",reference="",directory="";
    private string[] sources=[];
    private LabBatch? batch;
    private LabSample? sample;
    private Raster? reconstructed;
    private byte[]? samplePaletteHash;
    private CancellationTokenSource? operation;

    public IndexedLabWindow(string profile,Rgb[] colors)
    {
        InitializeComponent();
        settings=Path.Combine(profile,"indexed-lab.json");
        workingPalette=colors.ToArray();
        Preferences saved=new();
        try{if(File.Exists(settings))saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settings))??saved;}
        catch(Exception error) when(error is IOException or JsonException){Status.Text="Lab preferences could not be opened: "+error.Message;}
        root=saved.Root;reference=saved.Reference;directory=saved.Directory;
        Encoder.Text=string.IsNullOrEmpty(saved.Encoder)?FindTool("cjxl"):saved.Encoder;
        Decoder.Text=string.IsNullOrEmpty(saved.Decoder)?FindTool("djxl"):saved.Decoder;
        Alpha.Text=saved.Alpha;Method.ItemsSource=Enum.GetValues<EncodingMethod>();Method.SelectedItem=saved.Method;
        Zoom.ItemsSource=new[]{1,2,4,8};Zoom.SelectedItem=saved.Zoom;
        Refresh();
        Closing+=(_,e)=>{if(operation is not null){e.Cancel=true;operation.Cancel();}else Remember();};
    }

    private static string FindTool(string name)
    {
        foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            var file=Path.Combine(folder.Trim('"'),name+".exe");
            if(File.Exists(file)&&Path.IsPathFullyQualified(file))return file;
        }
        return "";
    }

    private void Remember()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings,JsonSerializer.Serialize(new Preferences(root,reference,Encoder.Text,Decoder.Text,Alpha.Text,
                Method.SelectedItem is EncodingMethod method?method:EncodingMethod.Auto,directory,Zoom.SelectedItem is int zoom?zoom:2)));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){Status.Text="Could not save lab preferences: "+e.Message;}
    }

    private void Refresh()
    {
        SelectionInfo.Text=$"Assets: {(root.Length==0?"choose a folder":root)}\nPalette: {(reference.Length==0?"choose or create one":reference)} · {sources.Length} source image(s)";
        RunButton.IsEnabled=root.Length>0&&reference.Length>0&&sources.Length>0&&operation is null;
        SaveButton.IsEnabled=sample is not null&&samplePaletteHash is not null&&operation is null;
        OpenButton.IsEnabled=RebindButton.IsEnabled=root.Length>0&&operation is null;
        ExportButton.IsEnabled=reconstructed is not null&&operation is null;
        CsvButton.IsEnabled=batch is not null&&operation is null;
        CancelButton.IsEnabled=operation is not null;
    }

    private void Guard(Action action)
    {
        if(operation is not null)return;
        try{action();Refresh();}
        catch(Exception e){Status.Text=e.Message;}
    }

    private async Task Io(Func<Task> action)
    {
        if(operation is not null)return;
        operation=new();Refresh();CancelButton.IsEnabled=false;
        try{await action();}
        catch(Exception error){Status.Text=error.Message;}
        finally{operation.Dispose();operation=null;Refresh();Remember();}
    }

    private string Root()=>root.Length>0?root:throw new InvalidOperationException("Choose an Assets root first.");
    private OpenFileDialog OpenDialog(string filter)=>new(){Filter=filter,InitialDirectory=Directory.Exists(directory)?directory:""};
    private SaveFileDialog SaveDialog(string filter,string name)=>new(){Filter=filter,FileName=name,InitialDirectory=Directory.Exists(directory)?directory:"",OverwritePrompt=false};

    private void Invalidate()
    {
        sample=null;samplePaletteHash=null;batch=null;reconstructed=null;
        Original.Source=Converted.Source=Reconstructed.Source=null;Results.Clear();
    }

    private void ChooseRoot(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var dialog=new OpenFolderDialog{Title="Select Assets root",InitialDirectory=root};
        if(dialog.ShowDialog(this)!=true)return;
        root=dialog.FolderName;reference="";Invalidate();Remember();
    });
    private static readonly string[] Extensions=[".png",".bmp",".jpg",".jpeg",".tif",".tiff"];
    private async void ChooseImages(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        var dialog=OpenDialog("Images|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff");dialog.Multiselect=true;
        if(dialog.ShowDialog(this)!=true)return;
        sources=dialog.FileNames;directory=Path.GetDirectoryName(sources[0])!;Invalidate();
        var source=await Task.Run(()=>IndexedBenchmark.Load(sources[0]));
        Original.Source=Bitmap(source);ZoomChanged(this,null!);
    });
    private async void ChooseFolder(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        var dialog=new OpenFolderDialog{Title="Choose a folder of images",InitialDirectory=directory};
        if(dialog.ShowDialog(this)!=true)return;
        directory=dialog.FolderName;
        sources=Directory.EnumerateFiles(directory).Where(p=>Extensions.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)).Order().ToArray();
        Invalidate();
        if(sources.Length>0)
        {
            var source=await Task.Run(()=>IndexedBenchmark.Load(sources[0]));
            Original.Source=Bitmap(source);ZoomChanged(this,null!);
        }
    });
    private void ChoosePalette(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var dialog=OpenDialog("Shared palette|*.pfpal");dialog.InitialDirectory=Path.Combine(Root(),"Palettes");
        if(dialog.ShowDialog(this)!=true)return;
        var candidate=Path.GetRelativePath(Root(),dialog.FileName).Replace('\\','/');
        SharedPalette.Read(IndexedCodec.PalettePath(root,candidate));
        reference=candidate;Invalidate();Remember();
    });

    private void CreatePalette(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        string assets=Root();
        var paletteSource=OpenDialog("Palette text, JSON or GPL|*.txt;*.json;*.gpl;*.pal");
        string examples=Path.Combine(AppContext.BaseDirectory,"examples");
        if(Directory.Exists(examples))paletteSource.InitialDirectory=examples;
        var selection=workingPalette.Length is 31 or 32
            ?MessageBox.Show(this,$"Use the {workingPalette.Length}-color working palette? Choose No to import a 31- or 32-color palette file.",
                "Create shared palette",MessageBoxButton.YesNoCancel)
            :MessageBoxResult.No;
        if(selection==MessageBoxResult.Cancel)return;
        Rgb[] colors;
        if(selection==MessageBoxResult.Yes)colors=workingPalette;
        else
        {
            if(paletteSource.ShowDialog(this)!=true)return;
            colors=Path.GetExtension(paletteSource.FileName).ToLowerInvariant() is ".gpl" or ".pal"
                ?Palette.Load(paletteSource.FileName):Palette.ParseList(File.ReadAllText(paletteSource.FileName));
        }
        if(colors.Length==32)
        {
            var source=sources.Length>0?IndexedBenchmark.Load(sources[0]):null;
            var removal=new PaletteRemovalWindow(colors,source,byte.TryParse(Alpha.Text,out byte alpha)&&alpha>0?alpha:(byte)128){Owner=this};
            if(removal.ShowDialog()!=true)return;
            colors=colors.Where((_,i)=>i!=removal.Removed).ToArray();
        }
        var palette=SharedPalette.Create(colors);
        string folder=Path.Combine(assets,"Palettes");Directory.CreateDirectory(folder);
        var dialog=SaveDialog("Shared palette|*.pfpal","experimental.pfpal");dialog.InitialDirectory=folder;
        if(dialog.ShowDialog(this)!=true)return;
        string candidate=Path.GetRelativePath(assets,dialog.FileName).Replace('\\','/');
        using(var stream=new FileStream(IndexedCodec.PalettePath(assets,candidate),FileMode.CreateNew))stream.Write(palette.Serialize());
        reference=candidate;Invalidate();Remember();Status.Text="Saved a separate 31-color shared palette with transparent index 0.";
    });
    private void ExportPalette(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var palette=SharedPalette.Read(IndexedCodec.PalettePath(Root(),reference));
        var dialog=SaveDialog("HEX text|*.txt","palette.txt");
        if(dialog.ShowDialog(this)!=true)return;
        using var writer=new StreamWriter(new FileStream(dialog.FileName,FileMode.CreateNew));
        foreach(var color in palette.Colors)writer.WriteLine(color.Hex);
    });
    private void ChooseTool(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var dialog=OpenDialog("Executable|*.exe");
        if(dialog.ShowDialog(this)!=true)return;
        (Equals(((Button)sender).Tag,"encoder")?Encoder:Decoder).Text=dialog.FileName;Remember();
    });

    private async void RunComparison(object sender,RoutedEventArgs e)
    {
        if(operation is not null)return;
        if(!byte.TryParse(Alpha.Text,out byte threshold)||threshold==0){Status.Text="Enter an alpha threshold from 1 to 255.";return;}
        var assets=Root();var paletteReference=reference;var input=sources.ToArray();
        var encoder=Encoder.Text.Trim();var decoder=Decoder.Text.Trim();
        if((encoder.Length>0&&!Path.IsPathFullyQualified(encoder))||(decoder.Length>0&&!Path.IsPathFullyQualified(decoder)))
        {Status.Text="Select full executable paths for the optional JPEG XL tools.";return;}
        Invalidate();operation=new();Refresh();
        try
        {
            var fingerprint=SHA256.HashData(SharedPalette.Read(IndexedCodec.PalettePath(assets,paletteReference)).Serialize());
            var progress=new Progress<string>(message=>Status.Text=message);
            batch=await Task.Run(()=>IndexedBenchmark.Run(assets,paletteReference,input,threshold,encoder,decoder,progress,operation.Token));
            sample=batch.First;
            if(sample is not null)
            {
                samplePaletteHash=fingerprint;reconstructed=sample.Decoded;Display();
            }
            Results.Text=FormatBatch(batch);
            Status.Text=batch.Rows.SelectMany(r=>r.Measurements).Any(m=>m.Verification!="PASS")?"Comparison completed with failures. See Results.":$"Preview: {Path.GetFileName(input[0])}. Results includes all {input.Length} image(s) and shared palette overhead.";
        }
        catch(OperationCanceledException){Status.Text="Comparison canceled.";}
        catch(Exception error){Status.Text=error.Message;}
        finally{operation.Dispose();operation=null;Refresh();Remember();}
    }
    private void CancelRun(object sender,RoutedEventArgs e)=>operation?.Cancel();

    private async void SaveImage(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        if(sample is null)return;
        var sha=SHA256.HashData(SharedPalette.Read(IndexedCodec.PalettePath(Root(),reference)).Serialize());
        if(samplePaletteHash is null||!sha.SequenceEqual(samplePaletteHash))throw new InvalidDataException("Palette changed. Run comparison again before saving.");
        var dialog=SaveDialog("Indexed image|*.pfi",sources.Length>0?Path.GetFileNameWithoutExtension(sources[0])+".pfi":"image.pfi");
        if(dialog.ShowDialog(this)!=true)return;
        var method=(EncodingMethod)Method.SelectedItem;
        var result=await Task.Run(()=>{
            var encoded=IndexedCodec.Write(dialog.FileName,root,reference,sample.Indices,method);
            var decoded=IndexedCodec.Read(dialog.FileName,root);
            return (encoded.Method,Raster:decoded.Image.ToRaster(decoded.Palette));
        });
        directory=Path.GetDirectoryName(dialog.FileName)!;
        reconstructed=result.Raster;Display();
        Status.Text=$"Lossless indexed round-trip: {IndexedBenchmark.Verify(sample.Converted,reconstructed)} · {result.Method} · {new FileInfo(dialog.FileName).Length:N0} bytes";
    });
    private async void OpenImage(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        var dialog=OpenDialog("Indexed image|*.pfi");dialog.InitialDirectory=Root();
        if(dialog.ShowDialog(this)!=true)return;
        var decoded=await Task.Run(()=>{var read=IndexedCodec.Read(dialog.FileName,root);return read.Image.ToRaster(read.Palette);});
        directory=Path.GetDirectoryName(dialog.FileName)!;
        Invalidate();reconstructed=decoded;Display();
        Status.Text=$"Decoded {decoded.Width} × {decoded.Height}. Palette and pixel checksum verified.";
    });
    private async void RebindImage(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        var dialog=OpenDialog("Indexed image|*.pfi");dialog.InitialDirectory=Root();
        if(dialog.ShowDialog(this)!=true)return;
        if(MessageBox.Show(this,"Save these indices using the currently selected shared palette? Colors can change. The original file is preserved.","Rebind indexed image",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var save=SaveDialog("Indexed image|*.pfi","rebound.pfi");
        if(save.ShowDialog(this)!=true)return;
        var method=(EncodingMethod)Method.SelectedItem;
        var rebound=await Task.Run(()=>{
            var decoded=IndexedCodec.Read(dialog.FileName,root,reference);
            IndexedCodec.Write(save.FileName,root,reference,decoded.Image,method);
            var bound=IndexedCodec.Read(save.FileName,root);
            return bound.Image.ToRaster(bound.Palette);
        });
        directory=Path.GetDirectoryName(save.FileName)!;
        Invalidate();reconstructed=rebound;Display();Status.Text="Saved a new image with the selected palette.";
    });
    private async void ExportPng(object sender,RoutedEventArgs e)=>await Io(async ()=>
    {
        var dialog=SaveDialog("PNG image|*.png","reconstructed.png");
        if(reconstructed is not {} image||dialog.ShowDialog(this)!=true)return;
        await Task.Run(()=>image.Save(dialog.FileName));
        directory=Path.GetDirectoryName(dialog.FileName)!;
        Status.Text="Saved the reconstructed PNG.";
    });

    private static string FormatBatch(LabBatch batch)
    {
        var rows=batch.Rows;
        var lines=new List<string>{ $"{rows.Count} image(s) · original total {rows.Sum(r=>r.SourceBytes):N0} bytes",batch.JxlStatus,""};
        foreach(var group in rows.SelectMany(r=>r.Measurements).GroupBy(m=>m.Format))
        {
            if(group.Count()!=rows.Count||group.Any(m=>m.Verification!="PASS")){lines.Add($"{group.Key}: no verified collection total");continue;}
            bool pfi=group.Key is "Packed" or "Runs" or "Brotli";
            long sum=group.Sum(m=>m.Bytes);
            lines.Add($"{group.Key}: {sum:N0} bytes"+(pfi?$" + shared palette {SharedPalette.Size} = {sum+SharedPalette.Size:N0} bytes":""));
        }
        var comparisons=rows.Select(r=>r.Measurements.Where(m=>m.PayloadBytes.HasValue&&m.Verification=="PASS").MinBy(m=>m.Bytes)).ToArray();
        if(comparisons.Length>0&&comparisons.All(m=>m is not null))
        {
            long best=comparisons.Sum(m=>m!.Bytes)+SharedPalette.Size;
            lines.Add($"Auto PFI collection: {best:N0} bytes including one palette.");
            foreach(string baseline in new[]{"RGBA PNG","Indexed PNG","JPEG XL"})
            {
                var data=rows.SelectMany(r=>r.Measurements).Where(m=>m.Format==baseline&&m.Verification=="PASS").ToArray();
                if(data.Length==rows.Count)
                {
                    long total=data.Sum(m=>m.Bytes),saved=total-best;
                    lines.Add($"Against {baseline}: {saved:N0} bytes saved ({100.0*saved/total:F1}%, {saved/(double)rows.Count:N1} bytes/image).");
                }
            }
        }
        foreach(var row in rows)
        {
            lines.Add("");lines.Add($"{Path.GetFileName(row.Source)} · {row.Width}×{row.Height} · {row.Colors} opaque colors · transparency {row.Transparent} · original {row.SourceBytes:N0} bytes");
            lines.Add($"Conversion changed {row.ChangedPixels:N0} RGBA pixels ({row.PartialAlpha:N0} partial-alpha input pixels).");
            lines.Add("Lossless indexed round-trip: "+(row.Measurements.Where(m=>m.PayloadBytes.HasValue).All(m=>m.Verification=="PASS")?"PASS":"FAIL"));
            foreach(var m in row.Measurements)
                lines.Add($"  {m.Format}: {m.Bytes:N0} bytes"+(m.PayloadBytes is int pay?$" ({pay:N0} payload, {m.Bytes-pay:N0} header)":"")+
                          $" · {m.EncodeMs:F1}/{m.DecodeMs:F1} ms encode/decode · {m.Verification}");
        }
        return string.Join(Environment.NewLine,lines);
    }

    private void ExportCsv(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(batch is null)return;
        var dialog=SaveDialog("CSV file|*.csv","indexed-lab.csv");
        if(dialog.ShowDialog(this)!=true)return;
        using var writer=new StreamWriter(new FileStream(dialog.FileName,FileMode.CreateNew));
        static string Quote(string value)=>"\""+value.Replace("\"","\u0022\"")+"\"";
        writer.WriteLine("source,width,height,colors,transparent,partial_alpha,changed_pixels,source_bytes,format,file_bytes,payload_bytes,encode_ms,decode_ms,verification");
        foreach(var row in batch.Rows)foreach(var m in row.Measurements)
            writer.WriteLine(FormattableString.Invariant($"{Quote(row.Source)},{row.Width},{row.Height},{row.Colors},{row.Transparent},{row.PartialAlpha},{row.ChangedPixels},{row.SourceBytes},{m.Format},{m.Bytes},{m.PayloadBytes},{m.EncodeMs:F2},{m.DecodeMs:F2},{Quote(m.Verification)}"));
    });

    internal static BitmapSource Bitmap(Raster raster)
    {
        var bitmap=BitmapSource.Create(raster.Width,raster.Height,96,96,PixelFormats.Bgra32,null,raster.Pixels,raster.Width*4);
        bitmap.Freeze();return bitmap;
    }
    private void Display()
    {
        Original.Source=sample is null?null:Bitmap(sample.Source);
        Converted.Source=sample is null?null:Bitmap(sample.Converted);
        Reconstructed.Source=reconstructed is null?null:Bitmap(reconstructed);
        ZoomChanged(this,null!);
    }
    private void ZoomChanged(object sender,SelectionChangedEventArgs e)
    {
        if(Original is null||Zoom.SelectedItem is not int zoom)return;
        foreach(var image in new[]{Original,Converted,Reconstructed})image.LayoutTransform=new ScaleTransform(zoom,zoom);
    }
}

public partial class MainWindow
{
    private void ShowLabTools(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;
        var menu=new ContextMenu{PlacementTarget=button};AddPfaTools(menu);
        var lab=new MenuItem{Header="Indexed Image Lab…"};
        lab.Click+=(_,_)=>new IndexedLabWindow(ProfileDirectory,selectedPalette){Owner=this}.ShowDialog();
        menu.Items.Add(lab);button.ContextMenu=menu;menu.IsOpen=true;
    }
}
