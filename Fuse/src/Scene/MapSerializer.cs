using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuse.Core;
using Fuse.Physics;

namespace Fuse.Scene;

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

    public static string SerializeScene(Renderer.Scene scene, PhysicsWorld physics)
    {
        var j = new JsonObject
        {
            ["version"] = 1,
            ["objects"] = new JsonArray()
        };

        var objects = (JsonArray)j["objects"]!;
        foreach (var e in scene.Entities)
        {
            var obj = new JsonObject
            {
                ["id"] = e.Id,
                ["visible"] = e.Visible
            };

            if (e.MeshKey.Contains('/') || e.MeshKey.Contains('\\'))
            {
                obj["model"] = e.MeshKey;
                if (e.ModelScale != 1.0f)
                    obj["model_scale"] = e.ModelScale;
            }
            else
            {
                obj["mesh"] = e.MeshKey;
            }

            if (!string.IsNullOrEmpty(e.TexturePath))
                obj["texture"] = e.TexturePath;

            if (!string.IsNullOrEmpty(e.InteractableType))
                obj["interactable"] = e.InteractableType;

            if (e.Body != null && e.Body.IsBuilt)
                obj["body"] = SerializeBody(e, physics);

            objects.Add(obj);
        }

        return j.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static List<RigidBody>? DeserializeScene(string json,
        Renderer.Scene scene, PhysicsWorld physics,
        AssetManagement.AssetManager assets,
        string? resPath = null)
    {
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

        var objects = root["objects"]!.AsArray();
        foreach (var objNode in objects)
        {
            if (objNode == null) continue;
            var obj = objNode.AsObject();

            string id = obj.TryGetPropertyValue("id", out var idNode)
                ? (string)idNode! : "unnamed";

            bool isModel = obj.TryGetPropertyValue("model", out var modelNode);
            string meshKey = isModel
                ? (string)modelNode!
                : (obj.TryGetPropertyValue("mesh", out var meshNode)
                    ? (string)meshNode! : "");

            float modelScale = obj.TryGetPropertyValue("model_scale", out var scaleNode)
                ? (float)scaleNode! : 1.0f;

            string texturePath = obj.TryGetPropertyValue("texture", out var texNode)
                ? (string)texNode! : "";

            Renderer.Mesh? mesh = null;
            string modelPath = meshKey;
            if (resPath != null && isModel && !Path.IsPathRooted(meshKey))
                modelPath = Path.GetFullPath(Path.Combine(resPath, meshKey));

            if (isModel)
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

            if (obj.TryGetPropertyValue("interactable", out var interactableNode))
                entity.InteractableType = (string)interactableNode!;

            if (obj.TryGetPropertyValue("body", out var bodyNode))
            {
                var bj = bodyNode!.AsObject();
                bool isTrimesh = bj.TryGetPropertyValue("shape", out var shapeNode)
                    && (string)shapeNode! == "trimesh";

                var body = new RigidBody();
                ConfigureBodyFromJson(body, bj);

                if (isTrimesh && isModel)
                {
                    var model = assets.GetModel(modelPath, modelScale);
                    if (model != null && model.CollVertices.Length > 0)
                        body.SetTrimesh(model.CollVertices, model.CollIndices);
                    else
                        body.SetBox(new Vector3(0.5f));
                }

                body.Build(physics);
                entity.Body = body;
                createdBodies.Add(body);
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
    }

    public static bool SaveToFile(Renderer.Scene scene, PhysicsWorld physics, string filepath)
    {
        string json = SerializeScene(scene, physics);
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
        string? resPath = null)
    {
        if (!File.Exists(filepath))
        {
            Logger.Error($"Failed to load map: {filepath}");
            return null;
        }

        string json = File.ReadAllText(filepath);
        return DeserializeScene(json, scene, physics, assets, resPath);
    }
}
