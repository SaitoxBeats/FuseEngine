using System.Numerics;

namespace Fuse.UI;

public class HUDPanel : HUDElement
{
    private HUDLayout _layout;
    private Vector2 _size;
    private Vector4 _color;

    public HUDPanel(HUDLayout layout, Vector2 size, Vector4 color)
    {
        _layout = layout;
        _size = size;
        _color = color;
    }

    public Vector4 Color { get => _color; set => _color = value; }
    public Vector2 Size { get => _size; set => _size = value; }
    public HUDLayout Layout { get => _layout; set => _layout = value; }

    public override void Draw(Renderer.UIRenderer ui, int screenW, int screenH)
    {
        Vector2 pos = HUDHelper.ResolvePosition(_layout, screenW, screenH, _size);
        ui.DrawRect(pos.X, pos.Y, _size.X, _size.Y, _color);
    }
}
