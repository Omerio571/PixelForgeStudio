using System.IO;
using System.Text.Json;

namespace PixelForge.Core;

public sealed record PfaExportResult(string Directory,int Directions,int Layers,long Bytes,string? Report=null);

public static class PfaExport
{
    public static PfaExportResult Save(AssetExportProfile profile,string name,ConversionOutput[] sources,CancellationToken token=default)
    {
        if(!Path.IsPathFullyQualified(profile.Root)||!Directory.Exists(profile.Root))throw new InvalidDataException("Choose an existing Assets root.");
        Pfa.Segment(name);Pfa.Segment(profile.Category);
        if(sources.Length==0||!(profile.Base||profile.Normal||profile.Emission))throw new InvalidDataException("Choose an asset and at least one layer.");
        if(!profile.ExportPfa&&!profile.ExportPng&&!profile.ExportWebp)
            throw new InvalidDataException("Select at least one output format.");
        if((profile.ExportPfa||profile.DetailedReport)&&profile.Base&&profile.BasePalette.Length>0&&
            !File.Exists(Pfa.PalettePath(profile.Root,profile.BasePalette)))
            throw new FileNotFoundException("Required palette: "+profile.BasePalette);
        var cwebp=ExactImageTools.Resolve(profile.Cwebp,"cwebp");var dwebp=ExactImageTools.Resolve(profile.Dwebp,"dwebp");
        if(profile.ExportWebp&&(cwebp is null||dwebp is null))
            throw new InvalidDataException("WebP export requires cwebp and dwebp. Set their paths under Palettes and encoding, or add both to PATH.");
        if(sources.Length>1&&(profile.Base&&profile.BasePng.Length>0||profile.Normal&&profile.NormalPng.Length>0||profile.Emission&&profile.EmissionPng.Length>0))
            throw new InvalidDataException("External PNGs apply to one direction only. Disable All generated directions or clear the overrides.");
        var rootId=profile.ExportPfa||profile.DetailedReport?Pfa.RootId(profile.Root,true):Guid.Empty;
        string category=Path.Combine(profile.Root,profile.Category);
        Directory.CreateDirectory(category);
        if(File.GetAttributes(category).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Linked asset categories are unsupported.");
        string destination=Path.Combine(category,name);
        for(int n=2;Path.Exists(destination);n++)destination=Path.Combine(category,name+"-v"+n);
        string stage=Path.Combine(category,".pfa-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        int count=0;long bytes=0;
        var report=profile.DetailedReport?new PfaCompressionReport(profile.Root):null;
        try
        {
            string work=Path.Combine(stage,".work");Directory.CreateDirectory(work);
            var tools=new ExactImageTools(work,token);
            var cjxl=ExactImageTools.Resolve(profile.Cjxl,"cjxl");var djxl=ExactImageTools.Resolve(profile.Djxl,"djxl");
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
                var layerFiles=new Dictionary<string,Dictionary<string,string>>();
                if(profile.Base&&profile.BasePng.Length>0&&cwebp is not null&&dwebp is not null&&(profile.ExportWebp||report is not null))
                {
                    if(!Path.IsPathFullyQualified(profile.BasePng)||!File.Exists(profile.BasePng))
                        throw new FileNotFoundException("External base PNG is missing: "+profile.BasePng);
                    ExactImageTools.ValidateExternalArtwork(profile.BasePng);
                }
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
                    string stem=name+direction+layer;
                    var info=new PfaInfo(name,layer,direction,settings.Width,settings.Height,paths.Select(f=>f.DurationMs).ToArray(),rootId,palette,"",placement);
                    var exported=new Dictionary<string,string>();layerFiles[layer.ToString()]=exported;
                    PfaImage? written=null;
                    if(profile.ExportPfa||report is not null)
                    {
                        string pfa=Path.Combine(profile.ExportPfa?folder:work,stem+".pfa");
                        written=Pfa.Write(pfa,profile.Root,info,frames,profile.Compression,token);
                        if(profile.ExportPfa)exported["pfa"]=Path.GetFileName(pfa);
                    }
                    if(profile.ExportPng||profile.ExportWebp||report is not null)
                    {
                        var atlas=Atlas(frames,root);
                        string png=Path.Combine(profile.ExportPng?folder:work,stem+".png");
                        atlas.Save(png);
                        if(IndexedLab.IndexedBenchmark.Verify(atlas,IndexedLab.IndexedBenchmark.Load(png,8192))!="PASS")
                            throw new InvalidDataException("PNG atlas did not reconstruct exactly.");
                        if(profile.ExportPng)exported["png"]=Path.GetFileName(png);
                        string? webp=null,jxl=null;
                        string webpState="Unavailable: configure cwebp and dwebp",jxlState="Unavailable: configure cjxl and djxl";
                        if((profile.ExportWebp||report is not null)&&cwebp is not null&&dwebp is not null)
                        {
                            string target=Path.Combine(profile.ExportWebp?folder:work,stem+".webp");
                            try
                            {
                                var probe=tools.Webp(cwebp,dwebp,png,target,atlas);
                                webp=target;webpState=$"{probe.Versions} · {probe.Encode:F1}/{probe.Decode:F1} ms · PASS";
                                if(profile.ExportWebp)exported["webp"]=Path.GetFileName(target);
                            }
                            catch(Exception e) when(!profile.ExportWebp&&e is not OperationCanceledException)
                            {webpState="FAIL: "+e.Message;}
                        }
                        if(report is not null&&cjxl is not null&&djxl is not null)
                        {
                            try
                            {
                                string target=Path.Combine(work,stem+".jxl");
                                var probe=tools.Jxl(cjxl,djxl,png,target,atlas);
                                jxl=target;jxlState=$"{probe.Versions} · {probe.Encode:F1}/{probe.Decode:F1} ms · PASS";
                            }
                            catch(Exception e) when(e is not OperationCanceledException){jxlState="FAIL: "+e.Message;}
                        }
                        if(report is not null)
                        {
                            var baseline=Pfa.Write(Path.Combine(work,stem+".baseline.pfa"),profile.Root,info,frames,profile.Compression,token,baseline:true);
                            report.Add(stem,frames,baseline,written!,png,webp,webpState,jxl,jxlState);
                        }
                    }
                    count++;
                    if(profile.PngPreviews)for(int i=0;i<frames.Length;i++)
                        frames[i].Save(Path.Combine(folder,$"{layer}-{i+1:000}.png"));
                }
                if(profile.ExportPng||profile.ExportWebp||report is not null)
                {
                    string Layout(Dictionary<string,Dictionary<string,string>> files)=>JsonSerializer.Serialize(new
                    {
                        version=1,asset=name,direction,placement,canvas=Get("canvas"),width=Get("width"),
                        height=Get("height"),columns=Get("columns"),rows=Get("rows"),frames=Get("frames"),layers=files
                    },AssetSettings.JsonOptions);
                    if(profile.ExportPng||profile.ExportWebp)
                        File.WriteAllText(Path.Combine(folder,name+direction+".asset.json"),Layout(layerFiles));
                    if(report is not null)
                    {
                        long ManifestBytes(string extension)
                        {
                            string path=Path.Combine(work,name+direction+"-"+extension+".asset.json");
                            var files=layerFiles.ToDictionary(item=>item.Key,item=>new Dictionary<string,string>{{extension,name+direction+item.Key+"."+extension}});
                            File.WriteAllText(path,Layout(files));return new FileInfo(path).Length;
                        }
                        report.AddDirection(direction,ManifestBytes("png"),ManifestBytes("webp"),ManifestBytes("jxl"));
                    }
                }
            }
            if(report is not null)File.WriteAllText(Path.Combine(stage,"compression-report.txt"),report.Render());
            Directory.Delete(work,true);
            bytes=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length);
            token.ThrowIfCancellationRequested();Directory.Move(stage,destination);
            return new(destination,directions.Count,count,bytes,report is null?null:Path.Combine(destination,"compression-report.txt"));
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    internal static Raster Atlas(Raster[] frames,JsonElement layout)
    {
        int width=layout.GetProperty("width").GetInt32(),height=layout.GetProperty("height").GetInt32();
        var rects=layout.GetProperty("frames");
        if(width is <1 or >8192||height is <1 or >8192||(long)width*height>33_554_432||rects.GetArrayLength()!=frames.Length)
            throw new InvalidDataException("Unexpected atlas dimensions or frame count.");
        var pixels=new byte[width*height*4];
        for(int i=0;i<frames.Length;i++)
        {
            var frame=frames[i];var rectangle=rects[i];
            int x=rectangle.GetProperty("x").GetInt32(),y=rectangle.GetProperty("y").GetInt32();
            if(x<0||y<0||x>width-frame.Width||y>height-frame.Height)
                throw new InvalidDataException("Frame is outside the sprite atlas.");
            for(int row=0;row<frame.Height;row++)Buffer.BlockCopy(frame.Pixels,row*frame.Width*4,pixels,((y+row)*width+x)*4,frame.Width*4);
        }
        return new(width,height,pixels);
    }

    private static Raster[] ExternalFrames(string path,JsonElement layout,int width,int height,int count,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))
            throw new FileNotFoundException("External layer PNG is missing. Select another file or clear the override: "+path);
        var image=IndexedLab.IndexedBenchmark.Load(path,8192);
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
