namespace Fuse.Scene.Model;

public class MapObject
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;
    public string? ParentId { get; set; }

    public string? Mesh { get; set; }
    public string? Model { get; set; }
    public float ModelScale { get; set; } = 1.0f;
    public System.Numerics.Vector2 UvScale { get; set; } = System.Numerics.Vector2.One;

    public string? Texture { get; set; }
    public string? Interactable { get; set; }
    public string? Behaviour { get; set; }
    public MapBody? Body { get; set; }

    public bool IsModel => !string.IsNullOrEmpty(Model);

    public bool IsGloballyVisible(MapDocument doc)
    {
        if (!Visible) return false;
        if (string.IsNullOrEmpty(ParentId)) return true;
        for (int i = 0; i < doc.Objects.Count; i++)
        {
            if (doc.Objects[i].Id == ParentId)
            {
                return doc.Objects[i].IsGloballyVisible(doc);
            }
        }
        return true;
    }
}