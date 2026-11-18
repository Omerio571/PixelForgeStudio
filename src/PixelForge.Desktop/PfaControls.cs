using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.Desktop;

public partial class MainWindow
{
    private AssetExportProfile[] pfaProfiles=[];
    private string activePfaProfile="";
    private string[] recentPfa=[];

    private void OpenPfaExport(bool profilesOnly)
    {
        Try(()=>{
            if(!profilesOnly&&(output is null||!EnsureTimingApplied()))
            {Status.Text="Generate or reopen an asset before exporting PFA.";return;}
            var current=!profilesOnly&&output is not null?GeneratedAsset.Open(output.Directory):null;
            var batch=current is not null?directionalBatch.Select(item=>GeneratedAsset.Open(item.Directory)).ToArray():[];
            var dialog=CreatePfaExporter(current,batch);
            dialog.ShowDialog();
        });
    }
    private Window CreatePfaExporter(ConversionOutput? current,ConversionOutput[] batch)=>
        new PfaExportWindow(pfaProfiles,activePfaProfile,(profiles,active)=>{
            pfaProfiles=profiles;activePfaProfile=active;SavePreferences();
        },current,batch,path=>session?.IsManaged(path)==true){Owner=this};

    private void ChoosePfa(bool folder)
    {
        if(folder)
        {
            var dialog=new OpenFolderDialog{Title="Open PFA direction or asset folder",InitialDirectory=directories.Initial("pfa")};
            if(dialog.ShowDialog(this)==true)ShowPfa(dialog.FolderName);
        }
        else
        {
            var dialog=new OpenFileDialog{Filter="PixelForge image layer|*.pfa",InitialDirectory=directories.Initial("pfa")};
            if(dialog.ShowDialog(this)==true)ShowPfa(dialog.FileName);
        }
    }
    private void ShowPfa(string path)=>Try(()=>{
        var viewer=new PfaViewerWindow(path){Owner=this};viewer.Show();
        recentPfa=[path,..recentPfa.Where(p=>!p.Equals(path,StringComparison.OrdinalIgnoreCase)).Take(7)];
        directories.SelectedFolder("pfa",Directory.Exists(path)?path:Path.GetDirectoryName(path)!);
        SavePreferences();
    });
    private void AddPfaTools(ContextMenu menu)
    {
        void Add(string title,Action execute)
        {var item=new MenuItem{Header=title};item.Click+=(_,_)=>execute();menu.Items.Add(item);}
        Add("Export Asset (.pfa)…",()=>OpenPfaExport(false));
        Add("Open .pfa…",()=>ChoosePfa(false));
        Add("Open PFA folder…",()=>ChoosePfa(true));
        Add("Manage Export Profiles…",()=>OpenPfaExport(true));
        if(recentPfa.Length>0)
        {
            var recent=new MenuItem{Header="Recent PFA assets"};menu.Items.Add(recent);
            foreach(string path in recentPfa)
            {
                var entry=new MenuItem{Header=Path.GetFileName(path),ToolTip=path};entry.Click+=(_,_)=>ShowPfa(path);recent.Items.Add(entry);
            }
        }
        menu.Items.Add(new Separator());
    }
}
