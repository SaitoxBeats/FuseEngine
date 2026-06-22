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
    public Texture? Texture { get; set; }
    public RigidBody? Body { get; set; }
    public Transform Transform { get; set; } = new();
    public bool Visible { get; set; } = true;
    public string ParentId { get; set; } = "";
    public Vector3 InitialRelativePosition { get; set; }
    public Quaternion InitialRelativeRotation { get; set; } = Quaternion.Identity;
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

    public void Render(Shader shader, PhysicsWorld world, Texture defaultTexture)
    {
        // 1. Update all physics-driven parent positions
        foreach (var e in _entities)
        {
            if (e.Body != null && e.Body.IsBuilt)
            {
                e.Transform.Position = e.Body.Position(world);
                e.Transform.Rotation = e.Body.Rotation(world);
            }
        }

        // 2. Resolve world transforms for parent-child hierarchies
        var worldPositions = new Dictionary<string, Vector3>();
        var worldRotations = new Dictionary<string, Quaternion>();

        bool HasPhysicsAncestor(Entity entity)
        {
            string pId = entity.ParentId;
            while (!string.IsNullOrEmpty(pId))
            {
                var parent = _entities.FirstOrDefault(p => p.Id == pId);
                if (parent == null) break;
                if (parent.Body != null && parent.Body.IsBuilt) return true;
                pId = parent.ParentId;
            }
            return false;
        }

        Vector3 GetWorldPosition(Entity e)
        {
            if (worldPositions.TryGetValue(e.Id, out var pos)) return pos;
            if (string.IsNullOrEmpty(e.ParentId))
            {
                worldPositions[e.Id] = e.Transform.Position;
                return e.Transform.Position;
            }
            var parent = _entities.FirstOrDefault(p => p.Id == e.ParentId);
            if (parent == null)
            {
                worldPositions[e.Id] = e.Transform.Position;
                return e.Transform.Position;
            }

            if (e.Body != null && e.Body.IsBuilt && HasPhysicsAncestor(e))
            {
                worldPositions[e.Id] = e.Transform.Position;
                return e.Transform.Position;
            }

            Vector3 wPos = GetWorldPosition(parent) + Vector3.Transform(e.InitialRelativePosition, GetWorldRotation(parent));
            worldPositions[e.Id] = wPos;
            return wPos;
        }

        Quaternion GetWorldRotation(Entity e)
        {
            if (worldRotations.TryGetValue(e.Id, out var rot)) return rot;
            if (string.IsNullOrEmpty(e.ParentId))
            {
                worldRotations[e.Id] = e.Transform.Rotation;
                return e.Transform.Rotation;
            }
            var parent = _entities.FirstOrDefault(p => p.Id == e.ParentId);
            if (parent == null)
            {
                worldRotations[e.Id] = e.Transform.Rotation;
                return e.Transform.Rotation;
            }

            if (e.Body != null && e.Body.IsBuilt && HasPhysicsAncestor(e))
            {
                worldRotations[e.Id] = e.Transform.Rotation;
                return e.Transform.Rotation;
            }

            Quaternion wRot = GetWorldRotation(parent) * e.InitialRelativeRotation;
            worldRotations[e.Id] = wRot;
            return wRot;
        }

        foreach (var e in _entities)
        {
            if (!string.IsNullOrEmpty(e.ParentId))
            {
                bool conflict = e.Body != null && e.Body.IsBuilt && HasPhysicsAncestor(e);
                if (!conflict)
                {
                    e.Transform.Position = GetWorldPosition(e);
                    e.Transform.Rotation = GetWorldRotation(e);
                    if (e.Body != null && e.Body.IsBuilt)
                    {
                        world.SetBodyPositionAndRotation(e.Body.Native, e.Transform.Position, e.Transform.Rotation);
                    }
                }
            }
        }

        // 3. Render all entities
        foreach (var e in _entities)
        {
            if (!e.Visible || e.Mesh == null) continue;

            shader.SetMat4("uModel", e.Transform.Matrix);
            shader.SetVec2("uUvScale", e.UvScale);

            var tex = e.Texture ?? defaultTexture;
            if (tex != null)
            {
                shader.SetBool("uUseTexture", true);
                tex.Bind(0);
            }
            else
            {
                shader.SetBool("uUseTexture", false);
            }

            e.Mesh.Draw();
        }
    }
}
