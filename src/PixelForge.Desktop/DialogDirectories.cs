using System.IO;

namespace PixelForge.Desktop;
public sealed class DialogDirectories
{
    public Dictionary<string,string> Locations {get;set;}=[];
    public string Initial(string category,string? fallback=null)
    {
        if(Locations.TryGetValue(category,out var path)&&Directory.Exists(path))return path;
        if(!string.IsNullOrEmpty(fallback)&&Directory.Exists(fallback))return fallback;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
    public void SelectedFile(string category,string file)=>SelectedFolder(category,Path.GetDirectoryName(Path.GetFullPath(file))!);
    public void SelectedFolder(string category,string folder){if(Directory.Exists(folder))Locations[category]=Path.GetFullPath(folder);}
}
