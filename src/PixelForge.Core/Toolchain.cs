using System.Diagnostics;
using System.IO;

namespace PixelForge.Core;
public sealed record Toolchain(string Blender, string Aseprite)
{
    public static Toolchain Discover() => new(Find("PIXELFORGE_BLENDER", "Blender", "blender.exe"), Find("PIXELFORGE_ASEPRITE", "Aseprite", "Aseprite.exe"));
    private static string Find(string variable,string directory,string executable)
    {
        var configured=Environment.GetEnvironmentVariable(variable);
        if(!string.IsNullOrWhiteSpace(configured))return configured;
        var steam=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steamapps","common",directory,executable);
        return steam;
    }
    public void Validate(bool requireAseprite=true)
    {
        if(!File.Exists(Blender))throw new FileNotFoundException("Blender was not found. Set PIXELFORGE_BLENDER to its executable path.",Blender);
        if(requireAseprite&&!File.Exists(Aseprite))throw new FileNotFoundException("Aseprite was not found. Set PIXELFORGE_ASEPRITE to its executable path.",Aseprite);
    }
    public static async Task Run(string executable,IEnumerable<string> arguments,string log,CancellationToken cancellation=default,IProgress<string>? progress=null)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException($"Could not start {executable}.");
        using var registration=cancellation.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        async Task<string> ReadOutput()
        {
            var lines=new System.Text.StringBuilder();
            while(await process.StandardOutput.ReadLineAsync() is { } line)
            { lines.AppendLine(line);if(line.StartsWith("PIXELFORGE_PROGRESS:"))progress?.Report(line[20..].Trim()); }
            return lines.ToString();
        }
        var stdout=ReadOutput();var stderr=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        var output=await stdout+Environment.NewLine+await stderr;await File.WriteAllTextAsync(log,output,CancellationToken.None);
        cancellation.ThrowIfCancellationRequested();
        if(process.ExitCode!=0)throw new IOException($"{Path.GetFileName(executable)} could not finish. Open the technical log for details: {log}");
    }
}
