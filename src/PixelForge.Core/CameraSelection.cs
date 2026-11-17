using System.Text.Json.Serialization;

namespace PixelForge.Core;

public enum CameraFamily { Isometric, Top, Bottom, Side }
public enum CameraDirection { NE, SE, SW, NW, N, E, S, W }
public enum CameraPerspective
{
    Dimetric2To1, TrueIsometric, DirectTop, TopDownThreeQuarter, HighTopDown,
    DirectBottom, AngledUnderside, StraightSide, SlightlyElevatedSide
}

public sealed record CameraSelection(CameraFamily Family, CameraDirection Direction, CameraPerspective Perspective, double? Elevation = null)
{
    public static CameraDirection[] Directions(CameraFamily family) => family == CameraFamily.Isometric
        ? [CameraDirection.NE, CameraDirection.SE, CameraDirection.SW, CameraDirection.NW]
        : [CameraDirection.N, CameraDirection.E, CameraDirection.S, CameraDirection.W];

    public static CameraPerspective[] Perspectives(CameraFamily family) => family switch
    {
        CameraFamily.Isometric => [CameraPerspective.Dimetric2To1, CameraPerspective.TrueIsometric],
        CameraFamily.Top => [CameraPerspective.DirectTop, CameraPerspective.TopDownThreeQuarter, CameraPerspective.HighTopDown],
        CameraFamily.Bottom => [CameraPerspective.DirectBottom, CameraPerspective.AngledUnderside],
        CameraFamily.Side => [CameraPerspective.StraightSide, CameraPerspective.SlightlyElevatedSide],
        _ => []
    };

    public static CameraSelection FromLegacy(CameraView view, Projection projection) => view switch
    {
        CameraView.Top => new(CameraFamily.Top, CameraDirection.N, CameraPerspective.DirectTop),
        CameraView.Bottom => new(CameraFamily.Bottom, CameraDirection.N, CameraPerspective.DirectBottom),
        CameraView.Front => new(CameraFamily.Side, CameraDirection.S, CameraPerspective.StraightSide),
        CameraView.Back => new(CameraFamily.Side, CameraDirection.N, CameraPerspective.StraightSide),
        CameraView.Left => new(CameraFamily.Side, CameraDirection.W, CameraPerspective.StraightSide),
        CameraView.Right => new(CameraFamily.Side, CameraDirection.E, CameraPerspective.StraightSide),
        _ => new(CameraFamily.Isometric, Enum.Parse<CameraDirection>(view.ToString()[9..]),
            projection == Projection.TrueIsometric ? CameraPerspective.TrueIsometric : CameraPerspective.Dimetric2To1)
    };

    public void Validate()
    {
        if (!Directions(Family).Contains(Direction) || !Perspectives(Family).Contains(Perspective))
            throw new ArgumentException("Choose a direction and perspective supported by the camera view type.");
        if (Elevation is double angle && (Perspective != CameraPerspective.TopDownThreeQuarter ||
            !double.IsFinite(angle) || angle is < 40 or > 85))
            throw new ArgumentException("Top-Down 3/4 elevation must be 40–85°.");
    }
}
