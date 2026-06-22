namespace Fuse.Scene.Model;

public class MapObject
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;

    public string? Mesh { get; set; }
    public string? Model { get; set; }
    public float ModelScale { get; set; } = 1.0f;
    public System.Numerics.Vector2 UvScale { get; set; } = System.Numerics.Vector2.One;

    public string? Texture { get; set; }
    public string? Interactable { get; set; }
    public MapBody? Body { get; set; }

    public bool IsModel => !string.IsNullOrEmpty(Model);
}