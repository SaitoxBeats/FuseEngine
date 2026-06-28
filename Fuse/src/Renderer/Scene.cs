using System.Numerics;
using Fuse.Physics;
using JoltPhysicsSharp;

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
    public string BehaviourType { get; set; } = "";
    public System.Numerics.Vector3 ModelScale { get; set; } = System.Numerics.Vector3.One;
    public Vector2 UvScale { get; set; } = Vector2.One;
    public Vector2 UvOffset { get; set; } = Vector2.Zero;
    public float UvRotation { get; set; } = 0f;
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
    private readonly List<Light> _lights = [];
    private readonly Dictionary<BodyID, Entity> _bodyEntityMap = [];

    public IReadOnlyList<Light> Lights => _lights;

    public Light AddLight(Light light)
    {
        _lights.Add(light);
        return light;
    }

    public void RemoveLight(Light light) => _lights.Remove(light);

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
        if (body != null)
            _bodyEntityMap[body.Native] = entity;
        return entity;
    }

    public void Clear()
    {
        _bodyEntityMap.Clear();
        _entities.Clear();
        _lights.Clear();
    }

    public IReadOnlyList<Entity> Entities => _entities;

    public void RegisterBody(Entity entity)
    {
        if (entity.Body != null)
            _bodyEntityMap[entity.Body.Native] = entity;
    }

    public Entity? GetEntityByBody(BodyID bodyId)
    {
        _bodyEntityMap.TryGetValue(bodyId, out var entity);
        return entity;
    }

    public void Render(Shader shader, PhysicsWorld world, Texture defaultTexture, Matrix4x4? cullMatrix = null)
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

            if (cullMatrix.HasValue)
            {
                Vector3 worldPos = e.Transform.Position;
                Vector4 ndcPos = Vector4.Transform(new Vector4(worldPos, 1.0f), cullMatrix.Value);
                
                if (ndcPos.W != 0)
                {
                    ndcPos.X /= ndcPos.W;
                    ndcPos.Y /= ndcPos.W;
                    ndcPos.Z /= ndcPos.W;
                }
                
                // Conservatively estimate object radius
                float radius = MathF.Max(e.Transform.Scale.X, MathF.Max(e.Transform.Scale.Y, e.Transform.Scale.Z)) * 2.0f;
                
                // Extremely safe minimum radius since we don't have precise AABBs for meshes
                // 30.0 units guarantees that large objects like grounds or walls won't vanish easily
                radius = MathF.Max(radius, 40.0f); 
                
                // Convert world radius to NDC padding using the matrix's scale components
                float pX = MathF.Abs(radius * cullMatrix.Value.M11);
                float pY = MathF.Abs(radius * cullMatrix.Value.M22);

                // Ignore Z culling entirely! Objects far behind the camera can still cast long shadows into our view.
                // If fully outside the X and Y bounds of the light's view, ignore it!
                if (ndcPos.X > 1.0f + pX || ndcPos.X < -1.0f - pX ||
                    ndcPos.Y > 1.0f + pY || ndcPos.Y < -1.0f - pY)
                {
                    continue; // Skip rendering!
                }
            }

            shader.SetMat4("uModel", e.Transform.Matrix);
            shader.SetVec2("uUvScale", e.UvScale);
            shader.SetVec2("uUvOffset", e.UvOffset);
            shader.SetFloat("uUvRotation", e.UvRotation);

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

    public bool IsEntityTrigger(string id)
    {
        var entity = _entities.FirstOrDefault(e => e.Id == id);
        return entity?.Body?.IsTrigger ?? false;
    }
}
