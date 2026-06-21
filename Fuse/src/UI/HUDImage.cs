using System.Numerics;

namespace Fuse.UI;

public class HUDImage : HUDElement
{
    private Renderer.Texture? _tex;
    private HUDLayout _layout;
    private Vector2 _size;

    public HUDImage(Renderer.Texture tex, HUDLayout layout, Vector2 size)
    {
        _tex = tex;
        _layout = layout;
        _size = size;
    }

    public Renderer.Texture? Texture { get => _tex; set => _tex = value; }
    public Vector2 Size { get => _size; set => _size = value; }
    public HUDLayout Layout { get => _layout; set => _layout = value; }

    public override void Draw(Renderer.UIRenderer ui, int screenW, int screenH)
    {
        if (_tex == null) return;
        Vector2 pos = HUDHelper.ResolvePosition(_layout, screenW, screenH, _size);
        ui.DrawImage(_tex, pos.X, pos.Y, _size.X, _size.Y);
    }
}
