using System;
using System.Collections.Generic;
using System.Numerics;

class Program
{
    static void Main()
    {
        var faces = new List<Plane>();
        
        float k = 0.5f;
        // Top, Bottom, Right, Left, Front, Back
        faces.Add(new Plane(new Vector3(0, 1, 0), -1));
        faces.Add(new Plane(new Vector3(0, -1, 0), -1));
        
        var rNormal = new Vector3(1, -k, 0);
        float rLen = rNormal.Length();
        faces.Add(new Plane(rNormal / rLen, -1 / rLen));
        
        var lNormal = new Vector3(-1, k, 0);
        float lLen = lNormal.Length();
        faces.Add(new Plane(lNormal / lLen, -1 / lLen));
        
        faces.Add(new Plane(new Vector3(0, 0, 1), -1));
        faces.Add(new Plane(new Vector3(0, 0, -1), -1));
        
        var vertices = new List<Vector3>();
        int numFaces = faces.Count;
        for (int i = 0; i < numFaces - 2; i++)
        for (int j = i + 1; j < numFaces - 1; j++)
        for (int kIdx = j + 1; kIdx < numFaces; kIdx++)
        {
            if (TryIntersectPlanes(faces[i], faces[j], faces[kIdx], out Vector3 v))
            {
                bool valid = true;
                foreach (var face in faces)
                {
                    if (Vector3.Dot(face.Normal, v) + face.D > 0.001f)
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid)
                {
                    vertices.Add(v);
                }
            }
        }
        
        Console.WriteLine($"Found {vertices.Count} vertices.");
        foreach(var v in vertices) Console.WriteLine(v);
    }
    
    private static bool TryIntersectPlanes(Plane p1, Plane p2, Plane p3, out Vector3 intersection)
    {
        float det = Vector3.Dot(p1.Normal, Vector3.Cross(p2.Normal, p3.Normal));
        if (System.Math.Abs(det) < 0.0001f)
        {
            intersection = Vector3.Zero;
            return false;
        }

        intersection = -(p1.D * Vector3.Cross(p2.Normal, p3.Normal) +
                         p2.D * Vector3.Cross(p3.Normal, p1.Normal) +
                         p3.D * Vector3.Cross(p1.Normal, p2.Normal)) / det;
        return true;
    }
}
