using System.IO;
using System.Text.Json;

namespace PixelForge.Core;

public enum CameraView { IsometricSE, IsometricSW, IsometricNE, IsometricNW, Top, Bottom, Front, Back, Left, Right }
public enum Projection { Dimetric2To1, TrueIsometric }
public enum OutlineStyle { OuterSilhouette, VisibleComponents, Selective }
public enum GroupedOutlineMode { Conservative, Unified }
public enum FitBoundsSource { ObjectOnly, ObjectAndGroundShadow }
public sealed record AssetSettings
{
    public string Model { get; init; } = "";
    public int Width { get; init; } = 128;
    public int Height { get; init; } = 128;
    public CameraView View { get; init; } = CameraView.IsometricSE;
    public Projection Projection { get; init; } = Projection.Dimetric2To1;
    public int SamplesPerPixel { get; init; } = 3;
    public double Padding { get; init; } = .08;
    public bool WorldScale { get; init; }
    public FitBoundsSource FitBounds { get; init; } = FitBoundsSource.ObjectOnly;
    public double PixelsPerUnit { get; init; } = 16;
    public double LightAzimuth { get; init; } = 135;
    public double LightElevation { get; init; } = 45;
    public double LightIntensity { get; init; } = 2.2;
    public double AmbientIntensity { get; init; } = .65;
    public double ShadowIntensity { get; init; } = 1;
    public bool CastShadows { get; init; } = true;
    public bool GroundShadows { get; init; }
    public double GroundShadowStrength { get; init; } = .65;
    public double GroundShadowSoftness { get; init; } = .05;
    public bool Animation { get; init; }
    public int? Frame { get; init; }
    public int StartFrame { get; init; } = 1;
    public int EndFrame { get; init; } = 12;
    public int FrameStep { get; init; } = 1;
    public int Fps { get; init; } = 12;
    public string AnimationTag { get; init; } = "Animation";
    public int FrameCount => Animation ? (EndFrame-StartFrame)/Math.Max(1,FrameStep)+1 : 1;
    public bool Outlines { get; init; }
    public bool RespectOutlineGroups { get; init; }
    public GroupedOutlineMode GroupedOutlines { get; init; } = GroupedOutlineMode.Unified;
    public OutlineStyle OutlineType { get; init; }
    public int OutlineThickness { get; init; } = 1;
    public string OutlineColor { get; init; } = "#171923";
    public bool EdgeHighlights { get; init; }
    public double EdgeHighlightStrength { get; init; } = .3;
    public string[] Colors { get; init; } = Palette.Default.Select(c => c.Hex).ToArray();
    public void Validate()
    {
        if (Width is < 1 or > 2048 || Height is < 1 or > 2048) throw new ArgumentException("Width and height must each be 1–2048 pixels.");
        if (SamplesPerPixel is not (1 or 3)) throw new ArgumentException("Sampling must be 1 (direct) or 3 (coverage).");
        if (!double.IsFinite(Padding) || Padding < 0 || Padding > .4) throw new ArgumentException("Padding must be between 0 and 0.4.");
        if (!double.IsFinite(PixelsPerUnit) || PixelsPerUnit <= 0 || PixelsPerUnit > 2048) throw new ArgumentException("Pixels per unit must be greater than zero and at most 2048.");
        if (!Enum.IsDefined(View) || !Enum.IsDefined(Projection)) throw new ArgumentException("Unknown camera or projection.");
        if (!Enum.IsDefined(FitBounds) || !Enum.IsDefined(GroupedOutlines)) throw new ArgumentException("Unknown fit-bounds or grouped-outline mode.");
        if (Colors.Length == 0 || Colors.Length > 4096) throw new ArgumentException("Select 1–4096 palette colors.");
        foreach (var color in Colors) Palette.ParseColor(color);
        if (!double.IsFinite(GroundShadowStrength) || GroundShadowStrength is < 0 or > 1 || !double.IsFinite(GroundShadowSoftness) || GroundShadowSoftness is < 0 or > .5) throw new ArgumentException("Ground-shadow strength must be 0–100% and softness 0–0.5 radians.");
        if (Frame is < -100000 or > 100000 || StartFrame is < -100000 or > 100000 || EndFrame is < -100000 or > 100000 || FrameStep is < 1 or > 100000 || Fps is < 1 or > 120) throw new ArgumentException("Use valid Blender frames (-100000–100000), a positive step and playback FPS from 1–120.");
        if (Animation && (EndFrame<StartFrame || FrameCount>256 || (long)Width*Height*FrameCount>16777216)) throw new ArgumentException("Animation must have 1–256 ordered frames and at most 16 million total output pixels. Increase frame step or reduce dimensions.");
        if (AnimationTag.Length>80 || AnimationTag.Any(char.IsControl)) throw new ArgumentException("Animation tag must be at most 80 characters with no control characters.");
        if (!double.IsFinite(LightAzimuth) || LightAzimuth is < -360 or > 360 || !double.IsFinite(LightElevation) || LightElevation is < 0 or > 90) throw new ArgumentException("Light direction needs an azimuth from -360 to 360° and elevation from 0 to 90°.");
        if (!double.IsFinite(LightIntensity) || LightIntensity is < 0 or > 10 || !double.IsFinite(AmbientIntensity) || AmbientIntensity is < 0 or > 5 || !double.IsFinite(ShadowIntensity) || ShadowIntensity is < 0 or > 1) throw new ArgumentException("Light intensity must be 0–10, ambient 0–5, and shadow intensity 0–1.");
        if (!Enum.IsDefined(OutlineType) || OutlineThickness is not (1 or 2)) throw new ArgumentException("Outline thickness must be 1 or 2 pixels.");
        if (!double.IsFinite(EdgeHighlightStrength) || EdgeHighlightStrength is < 0 or > 1) throw new ArgumentException("Edge highlight strength must be 0–100%.");
        if (Outlines)
        {
            var outline=Palette.ParseColor(OutlineColor);
            if (!Colors.Select(Palette.ParseColor).Contains(outline)) throw new ArgumentException("Choose an outline color from the approved palette.");
            if (.2126*outline.R+.7152*outline.G+.0722*outline.B > 140) throw new ArgumentException("The outline color is too light. Select a darker approved palette color (brightness at most 140/255), or disable outlines.");
        }
    }
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    public static AssetSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<AssetSettings>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Empty asset settings.");
        if (!Path.IsPathRooted(settings.Model)) settings = settings with { Model = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, settings.Model)) };
        settings.Validate(); return settings;
    }
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
}
