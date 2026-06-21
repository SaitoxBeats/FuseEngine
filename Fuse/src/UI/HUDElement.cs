using System.Numerics;

namespace Fuse.UI;

public enum HUDAnchor
{
    TopLeft, TopCenter, TopRight,
    CenterLeft, Center, CenterRight,
    BottomLeft, BottomCenter, BottomRight
}

public struct HUDLayout
{
    public HUDAnchor Anchor;
    public HUDAnchor Pivot;
    public Vector2 Offset;

    public HUDLayout(HUDAnchor anchor, HUDAnchor pivot, Vector2 offset)
    {
        Anchor = anchor;
        Pivot = pivot;
        Offset = offset;
    }
}

public static class HUDHelper
{
    public static Vector2 AnchorFactor(HUDAnchor a) => a switch
    {
        HUDAnchor.TopLeft => new(0, 0),
        HUDAnchor.TopCenter => new(0.5f, 0),
        HUDAnchor.TopRight => new(1, 0),
        HUDAnchor.CenterLeft => new(0, 0.5f),
        HUDAnchor.Center => new(0.5f, 0.5f),
        HUDAnchor.CenterRight => new(1, 0.5f),
        HUDAnchor.BottomLeft => new(0, 1),
        HUDAnchor.BottomCenter => new(0.5f, 1),
        HUDAnchor.BottomRight => new(1, 1),
        _ => Vector2.Zero,
    };

    public static Vector2 ResolvePosition(HUDLayout layout, int screenW, int screenH, Vector2 size)
    {
        Vector2 anchorPt = new Vector2(screenW, screenH) * AnchorFactor(layout.Anchor);
        Vector2 pivotOff = size * AnchorFactor(layout.Pivot);
        return anchorPt + layout.Offset - pivotOff;
    }

    public static HUDLayout MakeLayout(HUDAnchor anchor, Vector2 offset) =>
        new(anchor, anchor, offset);

    public static HUDLayout MakeLayout(HUDAnchor anchor, HUDAnchor pivot, Vector2 offset) =>
        new(anchor, pivot, offset);
}

public abstract class HUDElement
{
    public abstract void Draw(Renderer.UIRenderer ui, int screenW, int screenH);
    public virtual void Update(int screenW, int screenH) { }
}
