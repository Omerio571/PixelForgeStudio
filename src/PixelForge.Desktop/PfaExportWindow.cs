using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

internal sealed class PfaExportWindow : Window
{
    private readonly List<AssetExportProfile> saved;
    private readonly Action<AssetExportProfile[],string> persist;
    private readonly Func<string,bool> isTemporary;
    private readonly ConversionOutput? render;
    private readonly ConversionOutput[] directions;
    private readonly ComboBox profiles=new(),compression=new();
    private readonly ComboBox color=new(){IsEditable=true},normal=new(){IsEditable=true},emission=new(){IsEditable=true};
    private readonly CheckBox pfaFormat=new(){Content="PFA"},pngFormat=new(){Content="PNG atlas"},
        webpFormat=new(){Content="WebP Lossless (No Metadata)"},reports=new(){Content="Include detailed compression comparison"};
    private readonly TextBox cwebp=new(),dwebp=new(),cjxl=new(),djxl=new();
    private readonly TextBox basePng=new(),normalPng=new(),emissionPng=new();
    private readonly TextBox profileName=new(),root=new(),category=new(),asset=new();
    private readonly CheckBox objects=new(){Content="Base color"},normals=new(){Content="Normal"},emissions=new(){Content="Emission"},
        all=new(){Content="All generated directions"},png=new(){Content="Include PNG previews"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel body=new(){Margin=new Thickness(16)};
    private readonly Button cancel=new(){Content="Cancel export",IsEnabled=false};
    private CancellationTokenSource? operation;

    public PfaExportWindow(AssetExportProfile[] profilesSaved,string active,Action<AssetExportProfile[],string> save,ConversionOutput? source,ConversionOutput[] batch,Func<string,bool> temporary)
    {
        saved=profilesSaved.ToList();persist=save;isTemporary=temporary;render=source;directions=batch;
        Title="Export Asset · Export All";Width=650;Height=730;MinWidth=470;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Window");SetResourceReference(ForegroundProperty,"Text");
        Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        body.Children.Add(new TextBlock{Text="Export Asset",FontSize=21,FontWeight=FontWeights.SemiBold});
        Field("Saved profile",profiles);
        Actions(("New",()=>Fill(new())),("Duplicate",()=>profileName.Text+=" Copy"),("Delete",Delete));
        Field("Profile name",profileName);Field("Assets root",root);Actions(("Choose root…",ChooseRoot));
        Field("Category",category);Field("Asset name",asset);
        var layers=new WrapPanel();foreach(var check in new[]{objects,normals,emissions,all}){check.Margin=new Thickness(0,6,12,6);layers.Children.Add(check);}
        body.Children.Add(layers);
        body.Children.Add(new TextBlock{Text="Export Formats",FontWeight=FontWeights.SemiBold});
        var formats=new WrapPanel();body.Children.Add(formats);
        foreach(var check in new[]{pfaFormat,pngFormat,webpFormat}){check.Margin=new Thickness(0,3,12,3);formats.Children.Add(check);}
        body.Children.Add(new TextBlock{Text=source is null?"Save or manage profiles here. Generate or reopen a render to export pixels.":
            $"Current render has base color{(source.NormalSheet is null?"":", normals")}{(source.EmissionSheet is null?"":", emission")} · {source.Frames.Length} frame(s). Uncheck any unavailable layer for a base-only export.",TextWrapping=TextWrapping.Wrap});
        var advanced=new StackPanel();
        var expander=new Expander{Header="Palettes and encoding",Content=advanced};body.Children.Add(expander);
        Field("Base GPL (32 rows, index 0 transparent)",color,advanced);
        Field("Normal GPL (optional exact match)",normal,advanced);
        Field("Emission GPL (optional exact match)",emission,advanced);
        foreach(var palette in new[]{color,normal,emission})
            palette.SetBinding(ToolTipProperty,new System.Windows.Data.Binding(nameof(ComboBox.Text)){Source=palette});
        var imports=new WrapPanel();advanced.Children.Add(imports);
        foreach(var (label,input) in new[]{("Import base GPL…",color),("Import normal GPL…",normal),("Import emission GPL…",emission)})
        {
            var button=new Button{Content=label};button.Click+=(_,_)=>Try(()=>ImportPalette(input));imports.Children.Add(button);
        }
        Field("Compression (Auto = smallest tested lossless)",compression,advanced);advanced.Children.Add(png);
        foreach(var (label,input) in new[]{("External base PNG (optional)",basePng),("External normal PNG (optional)",normalPng),("External emission PNG (optional)",emissionPng)})
        {
            Field(label,input,advanced);
            var browse=new Button{Content="Browse PNG…",HorizontalAlignment=HorizontalAlignment.Left};
            browse.Click+=(_,_)=>Try(()=>{
                var dialog=new OpenFileDialog{Filter="PNG|*.png"};
                if(dialog.ShowDialog(this)==true)input.Text=dialog.FileName;
            });
            advanced.Children.Add(browse);
        }
        advanced.Children.Add(reports);
        foreach(var (label,input) in new[]{("cwebp executable (optional if on PATH)",cwebp),("dwebp executable (optional if on PATH)",dwebp),
            ("cjxl executable (comparison only)",cjxl),("djxl executable (comparison only)",djxl)})
        {
            Field(label,input,advanced);
            var browse=new Button{Content="Browse executable…",HorizontalAlignment=HorizontalAlignment.Left};
            browse.Click+=(_,_)=>Try(()=>{
                var picker=new OpenFileDialog{Filter="Executable|*.exe"};
                if(picker.ShowDialog(this)==true)input.Text=picker.FileName;
            });
            advanced.Children.Add(browse);
        }
        compression.ItemsSource=Enum.GetValues<PfaCompression>();
        Actions(("Save Changes to Profile",Save),("Rename saved profile",Rename));
        if(source is not null)
        {
            var button=new Button{Content="Export",IsDefault=true};
            button.Click+=Export;body.Children.Add(button);
            asset.Text=Path.GetFileNameWithoutExtension(AssetSettings.Load(Path.Combine(source.Directory,"asset.settings.json")).Model);
        }
        cancel.Click+=(_,_)=>operation?.Cancel();body.Children.Add(cancel);body.Children.Add(status);
        profiles.SelectionChanged+=(_,_)=>{
            if(profiles.SelectedItem is not string name||saved.FirstOrDefault(p=>p.Name==name) is not {} profile)return;
            Fill(profile);persist(saved.ToArray(),name);
        };
        Refresh(active);
        if(profiles.SelectedItem is null)Fill(new());
        Closing+=(_,e)=>{if(operation is not null){e.Cancel=true;operation.Cancel();}};
    }
    private void Field(string label,Control control,StackPanel? panel=null)
    {panel??=body;panel.Children.Add(new TextBlock{Text=label});panel.Children.Add(control);}
    private void Actions(params (string Label,Action Run)[] items)
    {
        var row=new WrapPanel();body.Children.Add(row);
        foreach(var item in items)
        {
            var button=new Button{Content=item.Label};button.Click+=(_,_)=>Try(item.Run);row.Children.Add(button);
        }
    }
    private void Try(Action action){if(operation is not null)return;try{action();}catch(Exception e){status.Text=e.Message;}}
    private void Refresh(string selected)
    {
        profiles.ItemsSource=saved.Select(p=>p.Name).ToArray();
        profiles.SelectedItem=selected;
    }
    private void Fill(AssetExportProfile p)
    {
        profileName.Text=p.Name;root.Text=p.Root;category.Text=p.Category;
        objects.IsChecked=p.Base;normals.IsChecked=p.Normal;emissions.IsChecked=p.Emission;all.IsChecked=p.AllDirections;
        pfaFormat.IsChecked=p.ExportPfa;pngFormat.IsChecked=p.ExportPng;webpFormat.IsChecked=p.ExportWebp;reports.IsChecked=p.DetailedReport;
        cwebp.Text=p.Cwebp;dwebp.Text=p.Dwebp;cjxl.Text=p.Cjxl;djxl.Text=p.Djxl;
        png.IsChecked=p.PngPreviews;compression.SelectedItem=p.Compression;
        basePng.Text=p.BasePng;normalPng.Text=p.NormalPng;emissionPng.Text=p.EmissionPng;
        Discover();color.Text=p.BasePalette;normal.Text=p.NormalPalette;emission.Text=p.EmissionPalette;
    }
    private AssetExportProfile Draft()=>new(profileName.Text.Trim(),root.Text.Trim(),category.Text.Trim(),color.Text.Trim(),normal.Text.Trim(),
        emission.Text.Trim(),objects.IsChecked==true,normals.IsChecked==true,emissions.IsChecked==true,all.IsChecked==true,
        (PfaCompression)compression.SelectedItem,png.IsChecked==true,basePng.Text.Trim(),normalPng.Text.Trim(),emissionPng.Text.Trim(),
        pfaFormat.IsChecked==true,pngFormat.IsChecked==true,webpFormat.IsChecked==true,cwebp.Text.Trim(),dwebp.Text.Trim(),
        cjxl.Text.Trim(),djxl.Text.Trim(),reports.IsChecked==true);
    private void Save()=>Store(false);
    private void Rename()=>Store(true);
    private void Store(bool rename)
    {
        var p=Draft();
        if(string.IsNullOrWhiteSpace(p.Name)||!Directory.Exists(p.Root)||!Path.IsPathFullyQualified(p.Root))
            throw new InvalidDataException("Name the profile and select an existing Assets root.");
        Pfa.Segment(p.Category);
        foreach(var reference in new[]{p.BasePalette,p.NormalPalette,p.EmissionPalette}.Where(x=>x.Length>0))
            if(!File.Exists(Pfa.PalettePath(p.Root,reference)))throw new FileNotFoundException("Required GPL palette: "+reference);
        int index=saved.FindIndex(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
        if(rename&&profiles.SelectedItem is string old)
        {
            if(index>=0&&saved[index].Name!=old)throw new InvalidDataException("A profile already has that name.");
            index=saved.FindIndex(x=>x.Name==old);
        }
        if(index<0)saved.Add(p);else saved[index]=p;
        Refresh(p.Name);persist(saved.ToArray(),p.Name);status.Text="Saved profile: "+p.Name;
    }
    private void Delete()
    {
        if(profiles.SelectedItem is not string name||
           MessageBox.Show(this,"Delete the saved profile "+name+"? Exported assets stay on disk.","Delete profile",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        saved.RemoveAll(p=>p.Name==name);
        string next=saved.FirstOrDefault()?.Name??"";Refresh(next);persist(saved.ToArray(),next);
        if(next.Length==0)Fill(new());
    }
    private void ChooseRoot()
    {
        var dialog=new OpenFolderDialog{InitialDirectory=root.Text,Title="Choose Assets root"};
        if(dialog.ShowDialog(this)!=true)return;
        root.Text=dialog.FolderName;color.Text=normal.Text=emission.Text="";Discover();
    }
    private void ImportPalette(ComboBox target)
    {
        if(!Directory.Exists(root.Text)||!Path.IsPathFullyQualified(root.Text))
            throw new InvalidDataException("Choose the Assets root first.");
        var picker=new OpenFileDialog{Filter="GIMP palette|*.gpl",InitialDirectory=root.Text};
        if(picker.ShowDialog(this)!=true)return;
        string imported=picker.FileName;
        var colors=Palette.Load(imported);
        string relative=Path.GetRelativePath(root.Text,imported).Replace('\\','/');
        string? existing=null;
        try
        {
            if(Path.GetFullPath(Pfa.PalettePath(root.Text,relative)).Equals(Path.GetFullPath(imported),StringComparison.OrdinalIgnoreCase))
                existing=relative;
        }
        catch(InvalidDataException){}
        if(existing is null)
        {
            var library=Path.Combine(root.Text,"Palettes");
            var choices=Directory.EnumerateFiles(root.Text,"*.gpl").Concat(Directory.Exists(library)?Directory.EnumerateFiles(library,"*.gpl"):[]);
            foreach(var candidate in choices)
            {
                var reference=Path.GetRelativePath(root.Text,candidate).Replace('\\','/');
                try
                {
                    if(Palette.Load(Pfa.PalettePath(root.Text,reference)).SequenceEqual(colors)){existing=reference;break;}
                }
                catch(InvalidDataException){}
            }
            if(existing is null)
            {
                if(MessageBox.Show(this,"Copy this GPL into the Assets palette library?","Shared palette",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                var leaf=Pfa.Segment(Path.GetFileName(imported));
                existing="Palettes/"+leaf;
                string destination=Pfa.PalettePath(root.Text,existing);
                for(int n=2;File.Exists(destination);n++)
                {
                    existing="Palettes/"+Path.GetFileNameWithoutExtension(leaf)+"-"+n+".gpl";
                    destination=Pfa.PalettePath(root.Text,existing);
                }
                Directory.CreateDirectory(library);File.Copy(imported,destination);
            }
        }
        var current=new[]{color.Text,normal.Text,emission.Text};Discover();
        color.Text=current[0];normal.Text=current[1];emission.Text=current[2];target.Text=existing;
    }

    private void Discover()
    {
        var names=new List<string>{""};
        if(Directory.Exists(root.Text))
        {
            IEnumerable<string> Paths(string folder)=>Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.gpl"):[];
            names.AddRange(Paths(root.Text).Concat(Paths(Path.Combine(root.Text,"Palettes")))
                .Select(path=>Path.GetRelativePath(root.Text,path).Replace('\\','/')).Order());
        }
        foreach(var palette in new[]{color,normal,emission})
        {
            string selection=palette.Text;
            palette.ItemsSource=names;
            palette.Text=selection;
        }
    }
    private async void Export(object sender,RoutedEventArgs e)
    {
        if(operation is not null||render is null)return;
        var button=(Button)sender;button.IsEnabled=false;cancel.IsEnabled=true;operation=new();
        try
        {
            var draft=Draft();
            if(Path.IsPathFullyQualified(draft.Root)&&isTemporary(draft.Root))throw new InvalidDataException("Choose a permanent Assets root outside the temporary render session.");
            string name=asset.Text.Trim();
            var selected=draft.AllDirections&&directions.Length>0?directions:[render];
            status.Text="Exporting and verifying each layer… Existing assets get a new version folder.";
            var result=await Task.Run(()=>PfaExport.Save(draft,name,selected,operation.Token));
            status.Text=$"PASS · {result.Directions} direction(s), {result.Layers} files, {result.Bytes:N0} exported bytes\n{result.Directory}";
            Actions(("Open Folder",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.Directory){UseShellExecute=true})));
            if(draft.ExportPfa)Actions(("Open in Asset Viewer",()=>new PfaViewerWindow(result.Directory,draft.Root){Owner=this}.Show()));
            if(result.Report is {} report)
                Actions(("View compression report",()=>new Window{Owner=this,Title="Compression comparison",Width=850,Height=650,
                    Content=new TextBox{Text=File.ReadAllText(report),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}}.Show()));
        }
        catch(OperationCanceledException){status.Text="Export canceled.";}
        catch(Exception ex){status.Text=ex.Message;}
        finally{operation.Dispose();operation=null;button.IsEnabled=true;cancel.IsEnabled=false;}
    }
}
