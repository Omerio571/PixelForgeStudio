using System.IO;
using System.Text.Json;

namespace PixelForge.Core;

public sealed record PfaExportResult(string Directory,int Directions,int Layers,long Bytes);

public static class PfaExport
{
    public static PfaExportResult Save(AssetExportProfile profile,string name,ConversionOutput[] sources,CancellationToken token=default)
    {
        if(!Path.IsPathFullyQualified(profile.Root)||!Directory.Exists(profile.Root))throw new InvalidDataException("Choose an existing Assets root.");
        Pfa.Segment(name);Pfa.Segment(profile.Category);
        if(sources.Length==0||!(profile.Base||profile.Normal||profile.Emission))throw new InvalidDataException("Choose an asset and at least one layer.");
        foreach(string reference in new[]{profile.Base?profile.BasePalette:"",profile.Normal?profile.NormalPalette:"",profile.Emission?profile.EmissionPalette:""}.Where(x=>x.Length>0))
            if(!File.Exists(Pfa.PalettePath(profile.Root,reference)))throw new FileNotFoundException("Required palette: "+reference);
        if(sources.Length>1&&(profile.Base&&profile.BasePng.Length>0||profile.Normal&&profile.NormalPng.Length>0||profile.Emission&&profile.EmissionPng.Length>0))
            throw new InvalidDataException("External PNGs apply to one direction only. Disable All generated directions or clear the overrides.");
        var rootId=Pfa.RootId(profile.Root,true);
        string category=Path.Combine(profile.Root,profile.Category);
        Directory.CreateDirectory(category);
        if(File.GetAttributes(category).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Linked asset categories are unsupported.");
        string destination=Path.Combine(category,name);
        for(int n=2;Path.Exists(destination);n++)destination=Path.Combine(category,name+"-v"+n);
        string stage=Path.Combine(category,".pfa-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        int count=0;long bytes=0;
        try
        {
            var directions=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var source in sources)
            {
                token.ThrowIfCancellationRequested();
                var settings=AssetSettings.Load(Path.Combine(source.Directory,"asset.settings.json"));
                var camera=settings.Camera??CameraSelection.FromLegacy(settings.View,settings.Projection);
                string direction=camera.Direction.ToString() switch {"N"=>"North","E"=>"East","S"=>"South","W"=>"West",var value=>value};
                if(!directions.Add(direction))throw new InvalidDataException("The source includes the same direction twice.");
                string folder=Path.Combine(stage,name+direction);Directory.CreateDirectory(folder);
                using var metadata=JsonDocument.Parse(File.ReadAllText(source.Metadata??throw new InvalidDataException("Render metadata is missing.")));
                var root=metadata.RootElement;
                JsonElement? Get(string key)=>root.TryGetProperty(key,out var node)?node:null;
                var placement=JsonSerializer.SerializeToElement(new{pivot=Get("pivot"),worldUnitsPerPixel=Get("worldUnitsPerPixel"),
                    camera=Get("camera"),cameraElevation=Get("cameraElevation"),tags=Get("tags"),normalSpace="+X East, +Y North, +Z Up; RGB=(N+1)/2"});
                var paths=source.Frames;
                var baseReference=profile.Base&&profile.BasePng.Length>0
                    ?ExternalFrames(profile.BasePng,root,settings.Width,settings.Height,paths.Length,token):null;
                foreach(var (layer,enabled,palette,external) in new[]{(PfaLayer.Base,profile.Base,profile.BasePalette,profile.BasePng),
                    (PfaLayer.Normal,profile.Normal,profile.NormalPalette,profile.NormalPng),(PfaLayer.Emission,profile.Emission,profile.EmissionPalette,profile.EmissionPng)})
                {
                    if(!enabled)continue;
                    var externalFrames=external.Length==0?null:layer==PfaLayer.Base?baseReference:
                        ExternalFrames(external,root,settings.Width,settings.Height,paths.Length,token);
                    var frames=new Raster[paths.Length];
                    for(int i=0;i<frames.Length;i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var frame=paths[i];
                        string? file=layer switch {PfaLayer.Base=>frame.Png,PfaLayer.Normal=>frame.Normal,_=>frame.Emission};
                        if(externalFrames is null&&(file is null||!File.Exists(file)))
                            throw new InvalidDataException($"{direction} has no {layer} frame {i+1}. Regenerate the dynamic maps or deselect that layer for this export.");
                        frames[i]=externalFrames?[i]??IndexedLab.IndexedBenchmark.Load(file!);
                        if(layer==PfaLayer.Base)continue;
                        var color=baseReference?[i]??IndexedLab.IndexedBenchmark.Load(frame.Png);
                        if(color.Width!=frames[i].Width||color.Height!=frames[i].Height||
                            Enumerable.Range(0,color.Width*color.Height).Any(p=>color.Pixels[p*4+3]!=frames[i].Pixels[p*4+3]))
                            throw new InvalidDataException($"{direction} {layer} frame {i+1} does not align with base color.");
                    }
                    string pfa=Path.Combine(folder,name+direction+layer+".pfa");
                    var info=new PfaInfo(name,layer,direction,settings.Width,settings.Height,paths.Select(f=>f.DurationMs).ToArray(),rootId,palette,"",placement);
                    var written=Pfa.Write(pfa,profile.Root,info,frames,profile.Compression,token);
                    bytes+=written.FileBytes;count++;
                    if(profile.PngPreviews)for(int i=0;i<frames.Length;i++)
                        frames[i].Save(Path.Combine(folder,$"{layer}-{i+1:000}.png"));
                }
            }
            token.ThrowIfCancellationRequested();Directory.Move(stage,destination);
            return new(destination,directions.Count,count,bytes);
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private static Raster[] ExternalFrames(string path,JsonElement layout,int width,int height,int count,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))
            throw new FileNotFoundException("External layer PNG is missing. Select another file or clear the override: "+path);
        var image=IndexedLab.IndexedBenchmark.Load(path);
        if(count==1&&image.Width==width&&image.Height==height)return [image];
        if(image.Width!=layout.GetProperty("width").GetInt32()||image.Height!=layout.GetProperty("height").GetInt32())
            throw new InvalidDataException("External PNG must match the current render atlas dimensions: "+path);
        var rectangles=layout.GetProperty("frames");
        if(rectangles.GetArrayLength()!=count)throw new InvalidDataException("External layer frame count does not match the render.");
        return rectangles.EnumerateArray().Select(rectangle=>{
            token.ThrowIfCancellationRequested();
            int x=rectangle.GetProperty("x").GetInt32(),y=rectangle.GetProperty("y").GetInt32();
            if(x<0||y<0||x>image.Width-width||y>image.Height-height)
                throw new InvalidDataException("External layer frame lies outside the atlas.");
            var pixels=new byte[width*height*4];
            for(int row=0;row<height;row++)Buffer.BlockCopy(image.Pixels,((y+row)*image.Width+x)*4,pixels,row*width*4,width*4);
            return new Raster(width,height,pixels);
        }).ToArray();
    }
}
