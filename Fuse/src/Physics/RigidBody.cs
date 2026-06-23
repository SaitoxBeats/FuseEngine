using System.Numerics;
using JoltPhysicsSharp;
using Fuse.Core;

namespace Fuse.Physics;

public class RigidBody
{
    public enum ShapeType
    {
        None,
        Box,
        Plane,
        Sphere,
        Capsule,
        Trimesh
    }

    private Shape? _shape;
    private BodyID _bodyID;
    private bool _built;

    private Vector3 _position;
    private Quaternion _rotation = Quaternion.Identity;
    private float _mass;
    private bool _isKinematic;
    private float _friction = 0.5f;
    private float _restitution = 0.3f;

    private ShapeType _shapeType;
    private Vector3 _boxHalfExtents = new(0.5f);
    private Vector3 _planeNormal = new(0, 1, 0);
    private float _planeDistance;
    private float _sphereRadius = 0.5f;
    private float _capsuleRadius = 0.4f;
    private float _capsuleHeight = 1.8f;
    private Vector3[]? _trimeshVerts;
    private uint[]? _trimeshIndices;

    public RigidBody SetBox(Vector3 halfExtents)
    {
        _shapeType = ShapeType.Box;
        _boxHalfExtents = halfExtents;
        return this;
    }

    public RigidBody SetPlane(Vector3 normal, float distance)
    {
        _shapeType = ShapeType.Plane;
        _planeNormal = normal;
        _planeDistance = distance;
        return this;
    }

    public RigidBody SetSphere(float radius)
    {
        _shapeType = ShapeType.Sphere;
        _sphereRadius = radius;
        return this;
    }

    public RigidBody SetCapsule(float radius, float height)
    {
        _shapeType = ShapeType.Capsule;
        _capsuleRadius = radius;
        _capsuleHeight = height;
        return this;
    }

    public RigidBody SetTrimesh(Vector3[] vertices, uint[] indices)
    {
        _shapeType = ShapeType.Trimesh;
        _trimeshVerts = vertices;
        _trimeshIndices = indices;
        return this;
    }

    public RigidBody SetPosition(Vector3 pos) { _position = pos; return this; }
    public RigidBody SetRotation(Quaternion rot) { _rotation = rot; return this; }
    public RigidBody SetMass(float mass) { _mass = mass; return this; }
    public RigidBody SetKinematic(bool kinematic) { _isKinematic = kinematic; return this; }
    public RigidBody SetFriction(float f) { _friction = f; return this; }
    public RigidBody SetRestitution(float r) { _restitution = r; return this; }

    public void Build(PhysicsWorld world)
    {
        if (_built)
            Destroy();

        switch (_shapeType)
        {
            case ShapeType.Box:
                _shape = new BoxShape(_boxHalfExtents);
                break;

            case ShapeType.Plane:
            {
                var plane = new System.Numerics.Plane(_planeNormal, _planeDistance);
                _shape = new PlaneShape(plane, null, 500.0f);
                break;
            }

            case ShapeType.Sphere:
                _shape = new SphereShape(_sphereRadius);
                break;

            case ShapeType.Capsule:
                _shape = new CapsuleShape(_capsuleHeight * 0.5f, _capsuleRadius);
                break;

            case ShapeType.Trimesh:
            {
                if (_trimeshVerts == null || _trimeshVerts.Length == 0 || _trimeshIndices == null || _trimeshIndices.Length < 3)
                {
                    Logger.Error("RigidBody.Build TRIMESH with no data, skipping");
                    return;
                }

                if (_mass > 0)
                    {
                        var hullSettings = new ConvexHullShapeSettings(new Span<Vector3>(_trimeshVerts));
                        _shape = hullSettings.Create();
                        if (_shape == null)
                        {
                            Logger.Error("RigidBody.Build ConvexHull creation failed");
                            return;
                        }
                    }
                    else
                    {
                        int triCount = _trimeshIndices.Length / 3;
                        var triangles = new IndexedTriangle[triCount];
                        for (int i = 0; i < triCount; i++)
                        {
                            uint a = _trimeshIndices[i * 3];
                            uint b = _trimeshIndices[i * 3 + 1];
                            uint c = _trimeshIndices[i * 3 + 2];
                            triangles[i] = new IndexedTriangle(a, b, c, 0, 0);
                        }

                        var meshSettings = new MeshShapeSettings(
                            new Span<Vector3>(_trimeshVerts),
                            new Span<IndexedTriangle>(triangles));
                        _shape = meshSettings.Create();
                        if (_shape == null)
                        {
                            Logger.Error("RigidBody.Build MeshShape creation failed");
                            return;
                        }
                    }
                break;
            }

            default:
                return;
        }

        var motionType = _isKinematic ? MotionType.Kinematic : _mass > 0 ? MotionType.Dynamic : MotionType.Static;

        var settings = new BodyCreationSettings(
            _shape!,
            _position,
            _rotation,
            motionType,
            0);

        settings.Friction = _friction;
        settings.Restitution = _restitution;
        settings.AllowSleeping = true;
        settings.MotionQuality = MotionQuality.Discrete;

        if (motionType == MotionType.Dynamic)
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateMassAndInertia;
        }

        _bodyID = world.CreateAndAddBody(settings);
        _built = true;
    }

    public void Destroy()
    {
        _shape?.Dispose();
        _shape = null;
        _bodyID = BodyID.Invalid;
        _built = false;
    }

    public Vector3 Position(PhysicsWorld world)
    {
        if (!_built) return _position;
        return world.GetBodyPosition(_bodyID);
    }

    public Quaternion Rotation(PhysicsWorld world)
    {
        if (!_built) return _rotation;
        return world.GetBodyRotation(_bodyID);
    }

    public Matrix4x4 ModelMatrix(PhysicsWorld world)
    {
        Vector3 p = Position(world);
        Quaternion r = Rotation(world);
        return Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(p);
    }

    public void ApplyCentralForce(PhysicsWorld world, Vector3 force)
    {
        if (_built)
            world.BodyInterface.AddForce(_bodyID, force);
    }

    public void ApplyCentralImpulse(PhysicsWorld world, Vector3 impulse)
    {
        if (_built)
            world.BodyInterface.AddImpulse(_bodyID, impulse);
    }

    public void SetLinearVelocity(PhysicsWorld world, Vector3 velocity)
    {
        if (_built)
            world.BodyInterface.SetLinearVelocity(_bodyID, velocity);
    }

    public Vector3 LinearVelocity(PhysicsWorld world)
    {
        if (!_built) return Vector3.Zero;
        return world.BodyInterface.GetLinearVelocity(_bodyID);
    }

    public BodyID Native => _bodyID;
    public bool IsBuilt => _built;

    public ShapeType Type => _shapeType;
    public Vector3 GetPosition() => _position;
    public Quaternion GetRotation() => _rotation;
    public float Mass => _mass;
    public float Friction => _friction;
    public float Restitution => _restitution;
    public Vector3 BoxHalfExtents => _boxHalfExtents;
    public float SphereRadius => _sphereRadius;
    public float CapsuleRadius => _capsuleRadius;
    public float CapsuleHeight => _capsuleHeight;
    public Vector3 PlaneNormal => _planeNormal;
    public float PlaneDistance => _planeDistance;
    public Vector3[]? TrimeshVertices => _trimeshVerts;
    public uint[]? TrimeshIndices => _trimeshIndices;
}
