using System.Numerics;

namespace Fuse.UI;

public class HUDText : HUDElement
{
    private string _text = "";
    private HUDLayout _layout;
    private float _scale;
    private Vector4 _color;

    public HUDText(string text, HUDLayout layout, float scale = 1.0f, Vector4 color = default)
    {
        _text = text;
        _layout = layout;
        _scale = scale;
        _color = color == default ? Vector4.One : color;
    }

    public string Text { get => _text; set => _text = value; }
    public Vector4 Color { get => _color; set => _color = value; }
    public float Scale { get => _scale; set => _scale = value; }
    public HUDLayout Layout { get => _layout; set => _layout = value; }

    public override void Draw(Renderer.UIRenderer ui, int screenW, int screenH)
    {
        if (string.IsNullOrEmpty(_text)) return;
        Vector2 size = Vector2.Zero;
        Vector2 pos = HUDHelper.ResolvePosition(_layout, screenW, screenH, size);
        ui.DrawText(pos.X, pos.Y, _text.AsSpan(), _color, _scale);
    }
}
