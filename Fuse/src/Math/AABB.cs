using System.Numerics;

namespace Fuse.Math;

public struct AABB
{
    private Vector3 _extents;
    private Vector3 _center;
    private Vector3 _boundsMin;
    private Vector3 _boundsMax;

    private const float Large = 1e30f;

    public AABB()
    {
        _boundsMin = new Vector3(Large);
        _boundsMax = new Vector3(-Large);
        CalculateCenterAndExtents();
    }

    public AABB(Vector3 min, Vector3 max)
    {
        _boundsMin = min;
        _boundsMax = max;
        CalculateCenterAndExtents();
    }

    public readonly Vector3 GetCenter() => _center;
    public readonly Vector3 GetBoundsMin() => _boundsMin;
    public readonly Vector3 GetBoundsMax() => _boundsMax;
    public readonly Vector3 GetExtents() => _extents;

    public void Grow(AABB b)
    {
        if (b._boundsMin.X != Large && b._boundsMin.X != -Large)
        {
            Grow(b._boundsMin);
            Grow(b._boundsMax);
        }
        CalculateCenterAndExtents();
    }

    public void Grow(Vector3 p)
    {
        _boundsMin = Vector3.Min(_boundsMin, p);
        _boundsMax = Vector3.Max(_boundsMax, p);
        CalculateCenterAndExtents();
    }

    public readonly float Area()
    {
        Vector3 e = _boundsMax - _boundsMin;
        return 2.0f * (e.X * e.Y + e.Y * e.Z + e.Z * e.X);
    }

    public readonly bool ContainsPoint(Vector3 point) =>
        point.X >= _boundsMin.X && point.X <= _boundsMax.X &&
        point.Y >= _boundsMin.Y && point.Y <= _boundsMax.Y &&
        point.Z >= _boundsMin.Z && point.Z <= _boundsMax.Z;

    public readonly bool IntersectsSphere(Vector3 sphereCenter, float radius)
    {
        Vector3 closest = Vector3.Clamp(sphereCenter, _boundsMin, _boundsMax);
        float distSq = Vector3.DistanceSquared(closest, sphereCenter);
        return distSq <= radius * radius;
    }

    public readonly bool IntersectsAABB(AABB other) =>
        _boundsMin.X <= other._boundsMax.X && _boundsMax.X >= other._boundsMin.X &&
        _boundsMin.Y <= other._boundsMax.Y && _boundsMax.Y >= other._boundsMin.Y &&
        _boundsMin.Z <= other._boundsMax.Z && _boundsMax.Z >= other._boundsMin.Z;

    public readonly bool IntersectsAABB(AABB other, float threshold)
    {
        var inflatedMinA = _boundsMin - new Vector3(threshold);
        var inflatedMaxA = _boundsMax + new Vector3(threshold);
        var inflatedMinB = other._boundsMin - new Vector3(threshold);
        var inflatedMaxB = other._boundsMax + new Vector3(threshold);

        return inflatedMinA.X <= inflatedMaxB.X && inflatedMaxA.X >= inflatedMinB.X &&
               inflatedMinA.Y <= inflatedMaxB.Y && inflatedMaxA.Y >= inflatedMinB.Y &&
               inflatedMinA.Z <= inflatedMaxB.Z && inflatedMaxA.Z >= inflatedMinB.Z;
    }

    public readonly Vector3 NearestPointTo(Vector3 worldPosition) =>
        Vector3.Clamp(worldPosition, _boundsMin, _boundsMax);

    private void CalculateCenterAndExtents()
    {
        _center = (_boundsMin + _boundsMax) * 0.5f;
        _extents = _boundsMax - _boundsMin;
    }
}
