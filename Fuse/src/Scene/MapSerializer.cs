using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuse.Core;
using Fuse.Physics;
using Fuse.Scene.Model;

namespace Fuse.Scene;

public record struct PlayerSpawn(Vector3 Position, float Yaw, float Pitch);

public static class MapSerializer
{
    private static Vector3 Vec3FromJson(JsonArray arr)
    {
        return new Vector3(
            (float)arr[0]!,
            (float)arr[1]!,
            (float)arr[2]!);
    }

    private static Quaternion QuatFromJson(JsonArray arr)
    {
        return new Quaternion(
            (float)arr[1]!,
            (float)arr[2]!,
            (float)arr[3]!,
            (float)arr[0]!);
    }

    private static JsonArray Vec3ToJson(Vector3 v) => new(v.X, v.Y, v.Z);
    private static JsonArray QuatToJson(Quaternion q) => new(q.W, q.X, q.Y, q.Z);

    private static string ShapeTypeToString(RigidBody.ShapeType t) => t switch
    {
        RigidBody.ShapeType.Box => "box",
        RigidBody.ShapeType.Plane => "plane",
        RigidBody.ShapeType.Sphere => "sphere",
        RigidBody.ShapeType.Capsule => "capsule",
        RigidBody.ShapeType.Trimesh => "trimesh",
        _ => "none"
    };

    private static RigidBody.ShapeType ShapeTypeFromString(string s) => s switch
    {
        "box" => RigidBody.ShapeType.Box,
        "plane" => RigidBody.ShapeType.Plane,
        "sphere" => RigidBody.ShapeType.Sphere,
        "capsule" => RigidBody.ShapeType.Capsule,
        "trimesh" => RigidBody.ShapeType.Trimesh,
        _ => RigidBody.ShapeType.None
    };

    private static JsonObject SerializeBody(Renderer.Entity e, PhysicsWorld physics)
    {
        var bj = new JsonObject();
        if (e.Body == null || !e.Body.IsBuilt) return bj;

        var pos = e.Body.Position(physics);
        var rot = e.Body.Rotation(physics);

        bj["shape"] = ShapeTypeToString(e.Body.Type);
        bj["position"] = Vec3ToJson(pos);
        bj["rotation"] = QuatToJson(rot);
        bj["mass"] = e.Body.Mass;
        bj["friction"] = e.Body.Friction;
        bj["restitution"] = e.Body.Restitution;
        bj["is_trigger"] = e.Body.IsTrigger;

        switch (e.Body.Type)
        {
            case RigidBody.ShapeType.Box:
                bj["half_extents"] = Vec3ToJson(e.Body.BoxHalfExtents);
                break;
            case RigidBody.ShapeType.Sphere:
                bj["radius"] = e.Body.SphereRadius;
                break;
            case RigidBody.ShapeType.Capsule:
                bj["radius"] = e.Body.CapsuleRadius;
                bj["height"] = e.Body.CapsuleHeight;
                break;
            case RigidBody.ShapeType.Plane:
                bj["normal"] = Vec3ToJson(e.Body.PlaneNormal);
                bj["distance"] = e.Body.PlaneDistance;
                break;
        }

        return bj;
    }

