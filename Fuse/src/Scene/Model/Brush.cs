using System.Collections.Generic;

namespace Fuse.Scene.Model;

public class Brush : MapObject
{
    public List<Face> Faces { get; set; } = new();

    public void AddFace(Face face)
    {
        Faces.Add(face);
    }

    public void UpdatePlanesFromHalfExtents(System.Numerics.Vector3 half)
    {
        foreach (var face in Faces)
        {
            var normal = face.Plane.Normal;
            if (System.Math.Abs(normal.X) > 0.9f)
            {
                face.Plane = new System.Numerics.Plane(normal, -half.X);
            }
            else if (System.Math.Abs(normal.Y) > 0.9f)
            {
                face.Plane = new System.Numerics.Plane(normal, -half.Y);
            }
            else if (System.Math.Abs(normal.Z) > 0.9f)
            {
                face.Plane = new System.Numerics.Plane(normal, -half.Z);
            }
        }
    }

    public static Brush CreateCube(System.Numerics.Vector3 position, System.Numerics.Vector3 size)
    {
        var brush = new Brush { Id = "brush_" + System.Guid.NewGuid().ToString().Substring(0, 8) };
        System.Numerics.Vector3 half = size / 2.0f;
        
        // Front, Back, Top, Bottom, Right, Left
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(0, 0, 1), -half.Z)));
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(0, 0, -1), -half.Z)));
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(0, 1, 0), -half.Y)));
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(0, -1, 0), -half.Y)));
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(1, 0, 0), -half.X)));
        brush.AddFace(new Face(new System.Numerics.Plane(new System.Numerics.Vector3(-1, 0, 0), -half.X)));
        
        // Add a body for selection
        brush.Body = new MapBody
        {
            Shape = MapShapeType.Box,
            Position = position,
            HalfExtents = half
        };

        return brush;
    }
}
