using System.Numerics;
using Fuse.Core;
using Fuse.Interaction;

namespace Fuse.Behaviours;

[InteractableType("MovingFloor")]
public sealed class MovingFloor : IBehaviour
{
    public Renderer.Entity? Entity { get; set; }
    public Physics.PhysicsWorld? World { get; set; }

    public float DistanceZ = -4f;
    public float Speed = 2f;

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
        Vector3 newPos = MathUtil.MoveTowards(current, target, Speed * dt);

        World.SetBodyPositionAndRotation(Entity.Body.Native, newPos, Entity.Body.Rotation(World));

        if (Vector3.Distance(newPos, target) < 0.01f)
            _goingForward = !_goingForward;
    }
}