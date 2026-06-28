using System.Numerics;
using Fuse.Core;
using Fuse.Interaction;

namespace Fuse.Behaviours;

[Behaviour("MovingFloor")]
public sealed class MovingFloor : IBehaviour
{
    public Renderer.Entity? Entity { get; set; }
    public Physics.PhysicsWorld? World { get; set; }

    [Export] public float DistanceZ { get; set; } = -4f;
    [Export] public float Speed { get; set; } = 2f;

    private Vector3 _startPos;
    private Vector3 _targetPos;
    private bool _goingForward = true;
    private bool _initialized;

    public void Update(float dt)
    {
        if (Entity?.Body == null || !Entity.Body.IsBuilt || World == null)
            return;

        if (!_initialized)
        {
            _startPos = Entity.Body.Position(World);
            _targetPos = _startPos + new Vector3(0f, 0f, DistanceZ);
            _initialized = true;
        }

        Vector3 current = Entity.Body.Position(World);
        Vector3 target = _goingForward ? _targetPos : _startPos;
        float dist = Vector3.Distance(current, target);

        if (dist < 0.01f)
        {
            _goingForward = !_goingForward;
            World.BodyInterface.SetLinearVelocity(Entity.Body.Native, Vector3.Zero);
            return;
        }

        Vector3 dir = Vector3.Normalize(target - current);
        World.BodyInterface.SetLinearVelocity(Entity.Body.Native, dir * Speed);
    }
}