using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Fuse;

public static class MathUtil
{
    public static float Deg(float degress) => degress * MathF.PI / 180f;

    public static float MoveTowards(float current, float target, float maxDelta)
    {
        float diff = target - current;
        if (MathF.Abs(diff) <= maxDelta) return target;
        return current + MathF.Sign(diff) * maxDelta;
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        float diffX = target.X - current.X;
        float diffY = target.Y - current.Y;
        float diffZ = target.Z - current.Z;
        float dist = MathF.Sqrt(diffX * diffX + diffY * diffY + diffZ * diffZ);
        if (dist <= maxDelta) return target;
        float t = maxDelta / dist;
        return new Vector3(current.X + diffX * t, current.Y + diffY * t, current.Z + diffZ * t);
    }
}
