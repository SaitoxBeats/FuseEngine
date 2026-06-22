using System.Numerics;

namespace Fuse.Scene.Model;

public enum MapShapeType
{
    Box,
    Sphere,
    Capsule,
    Plane,
    Trimesh,
    None
}

public class MapBody
{
    public MapShapeType Shape { get; set; } = MapShapeType.None;
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float Mass { get; set; }
    public float Friction { get; set; }
    public float Restitution { get; set; }

    public Vector3? HalfExtents { get; set; }
    public float? Radius { get; set; }
    public float? Height { get; set; }
    public Vector3? Normal { get; set; }
    public float? Distance { get; set; }
}