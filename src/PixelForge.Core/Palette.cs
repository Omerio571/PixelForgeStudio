using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PixelForge.Core;
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public uint Packed => (uint)(R << 16 | G << 8 | B);
}
public static class Palette
{
    public const int MaximumColors=4096; // Native projects are RGB, not indexed (no 256-color cap).
    public static Rgb[] ParseList(string text)
    {
        text=text.Trim();
        if(text.Length==0)throw new FormatException("Paste HEX colors or RGB triples first.");
        if(text.StartsWith('['))
        {
            try
            {
                using var json=System.Text.Json.JsonDocument.Parse(text);
                var root=json.RootElement;
                if(root.ValueKind!=System.Text.Json.JsonValueKind.Array)throw new FormatException("Expected an RGB list.");
                if(root.GetArrayLength()==3&&root.EnumerateArray().All(v=>v.ValueKind==System.Text.Json.JsonValueKind.Number))return [ParseColor(string.Join(',',root.EnumerateArray().Select(v=>v.ToString())))];
                return Normalize(root.EnumerateArray().Select(v=>v.ValueKind==System.Text.Json.JsonValueKind.String?ParseColor(v.GetString()!):ParseColor(string.Join(',',v.EnumerateArray().Select(n=>n.ToString())))));
            }
            catch(Exception e)when(e is System.Text.Json.JsonException or InvalidOperationException){throw new FormatException("Use a JSON list such as [[255,170,0],[112,119,132]] or [\"#242735\",\"#707784\"].",e);}
        }
        var hexTokens=Regex.Split(text,@"[\s,;]+").Where(s=>s.Length>0).ToArray();
        if(text.Contains('#')||hexTokens.All(s=>Regex.IsMatch(s,"^[0-9a-fA-F]{6}$")))return Normalize(hexTokens.Select(ParseColor));
        var rows=Regex.Split(text,@"[;\r\n]+").Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
        return Normalize(rows.Select(ParseColor));
    }
    private static Rgb[] Normalize(IEnumerable<Rgb> colors)
    {
        var result=colors.Distinct().ToArray();
        if(result.Length==0||result.Length>MaximumColors)throw new InvalidDataException($"Use 1–{MaximumColors} distinct colors. RGB Aseprite output preserves this palette; indexed export is not used.");
        return result;
    }
    public static Rgb[] LoadPng(string path)
    {
        var image=Raster.Read(path);var colors=new List<Rgb>();var seen=new HashSet<Rgb>();
        for(int p=0;p<image.Pixels.Length;p+=4)
        {
            // Partial-alpha pixels are not approved opaque swatches; never blend them.
            if(image.Pixels[p+3]!=255)continue;
            var color=new Rgb(image.Pixels[p+2],image.Pixels[p+1],image.Pixels[p]);
            if(seen.Add(color))colors.Add(color);
            if(colors.Count>MaximumColors)throw new InvalidDataException($"This PNG contains more than {MaximumColors} opaque colors. Use a swatch image instead of a photograph.");
        }
        return Normalize(colors);
    }
    public static async Task<Rgb[]> LoadNative(string path,string aseprite)
    {
        if(!File.Exists(aseprite))throw new FileNotFoundException("Aseprite was not found. Set PIXELFORGE_ASEPRITE to its executable path.",aseprite);
        var temp=Path.Combine(Path.GetTempPath(),"PixelForge-palette-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var script=Path.Combine(temp,"palette.lua");var data=Path.Combine(temp,"palette.txt");
        static string Q(string p)=>"\""+p.Replace("\\","/").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"";
        await File.WriteAllTextAsync(script,$"local s=app.open({Q(Path.GetFullPath(path))})\nassert(s,'Cannot open native palette source')\nlocal f=io.open({Q(data)},'w')\nlocal p=s.palettes[1]\nfor i=0,#p-1 do local c=p:getColor(i); f:write(string.format('#%02X%02X%02X\\n',c.red,c.green,c.blue)) end\nf:close()\n");
        await Toolchain.Run(aseprite,["--batch","--script",script],Path.Combine(temp,"aseprite.log"));
        var colors=File.ReadAllLines(data).Select(ParseColor).ToArray();
        if(colors.Length==0||colors.Length>MaximumColors)throw new InvalidDataException($"The native project must contain 1–{MaximumColors} palette entries. Export a smaller approved palette first.");
        return colors;
    }
    // Neutral starter palette. This is not the user's Hollow Core palette.
    public static Rgb[] Default { get; } = new[] { "#171923", "#303442", "#505563", "#747D86", "#A5AFB0", "#DFE4D8", "#30251F", "#50392B", "#795438", "#A77749", "#D1A46A", "#E9CE94", "#253C36", "#41634D", "#73905A", "#A2B879", "#423349", "#765060", "#AE6C65", "#DA9A80", "#263E55", "#466781", "#739BA6", "#AFCDD0" }.Select(ParseColor).ToArray();
    public static Rgb ParseColor(string text)
    {
        text = text.Trim();
        if (Regex.IsMatch(text, "^#?[0-9a-fA-F]{6}$")) { var v = uint.Parse(text.TrimStart('#'), NumberStyles.HexNumber); return new((byte)(v >> 16), (byte)(v >> 8), (byte)v); }
        var parts = Regex.Split(text.Replace("rgb(", "", StringComparison.OrdinalIgnoreCase).TrimEnd(')'), @"[\s,]+");
        if (parts.Length == 3 && parts.All(p => byte.TryParse(p, out _))) return new(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
        throw new FormatException($"Invalid color '{text}'. Use #RRGGBB or R,G,B (0–255).");
    }
    public static Rgb[] SelectIndices(IReadOnlyList<Rgb> source, string indices)
    {
        if (string.IsNullOrWhiteSpace(indices)) return source.ToArray();
        return Regex.Split(indices.Trim(), @"[\s,]+").Select(s => int.TryParse(s, out var i) && i >= 0 && i < source.Count ? source[i] : throw new ArgumentException($"Palette index '{s}' is outside 0–{source.Count - 1}.")).ToArray();
    }
    public static Rgb[] Load(string path)
    {
        var lines = File.ReadAllLines(path); var colors = new List<Rgb>();
        if (lines.Length == 0) throw new InvalidDataException("Palette file is empty.");
        int start;
        if (lines[0].Trim() == "GIMP Palette") start = 1;
        else if (lines[0].Trim() == "JASC-PAL" && lines.Length >= 3 && lines[1].Trim() == "0100") start = 3;
        else throw new InvalidDataException("Expected a GIMP GPL or JASC PAL palette.");
        foreach (var line in lines.Skip(start))
        {
            var t = line.Trim(); if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("Name:") || t.StartsWith("Columns:")) continue;
            var m = Regex.Match(t, @"^(\d+)\s+(\d+)\s+(\d+)(?:\s|$)");
            if (!m.Success) throw new InvalidDataException($"Invalid palette row: {t}");
            colors.Add(ParseColor($"{m.Groups[1]},{m.Groups[2]},{m.Groups[3]}"));
        }
        if (colors.Count == 0 || colors.Count > 4096) throw new InvalidDataException("Palette must contain 1–4096 colors.");
        if (start == 3 && (!int.TryParse(lines[2], out var count) || count != colors.Count)) throw new InvalidDataException("PAL entry count does not match its header.");
        return colors.ToArray();
    }
}
