using System.IO;

namespace PixelForge.Desktop;
public sealed class DialogDirectories
{
    public Dictionary<string,string> Locations {get;set;}=[];
    public string Initial(string category,string? fallback=null)
    {
        if(Locations.TryGetValue(category,out var path)&&Accessible(path) is { } remembered)return remembered;
        return Accessible(fallback)??Accessible(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
            ??Accessible(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))??AppContext.BaseDirectory;
    }
    public void SelectedFile(string category,string file)=>SelectedFolder(category,Path.GetDirectoryName(Path.GetFullPath(file))!);
    public void SelectedFolder(string category,string folder){if(Accessible(folder) is { } path)Locations[category]=path;}
    private static string? Accessible(string? directory)
    {
        if(string.IsNullOrWhiteSpace(directory))return null;
        try
        {
            string path=Path.GetFullPath(directory);
            // Exists alone can succeed for a directory the current user cannot read.
            using var entries=Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();return path;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException){return null;}
    }
}
