using JoltPhysicsSharp;

namespace Fuse.Physics;

public sealed class DefaultBodyFilter : BodyFilter
{
    protected override bool ShouldCollide(BodyID bodyId) => true;
    protected override bool ShouldCollideLocked(Body body) => true;
}

public sealed class DefaultShapeFilter : ShapeFilter
{
}

public sealed class DefaultBroadPhaseLayerFilter : BroadPhaseLayerFilter
{
    protected override bool ShouldCollide(BroadPhaseLayer layer) => true;
}

public sealed class DefaultObjectLayerFilter : ObjectLayerFilter
{
    protected override bool ShouldCollide(ObjectLayer layer) => true;
}
