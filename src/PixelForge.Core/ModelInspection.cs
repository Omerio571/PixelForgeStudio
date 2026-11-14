using System.IO;
using System.Text.Json;

namespace PixelForge.Core;
public sealed record ModelInspection(int Start,int End,int Current,double Fps,bool HasAnimation)
{
    public string? WorkingDirectory {get;init;}
    public static async Task<ModelInspection> Read(string model,Toolchain tools,CancellationToken cancellation=default,string? temporaryRoot=null)
    {
        tools.Validate(false);
        if(!File.Exists(model))throw new FileNotFoundException("Choose an existing Blender model.",model);
        string directory=Path.Combine(temporaryRoot??Path.GetTempPath(),"PixelForge-inspection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var job=JsonSerializer.SerializeToNode(new AssetSettings{Model=Path.GetFullPath(model)},AssetSettings.JsonOptions)!.AsObject();job["output"]=directory;job["inspect"]=true;
        var path=Path.Combine(directory,"job.json");await File.WriteAllTextAsync(path,job.ToJsonString(),cancellation);
        await Toolchain.Run(tools.Blender,["--background","--factory-startup","--disable-autoexec","--python-exit-code","1","--python",Path.Combine(AppContext.BaseDirectory,"bridge","render.py"),"--",path],Path.Combine(directory,"blender.log"),cancellation);
        using var data=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"inspection.json"),cancellation));var root=data.RootElement;
        return new ModelInspection(root.GetProperty("start").GetInt32(),root.GetProperty("end").GetInt32(),root.GetProperty("current").GetInt32(),root.GetProperty("fps").GetDouble(),root.GetProperty("has_animation").GetBoolean()){WorkingDirectory=directory};
    }
}