    public static string SerializeScene(Renderer.Scene scene, PhysicsWorld physics,
        PlayerSpawn? playerSpawn = null)
    {
        var j = new JsonObject
        {
            ["version"] = 1,
            ["objects"] = new JsonArray()
        };

        if (playerSpawn.HasValue)
        {
            var ps = playerSpawn.Value;
            j["player_spawn"] = new JsonObject
            {
                ["position"] = Vec3ToJson(ps.Position),
                ["yaw"] = ps.Yaw,
                ["pitch"] = ps.Pitch
            };
        }

        var objects = (JsonArray)j["objects"]!;
        foreach (var e in scene.Entities)
        {
            var obj = new JsonObject
            {
                ["id"] = e.Id,
                ["visible"] = e.Visible
            };

            if (!string.IsNullOrEmpty(e.ParentId))
                obj["parent"] = e.ParentId;

            if (e.MeshKey.Contains('/') || e.MeshKey.Contains('\\'))
            {
                obj["model"] = e.MeshKey;
                if (e.ModelScale != 1.0f)
                    obj["model_scale"] = e.ModelScale;
            }
            else
            {
                obj["mesh"] = e.MeshKey;
                if (e.UvScale != Vector2.One)
                    obj["uv_scale"] = new JsonArray(e.UvScale.X, e.UvScale.Y);
            }

            if (!string.IsNullOrEmpty(e.TexturePath))
                obj["texture"] = e.TexturePath;

            if (!string.IsNullOrEmpty(e.InteractableType))
                obj["interactable"] = e.InteractableType;

            if (!string.IsNullOrEmpty(e.BehaviourType))
                obj["behaviour"] = e.BehaviourType;

            if (e.Body != null && e.Body.IsBuilt)
                obj["body"] = SerializeBody(e, physics);

            objects.Add(obj);
        }

        return j.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static List<RigidBody>? DeserializeScene(string json,
        Renderer.Scene scene, PhysicsWorld physics,
        AssetManagement.AssetManager assets,
        out PlayerSpawn? playerSpawn,
        string? resPath = null)
    {
        playerSpawn = null;
        scene.Clear();
        var createdBodies = new List<RigidBody>();

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            Logger.Error($"Map parse error: {ex.Message}");
            return null;
        }

        if (rootNode == null)
        {
            Logger.Error("Map parse error: empty JSON");
            return null;
        }

        var root = rootNode.AsObject();

        int version = root.TryGetPropertyValue("version", out var verNode)
            ? (int)verNode! : 0;
        if (version != 1)
        {
            Logger.Error($"Unknown map version: {version}");
            return null;
        }

        if (root.TryGetPropertyValue("player_spawn", out var spawnNode))
        {
            var sj = spawnNode!.AsObject();
            playerSpawn = new PlayerSpawn(
                Vec3FromJson(sj["position"]!.AsArray()),
                (float)sj["yaw"]!,
                (float)sj["pitch"]!);
        }

        var parentMap = new Dictionary<string, string>();
        var visibleMap = new Dictionary<string, bool>();
        var objects = root["objects"]!.AsArray();
        foreach (var objNode in objects)
        {
            if (objNode == null) continue;
            var obj = objNode.AsObject();
            string id = obj.TryGetPropertyValue("id", out var idNode) ? (string)idNode! : "unnamed";
            bool visible = obj.TryGetPropertyValue("visible", out var visNode) ? (bool)visNode! : true;
            string? parent = obj.TryGetPropertyValue("parent", out var pNode) ? (string)pNode! : null;
            visibleMap[id] = visible;
            if (parent != null) parentMap[id] = parent;
        }

        bool IsGloballyVisible(string id)
        {
            if (!visibleMap.TryGetValue(id, out bool vis) || !vis) return false;
            if (parentMap.TryGetValue(id, out string parentId))
            {
                return IsGloballyVisible(parentId);
            }
            return true;
        }

        foreach (var objNode in objects)
        {
            if (objNode == null) continue;
            var obj = objNode.AsObject();

            string id = obj.TryGetPropertyValue("id", out var idNode)
                ? (string)idNode! : "unnamed";

            bool isModel = obj.TryGetPropertyValue("model", out var modelNode);
            bool isBrush = obj.TryGetPropertyValue("type", out var typeNode) && (string)typeNode! == "brush";
            string meshKey = isModel
                ? (string)modelNode!
                : (obj.TryGetPropertyValue("mesh", out var meshNode)
                    ? (string)meshNode! : "");

            float modelScale = obj.TryGetPropertyValue("model_scale", out var scaleNode)
                ? (float)scaleNode! : 1.0f;

            Vector2 uvScale = Vector2.One;
            if (obj.TryGetPropertyValue("uv_scale", out var uvNode))
            {
                var arr = uvNode!.AsArray();
                uvScale = new Vector2((float)arr[0]!, (float)arr[1]!);
            }

            string texturePath = obj.TryGetPropertyValue("texture", out var texNode)
                ? (string)texNode! : "";

            Renderer.Mesh? mesh = null;
            string modelPath = meshKey;
            if (resPath != null && isModel && !Path.IsPathRooted(meshKey))
                modelPath = Path.GetFullPath(Path.Combine(resPath, meshKey));

            if (isBrush)
            {
                var brushObj = (Brush)MapDocument.ParseObject(obj);
                var meshData = MeshGenerator.Generate(brushObj);
                mesh = new Renderer.Mesh(assets.Gl, meshData.Vertices, meshData.Indices);
            }
            else if (isModel)
            {
                var model = assets.GetModel(modelPath, modelScale);
                if (model != null) mesh = model.Mesh;
            }
            else
            {
                mesh = assets.GetMesh(meshKey);
            }

            if (mesh == null)
            {
                Logger.Warn($"Map load: unknown mesh '{meshKey}' for '{id}'");
                continue;
            }

            var entity = scene.Add(mesh, id);
            entity.MeshKey = meshKey;
            entity.TexturePath = texturePath;
            entity.ModelScale = modelScale;
            entity.UvScale = uvScale;
            entity.Visible = IsGloballyVisible(id);
            entity.ParentId = obj.TryGetPropertyValue("parent", out var pIdNode) ? (string)pIdNode! : "";

            if (!string.IsNullOrEmpty(texturePath))
            {
                string texPath = texturePath;
                if (texPath.StartsWith("res/"))
                {
                    texPath = texPath.Substring(4);
                }
                if (resPath != null && !Path.IsPathRooted(texPath))
                {
                    texPath = Path.GetFullPath(Path.Combine(resPath, texPath));
                }
                
                if (File.Exists(texPath))
                {
                    try
                    {
                        entity.Texture = assets.GetTexture(texPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Map load: failed to load texture '{texturePath}' for '{id}' - {ex.Message}");
                    }
                }
                else
                {
                    Logger.Warn($"Map load: texture file not found '{texPath}' for '{id}' - using fallback");
                    entity.Texture = null;
                }
            }

            if (obj.TryGetPropertyValue("interactable", out var interactableNode))
                entity.InteractableType = (string)interactableNode!;

            if (obj.TryGetPropertyValue("behaviour", out var behaviourNode))
                entity.BehaviourType = (string)behaviourNode!;

            if (entity.Visible && obj.TryGetPropertyValue("body", out var bodyNode))
            {
                var bj = bodyNode!.AsObject();
                bool isTrimesh = bj.TryGetPropertyValue("shape", out var shapeNode)
                    && (string)shapeNode! == "trimesh";

                var body = new RigidBody();
                ConfigureBodyFromJson(body, bj);

                if (body.IsTrigger)
                    entity.Visible = false;

                if (!string.IsNullOrEmpty(entity.BehaviourType))
                    body.SetKinematic(true);

                if (isTrimesh && isModel)
                {
                    var model = assets.GetModel(modelPath, modelScale);
                    if (model != null && model.CollVertices.Length > 0)
                        body.SetTrimesh(model.CollVertices, model.CollIndices, new Vector3(modelScale));
                    else
                        body.SetBox(new Vector3(0.5f));
                }

                body.Build(physics);
                entity.Body = body;
                scene.RegisterBody(entity);
                
                if (isBrush)
                    entity.Transform.Scale = Vector3.One;
                else if (body.Type == RigidBody.ShapeType.Box)
                    entity.Transform.Scale = body.BoxHalfExtents * 2.0f;
                else if (body.Type == RigidBody.ShapeType.Sphere)
                    entity.Transform.Scale = new Vector3(body.SphereRadius * 2.0f);
                else
                    entity.Transform.Scale = new Vector3(modelScale);
                    
                createdBodies.Add(body);
            }
            else
            {
                entity.Transform.Scale = isBrush ? Vector3.One : new Vector3(modelScale);
            }
        }

        // Compute initial relative transforms for children
        foreach (var entity in scene.Entities)
        {
            if (!string.IsNullOrEmpty(entity.ParentId))
            {
                var parent = scene.Entities.FirstOrDefault(e => e.Id == entity.ParentId);
                if (parent != null)
                {
                    entity.InitialRelativePosition = entity.Transform.Position - parent.Transform.Position;
                    entity.InitialRelativeRotation = Quaternion.Inverse(parent.Transform.Rotation) * entity.Transform.Rotation;
                }
            }
        }

        Logger.Info($"Map loaded ({scene.Entities.Count} entities)");
        return createdBodies;
    }

    private static void ConfigureBodyFromJson(RigidBody body, JsonObject bj)
    {
        var shape = bj.TryGetPropertyValue("shape", out var shapeToken)
            ? ShapeTypeFromString((string)shapeToken!)
            : RigidBody.ShapeType.None;

        switch (shape)
        {
            case RigidBody.ShapeType.Box:
                body.SetBox(Vec3FromJson(bj["half_extents"]!.AsArray()));
                break;
            case RigidBody.ShapeType.Sphere:
                body.SetSphere((float)bj["radius"]!);
                break;
            case RigidBody.ShapeType.Capsule:
                body.SetCapsule((float)bj["radius"]!, (float)bj["height"]!);
                break;
            case RigidBody.ShapeType.Plane:
                body.SetPlane(
                    Vec3FromJson(bj["normal"]!.AsArray()),
                    (float)bj["distance"]!);
                break;
        }

        if (bj.TryGetPropertyValue("position", out var posToken))
            body.SetPosition(Vec3FromJson(posToken!.AsArray()));
        if (bj.TryGetPropertyValue("rotation", out var rotToken))
            body.SetRotation(QuatFromJson(rotToken!.AsArray()));
        if (bj.TryGetPropertyValue("mass", out var massToken))
            body.SetMass((float)massToken!);
        if (bj.TryGetPropertyValue("friction", out var frictionToken))
            body.SetFriction((float)frictionToken!);
        if (bj.TryGetPropertyValue("restitution", out var restToken))
            body.SetRestitution((float)restToken!);
        if (bj.TryGetPropertyValue("is_trigger", out var triggerToken))
            body.SetTrigger((bool)triggerToken!);
    }

    public static bool SaveToFile(Renderer.Scene scene, PhysicsWorld physics, string filepath,
        PlayerSpawn? playerSpawn = null)
    {
        string json = SerializeScene(scene, physics, playerSpawn);
        try
        {
            File.WriteAllText(filepath, json);
            Logger.Info($"Map saved: {filepath} ({scene.Entities.Count} entities)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save map: {filepath} - {ex.Message}");
            return false;
        }
    }

    public static List<RigidBody>? LoadFromFile(string filepath,
        Renderer.Scene scene, PhysicsWorld physics,
        AssetManagement.AssetManager assets,
        out PlayerSpawn? playerSpawn,
        string? resPath = null)
    {
        playerSpawn = null;
        if (!File.Exists(filepath))
        {
            Logger.Error($"Failed to load map: {filepath}");
            return null;
        }

        string json = File.ReadAllText(filepath);
        return DeserializeScene(json, scene, physics, assets, out playerSpawn, resPath);
    }
}
