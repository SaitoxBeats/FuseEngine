namespace Blowtorch.Model;

public class MapObject
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;

    public string? Mesh { get; set; }
    public string? Model { get; set; }
    public float ModelScale { get; set; } = 1.0f;

    public string? Texture { get; set; }
    public string? Interactable { get; set; }
    public MapBody? Body { get; set; }

    public bool IsModel => !string.IsNullOrEmpty(Model);
}