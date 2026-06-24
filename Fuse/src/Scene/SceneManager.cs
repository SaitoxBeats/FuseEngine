using System.Numerics;
using System.Runtime.InteropServices;
using Fuse.Behaviours;
using Fuse.Core;
using Fuse.Interaction;
using Fuse.Physics;
using Fuse.Renderer;

namespace Fuse.Scene;

public class SceneManager
{
    private readonly Renderer.Scene _scene;
    private readonly PhysicsWorld _physics;
    private readonly AssetManagement.AssetManager _assets;

    public string CurrentMapPath { get; private set; } = null!;
    public Renderer.Scene ActiveScene => _scene;

    private readonly List<RigidBody> _bodies = [];
    private readonly List<IInteractable> _interactables = [];
    private readonly Dictionary<JoltPhysicsSharp.BodyID, GCHandle> _interactableHandles = [];
    private readonly List<IBehaviour> _behaviours = [];
    private TriggerSystem _triggerSystem = null!;

    public SceneManager(PhysicsWorld physics, AssetManagement.AssetManager assets)
    {
        _scene = new Renderer.Scene();
        _physics = physics;
        _assets = assets;
    }

    public void InitTriggerSystem(Player.Player player)
    {
        _triggerSystem = new TriggerSystem(
            player.NativeCharacter,
            _behaviours,
            id => _scene.GetEntityByBody(id),
            player.GetBodyLockInterface()
        );
    }

    public PlayerSpawn? LoadMap(string fileName)
    {
        string loadPath = $"{Fuse.ResPath.Path}/Maps/{fileName}";
        if (!File.Exists(loadPath))
        {
            Logger.Error($"Map not found: {loadPath}");
            return null;
        }

        ClearCurrentMap();

        var loaded = MapSerializer.LoadFromFile(
            loadPath, _scene, _physics, _assets, out var spawn, Fuse.ResPath.Path);

        if (loaded != null)
        {
            _bodies.AddRange(loaded);
        }

        RegisterInteractablesAndBehaviours();
        CurrentMapPath = loadPath;
        Logger.Info($"Map loaded: {fileName}");
        
        return spawn;
    }

    public PlayerSpawn? ReloadMap()
    {
        if (string.IsNullOrEmpty(CurrentMapPath)) return null;

        ClearCurrentMap();

        var loaded = MapSerializer.LoadFromFile(
            CurrentMapPath, _scene, _physics, _assets, out var spawn, Fuse.ResPath.Path);
            
        if (loaded != null)
        {
            _bodies.AddRange(loaded);
        }

        RegisterInteractablesAndBehaviours();
        return spawn;
    }

    private void ClearCurrentMap()
    {
        _interactables.Clear();
        _behaviours.Clear();

        foreach (var b in _bodies)
        {
            if (b.IsBuilt)
                _physics.DestroyBody(b.Native);
        }
        _bodies.Clear();

        foreach (var handle in _interactableHandles.Values)
            handle.Free();
        _interactableHandles.Clear();
        
        _scene.Clear();
    }

    private void RegisterInteractablesAndBehaviours()
    {
        foreach (var entity in _scene.Entities)
        {
            if (entity.Body != null)
                _scene.RegisterBody(entity);
        }

        foreach (var entity in _scene.Entities)
        {
            if (entity.Body != null && entity.Body.IsBuilt && !string.IsNullOrEmpty(entity.InteractableType))
            {
                var interactable = InteractionSystem.CreateInteractable(entity.InteractableType);
                if (interactable != null)
                {
                    interactable.Entity = entity;
                    interactable.World = _physics;
                    _interactables.Add(interactable);
                    var gcHandle = GCHandle.Alloc(interactable);
                    _interactableHandles[entity.Body.Native] = gcHandle;
                    _physics.BodyInterface.SetUserData(entity.Body.Native, (ulong)GCHandle.ToIntPtr(gcHandle));
                }
            }
        }

        foreach (var entity in _scene.Entities)
        {
            if (entity.Body != null && entity.Body.IsBuilt && !string.IsNullOrEmpty(entity.BehaviourType))
            {
                var behaviour = BehaviourSystem.Create(entity.BehaviourType);
                if (behaviour != null)
                {
                    behaviour.Entity = entity;
                    behaviour.World = _physics;
                    _behaviours.Add(behaviour);
                }
            }
        }
    }

    public void Update(float dt)
    {
        foreach (var interactable in _interactables)
            interactable.Update(dt);
            
        foreach (var behaviour in _behaviours)
            behaviour.Update(dt);
            
        if (_triggerSystem != null)
            _triggerSystem.Update(dt);
    }
    
    public bool CheckPendingResets()
    {
        foreach (var behaviour in _behaviours)
        {
            if (behaviour is TriggerReset reset && reset.PendingReset)
            {
                reset.PendingReset = false;
                return true;
            }
        }
        return false;
    }

    public void DrawDebug(Debug.DebugDrawer debugDrawer)
    {
        foreach (var b in _bodies)
        {
            if (!b.IsBuilt) continue;

            var pos = b.Position(_physics);
            var rot = b.Rotation(_physics);
            var color = b.Mass > 0 ? new Vector3(1, 1, 0) : new Vector3(1, 0, 0);

            switch (b.Type)
            {
                case RigidBody.ShapeType.Box:
                    debugDrawer.DrawBox(pos, rot, b.BoxHalfExtents, color);
                    break;
                case RigidBody.ShapeType.Sphere:
                    debugDrawer.DrawSphere(pos, rot, b.SphereRadius, color);
                    break;
                case RigidBody.ShapeType.Capsule:
                    debugDrawer.DrawCapsule(pos, rot, b.CapsuleHeight * 0.5f, b.CapsuleRadius, color);
                    break;
                case RigidBody.ShapeType.Trimesh:
                    if (b.TrimeshVertices != null && b.TrimeshIndices != null)
                        debugDrawer.DrawTrimesh(pos, rot, b.TrimeshVertices, b.TrimeshIndices, color);
                    break;
            }
        }
    }

    public void Dispose()
    {
        ClearCurrentMap();
    }
}
