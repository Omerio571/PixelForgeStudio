using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelForge.Core;

// Owns only GUID children of its private Temp root. A live lease prevents cross-instance cleanup.
public sealed class RenderSession : IDisposable
{
    private sealed record Inventory(string Owner,string Id,Dictionary<string,string> Files,bool Protected=false);
    private const string Owner="PixelForge.RenderSession.1";
    private readonly string root,library,id;
    private FileStream? lease;
    private bool disposed;
    private bool protectedInputs;
    private readonly Dictionary<string,string> registered=new(StringComparer.OrdinalIgnoreCase);
    public string DirectoryPath {get;}
    public List<string> Retained {get;}=[];
    public RenderSession(string profile)
    {
        root=Path.GetFullPath(Path.Combine(profile,"Temp"));library=Path.GetFullPath(Path.Combine(profile,"Library"));
        Directory.CreateDirectory(root);CheckPath(root);
        foreach(string folder in Directory.GetDirectories(root))TryClean(folder);
        id=Guid.NewGuid().ToString("N");DirectoryPath=Path.Combine(root,id);Directory.CreateDirectory(DirectoryPath);
        lease=new FileStream(Path.Combine(DirectoryPath,".lease"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        Snapshot();
    }
    private static void CheckPath(string path)
    {
        for(string? p=Path.GetFullPath(path);p is not null;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Session storage cannot use links or junctions.");
    }
    public bool IsManaged(string path)=>Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string,string> Scan(string folder)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            CheckPath(directory);
            foreach(string file in Directory.GetFiles(directory))
            {
                CheckPath(file);string relative=Path.GetRelativePath(folder,file);
                if(relative is ".lease" or ".session.json")continue;
                using var stream=File.OpenRead(file);result[relative]=Convert.ToHexString(SHA256.HashData(stream));
            }
            foreach(string child in Directory.GetDirectories(directory))Visit(child);
        }
        Visit(folder);return result;
    }
    public void Snapshot(string? generatedDirectory=null)
    {
        string folder=Path.GetFullPath(generatedDirectory??DirectoryPath);
        if(folder!=DirectoryPath&&!folder.StartsWith(DirectoryPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only this session's generated data can be registered.");
        foreach(var item in Scan(folder))registered.TryAdd(Path.GetRelativePath(DirectoryPath,Path.Combine(folder,item.Key)),item.Value);
        WriteInventory();
    }
    private void WriteInventory()
    {
        string staged=Path.Combine(DirectoryPath,".session-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(staged,JsonSerializer.Serialize(new Inventory(Owner,id,registered,protectedInputs)));File.Move(staged,Path.Combine(DirectoryPath,".session.json"),true);}
        finally{try{File.Delete(staged);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}}
    }
    public void ProtectInput(string file)
    {
        if(Path.GetFullPath(file).StartsWith(DirectoryPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){protectedInputs=true;WriteInventory();}
    }
    public bool Unchanged(string directory)
    {
        if(!Path.GetFullPath(directory).StartsWith(DirectoryPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return false;
        string prefix=Path.GetRelativePath(DirectoryPath,Path.GetFullPath(directory))+Path.DirectorySeparatorChar;
        var current=Scan(DirectoryPath).Where(p=>p.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToDictionary();
        var before=registered.Where(p=>p.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToDictionary();
        return current.Count==before.Count&&current.All(p=>before.TryGetValue(p.Key,out string? hash)&&hash==p.Value);
    }
    public void AcceptTimingChanges(string directory)
    {
        string prefix=Path.GetRelativePath(DirectoryPath,Path.GetFullPath(directory))+Path.DirectorySeparatorChar;
        foreach(string key in registered.Keys.Where(k=>k.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())registered.Remove(key);
        Snapshot(directory);
    }
    private void TryClean(string folder)
    {
        FileStream? abandoned=null;
        try
        {
            if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out _))return;
            CheckPath(folder);
            CheckPath(Path.Combine(folder,".session.json"));CheckPath(Path.Combine(folder,".lease"));
            var saved=JsonSerializer.Deserialize<Inventory>(File.ReadAllText(Path.Combine(folder,".session.json")));
            if(saved is null||saved.Owner!=Owner||saved.Id!=Path.GetFileName(folder))return;
            try{abandoned=new FileStream(Path.Combine(folder,".lease"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){return;} // Another instance holds the lease; it is not stale.
            if(saved.Protected){Retained.Add(folder);return;}
            var current=Scan(folder);
            if(current.Count!=saved.Files.Count||current.Any(p=>!saved.Files.TryGetValue(p.Key,out string? hash)||hash!=p.Value)){Retained.Add(folder);return;}
            abandoned.Dispose();abandoned=null;
            // Exact validated direct child only; no arbitrary user-selected path can reach this delete.
            Directory.Delete(folder,true);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){Retained.Add(folder);}
        finally{abandoned?.Dispose();}
    }
    public ConversionOutput Preserve(ConversionOutput output)
    {
        string original=Path.GetFullPath(output.Directory);
        if(!IsManaged(original))return output;
        if(File.Exists(Path.Combine(original,GeneratedAsset.ManifestName)))output=GeneratedAsset.Open(original);
        CheckPath(original);Scan(original);Directory.CreateDirectory(library);CheckPath(library);
        string destination=Path.Combine(library,Guid.NewGuid().ToString("N"));
        var linked=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        bool Internal(string path)=>Path.GetFullPath(path).StartsWith(original+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        string Map(string path)
        {
            if(Internal(path))return Path.Combine(destination,Path.GetRelativePath(original,path));
            if(!linked.TryGetValue(path,out string? target)){target=Path.Combine(destination,"linked-"+Guid.NewGuid().ToString("N")[..8]+"-"+Path.GetFileName(path));File.Copy(path,target,false);linked[path]=target;}
            return target;
        }
        var required=new[]{output.Png,output.Aseprite,output.Preview,output.Spritesheet,output.Metadata,output.NormalSheet,output.EmissionSheet}.OfType<string>().Concat(output.Frames.SelectMany(f=>new[]{f.Png,f.Normal,f.Emission}.OfType<string>()));
        bool own=!protectedInputs&&required.All(Internal)&&original.StartsWith(DirectoryPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        bool moved=false;
        if(own)
        {
            try{Directory.Move(original,destination);moved=true;}
            catch(Exception e)when((e is IOException or UnauthorizedAccessException)&&Directory.Exists(original)&&!Directory.Exists(destination)){}
        }
        if(!moved)
        {
            // Explicit preservation of a reopened temp render never moves another instance's files.
            Directory.CreateDirectory(destination);
            foreach(string file in Directory.GetFiles(original,"*",SearchOption.AllDirectories))
            {string target=Path.Combine(destination,Path.GetRelativePath(original,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,false);}
        }
        string prefix=Path.GetRelativePath(DirectoryPath,original)+Path.DirectorySeparatorChar;
        if(moved)foreach(string key in registered.Keys.Where(k=>k.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())registered.Remove(key);
        var saved=output with{Directory=destination,Png=Map(output.Png),Aseprite=Map(output.Aseprite),Preview=Map(output.Preview),Spritesheet=output.Spritesheet is null?null:Map(output.Spritesheet),Metadata=output.Metadata is null?null:Map(output.Metadata),NormalSheet=output.NormalSheet is null?null:Map(output.NormalSheet),EmissionSheet=output.EmissionSheet is null?null:Map(output.EmissionSheet),Frames=output.Frames.Select(f=>f with{Png=Map(f.Png),Normal=f.Normal is null?null:Map(f.Normal),Emission=f.Emission is null?null:Map(f.Emission)}).ToArray()};
        GeneratedAsset.Save(saved);WriteInventory();return saved;
    }
    public void Dispose(){if(disposed)return;disposed=true;lease?.Dispose();lease=null;TryClean(DirectoryPath);}
}
