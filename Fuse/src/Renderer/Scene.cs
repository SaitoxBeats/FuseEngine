using System.Numerics;
using Fuse.Physics;

namespace Fuse.Renderer;

public class Transform
{
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);
}

public class Entity
{
    public string Id { get; set; } = "";
    public string MeshKey { get; set; } = "";
    public string TexturePath { get; set; } = "";
    public string InteractableType { get; set; } = "";
    public float ModelScale { get; set; } = 1.0f;
    public Vector2 UvScale { get; set; } = Vector2.One;
    public Mesh? Mesh { get; set; }
    public RigidBody? Body { get; set; }
    public Transform Transform { get; set; } = new();
    public bool Visible { get; set; } = true;
}

public class Scene
{
    private readonly List<Entity> _entities = [];

    public Entity Add(Mesh mesh, string id, RigidBody? body = null)
    {
        var entity = new Entity
        {
            Id = id,
            MeshKey = id,
            Mesh = mesh,
            Body = body,
        };
        _entities.Add(entity);
        return entity;
    }

    public void Clear()
    {
        _entities.Clear();
    }

    public IReadOnlyList<Entity> Entities => _entities;

    public void Render(Shader shader, PhysicsWorld world)
    {
        foreach (var e in _entities)
        {
            if (!e.Visible || e.Mesh == null) continue;

            if (e.Body != null && e.Body.IsBuilt)
            {
                e.Transform.Position = e.Body.Position(world);
                e.Transform.Rotation = e.Body.Rotation(world);
            }

            shader.SetMat4("uModel", e.Transform.Matrix);
            shader.SetVec2("uUvScale", e.UvScale);
            e.Mesh.Draw();
        }
    }
}
