using System.IO;
using System.Text.Json;

namespace PixelForge.Core;

public sealed record RenderEntry(string Directory,string AssetName,DateTimeOffset Completed,AssetSettings Settings,ConversionOutput Output)
{
    public string? UnavailableReason
    {
        get
        {
            if(!System.IO.Directory.Exists(Directory))return "Output folder is unavailable";
            if(!File.Exists(Path.Combine(Directory,"asset.settings.json"))||!File.Exists(Output.Aseprite)||Output.Metadata is null||!File.Exists(Output.Metadata)||Output.Spritesheet is null||!File.Exists(Output.Spritesheet)||Output.Frames.Any(f=>!File.Exists(f.Png)))return "One or more saved output files are unavailable";
            return null;
        }
    }
    public string Label=>$"{AssetName} · {Completed.ToLocalTime():MM-dd HH:mm:ss} · {Settings.Width}×{Settings.Height} · {Settings.View}"+(UnavailableReason is null?"":" · unavailable");
}

// A reference list only: rotation never deletes or copies any generated artwork.
public sealed class RenderHistory
{
    private sealed record State(RenderEntry[] Recent,RenderEntry[] Favorites);
    private readonly string path;
    private readonly List<RenderEntry> recent=[];
    private readonly List<RenderEntry> favorites=[];
    public IReadOnlyList<RenderEntry> Recent=>recent;
    public IReadOnlyList<RenderEntry> Favorites=>favorites;
    public IEnumerable<RenderEntry> Entries=>recent.Concat(favorites).DistinctBy(e=>e.Directory,StringComparer.OrdinalIgnoreCase);
    public RenderHistory(string path)
    {
        this.path=path;
        if(!File.Exists(path))return;
        var state=JsonSerializer.Deserialize<State>(File.ReadAllText(path),AssetSettings.JsonOptions)??throw new InvalidDataException("Empty render history.");
        recent.AddRange((state.Recent??[]).DistinctBy(e=>e.Directory,StringComparer.OrdinalIgnoreCase).Take(4));
        favorites.AddRange((state.Favorites??[]).DistinctBy(e=>e.Directory,StringComparer.OrdinalIgnoreCase));
    }
    public RenderEntry AddCompleted(ConversionOutput output)
    {
        // Require the success marker written only after the full pipeline/native validation.
        using var validation=JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Directory,"validation.json")));
        foreach(var field in new[]{"paletteCompliant","binaryAlpha","sourceUnchanged","nativeReopened","layerCompositeMatches"})
            if(!validation.RootElement.GetProperty(field).GetBoolean())throw new InvalidDataException("Only successfully validated renders can enter history.");
        var saved=GeneratedAsset.Open(output.Directory);
        var settings=AssetSettings.Load(Path.Combine(saved.Directory,"asset.settings.json"));
        var entry=new RenderEntry(saved.Directory,Path.GetFileNameWithoutExtension(settings.Model),File.GetLastWriteTimeUtc(Path.Combine(saved.Directory,"validation.json")),settings,saved);
        recent.RemoveAll(e=>Same(e.Directory,entry.Directory));recent.Add(entry);recent.Sort((a,b)=>b.Completed.CompareTo(a.Completed));
        if(recent.Count>4)recent.RemoveRange(4,recent.Count-4);
        int favorite=favorites.FindIndex(e=>Same(e.Directory,entry.Directory));if(favorite>=0)favorites[favorite]=entry;
        Save();return entry;
    }
    public bool IsFavorite(RenderEntry entry)=>favorites.Any(e=>Same(e.Directory,entry.Directory));
    public void ToggleFavorite(RenderEntry entry)
    {
        if(IsFavorite(entry))favorites.RemoveAll(e=>Same(e.Directory,entry.Directory));else favorites.Add(entry);
        Save();
    }
    private static bool Same(string a,string b)=>StringComparer.OrdinalIgnoreCase.Equals(a,b);
    private void Save()
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var staging=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(staging,JsonSerializer.Serialize(new State(recent.ToArray(),favorites.ToArray()),AssetSettings.JsonOptions));File.Move(staging,path,true);}
        finally{if(File.Exists(staging))File.Delete(staging);}
    }
}
