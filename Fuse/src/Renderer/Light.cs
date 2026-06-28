using System.Numerics;

namespace Fuse.Renderer;

public enum LightType
{
    Point,
    Spot
}

public class Light
{
    public string Id { get; set; } = "";
    public LightType Type { get; set; } = LightType.Point;
    public Vector3 Position { get; set; }
    public Vector3 Direction { get; set; } = -Vector3.UnitY;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Radius { get; set; } = 10.0f;
    public float Intensity { get; set; } = 1.0f;
    public float InnerConeAngle { get; set; } = float.DegreesToRadians(20);
    public float OuterConeAngle { get; set; } = float.DegreesToRadians(30);
    public bool Enabled { get; set; } = true;
    public bool CastShadows { get; set; } = false;
    public float ShadowBias { get; set; } = 0.005f;

    public float InnerCos => MathF.Cos(InnerConeAngle);
    public float OuterCos => MathF.Cos(OuterConeAngle);
}
