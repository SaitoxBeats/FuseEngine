using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blowtorch.Model;

public class MapDocument
{
    public int Version { get; set; } = 1;
    public List<MapObject> Objects { get; set; } = [];
    public MapPlayerSpawn? PlayerSpawn { get; set; }

    public static MapDocument? Load(string path)
    {
        if (!File.Exists(path)) return null;

        string json = File.ReadAllText(path);
        return Parse(json);
    }

    public static MapDocument? Parse(string json)
    {
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (rootNode == null) return null;

        var root = rootNode.AsObject();
        var doc = new MapDocument
        {
            Version = root.TryGetPropertyValue("version", out var verNode) ? (int)verNode! : 1
        };

        if (root.TryGetPropertyValue("player_spawn", out var spawnNode))
        {
            var sj = spawnNode!.AsObject();
            doc.PlayerSpawn = new MapPlayerSpawn
            {
                Position = Vec3FromJson(sj["position"]!.AsArray()),
                Yaw = (float)sj["yaw"]!,
                Pitch = (float)sj["pitch"]!
            };
        }

        if (root.TryGetPropertyValue("objects", out var objectsNode))
        {
            foreach (var objNode in objectsNode!.AsArray())
            {
                if (objNode == null) continue;
                doc.Objects.Add(ParseObject(objNode.AsObject()));
            }
        }

        return doc;
    }

    public string Serialize()
    {
        var root = new JsonObject
        {
            ["version"] = Version,
            ["objects"] = new JsonArray()
        };

        if (PlayerSpawn != null)
        {
            root["player_spawn"] = new JsonObject
            {
                ["position"] = Vec3ToJson(PlayerSpawn.Position),
                ["yaw"] = PlayerSpawn.Yaw,
                ["pitch"] = PlayerSpawn.Pitch
            };
        }

        var objects = (JsonArray)root["objects"]!;
        foreach (var obj in Objects)
            objects.Add(SerializeObject(obj));

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static MapObject ParseObject(JsonObject obj)
    {
        var mo = new MapObject
        {
            Id = obj.TryGetPropertyValue("id", out var idNode) ? (string)idNode! : "unnamed",
            Visible = obj.TryGetPropertyValue("visible", out var visNode) ? (bool)visNode! : true,
            Mesh = obj.TryGetPropertyValue("mesh", out var meshNode) ? (string)meshNode! : null,
            Model = obj.TryGetPropertyValue("model", out var modelNode) ? (string)modelNode! : null,
            ModelScale = obj.TryGetPropertyValue("model_scale", out var scaleNode) ? (float)scaleNode! : 1.0f,
            Texture = obj.TryGetPropertyValue("texture", out var texNode) ? (string)texNode! : null,
            Interactable = obj.TryGetPropertyValue("interactable", out var interactNode) ? (string)interactNode! : null,
        };

        if (obj.TryGetPropertyValue("body", out var bodyNode))
            mo.Body = ParseBody(bodyNode!.AsObject());

        return mo;
    }

    private static MapBody ParseBody(JsonObject bj)
    {
        var body = new MapBody
        {
            Shape = ShapeFromString(bj.TryGetPropertyValue("shape", out var sNode) ? (string)sNode! : "none"),
            Mass = bj.TryGetPropertyValue("mass", out var mNode) ? (float)mNode! : 0,
            Friction = bj.TryGetPropertyValue("friction", out var fNode) ? (float)fNode! : 0.5f,
            Restitution = bj.TryGetPropertyValue("restitution", out var rNode) ? (float)rNode! : 0.3f,
        };

        if (bj.TryGetPropertyValue("position", out var posNode))
            body.Position = Vec3FromJson(posNode!.AsArray());
        if (bj.TryGetPropertyValue("rotation", out var rotNode))
            body.Rotation = QuatFromJson(rotNode!.AsArray());

        switch (body.Shape)
        {
            case MapShapeType.Box:
                if (bj.TryGetPropertyValue("half_extents", out var heNode))
                    body.HalfExtents = Vec3FromJson(heNode!.AsArray());
                break;
            case MapShapeType.Sphere:
                if (bj.TryGetPropertyValue("radius", out var radNode))
                    body.Radius = (float)radNode!;
                break;
            case MapShapeType.Capsule:
                if (bj.TryGetPropertyValue("radius", out var capRadNode))
                    body.Radius = (float)capRadNode!;
                if (bj.TryGetPropertyValue("height", out var hNode))
                    body.Height = (float)hNode!;
                break;
            case MapShapeType.Plane:
                if (bj.TryGetPropertyValue("normal", out var nNode))
                    body.Normal = Vec3FromJson(nNode!.AsArray());
                if (bj.TryGetPropertyValue("distance", out var dNode))
                    body.Distance = (float)dNode!;
                break;
        }

        return body;
    }

    private static JsonObject SerializeObject(MapObject obj)
    {
        var j = new JsonObject
        {
            ["id"] = obj.Id,
            ["visible"] = obj.Visible
        };

        if (obj.IsModel)
        {
            j["model"] = obj.Model!;
            if (obj.ModelScale != 1.0f)
                j["model_scale"] = obj.ModelScale;
        }
        else if (obj.Mesh != null)
        {
            j["mesh"] = obj.Mesh;
        }

        if (!string.IsNullOrEmpty(obj.Texture))
            j["texture"] = obj.Texture;
        if (!string.IsNullOrEmpty(obj.Interactable))
            j["interactable"] = obj.Interactable;

        if (obj.Body != null)
            j["body"] = SerializeBody(obj.Body);

        return j;
    }

    private static JsonObject SerializeBody(MapBody body)
    {
        var bj = new JsonObject
        {
            ["shape"] = ShapeToString(body.Shape),
            ["position"] = Vec3ToJson(body.Position),
            ["rotation"] = QuatToJson(body.Rotation),
            ["mass"] = body.Mass,
            ["friction"] = body.Friction,
            ["restitution"] = body.Restitution
        };

        switch (body.Shape)
        {
            case MapShapeType.Box:
                if (body.HalfExtents.HasValue)
                    bj["half_extents"] = Vec3ToJson(body.HalfExtents.Value);
                break;
            case MapShapeType.Sphere:
                if (body.Radius.HasValue)
                    bj["radius"] = body.Radius.Value;
                break;
            case MapShapeType.Capsule:
                if (body.Radius.HasValue)
                    bj["radius"] = body.Radius.Value;
                if (body.Height.HasValue)
                    bj["height"] = body.Height.Value;
                break;
            case MapShapeType.Plane:
                if (body.Normal.HasValue)
                    bj["normal"] = Vec3ToJson(body.Normal.Value);
                if (body.Distance.HasValue)
                    bj["distance"] = body.Distance.Value;
                break;
        }

        return bj;
    }

    private static Vector3 Vec3FromJson(JsonArray arr) => new(
        (float)arr[0]!, (float)arr[1]!, (float)arr[2]!);

    private static Quaternion QuatFromJson(JsonArray arr) => new(
        (float)arr[1]!, (float)arr[2]!, (float)arr[3]!, (float)arr[0]!);

    private static JsonArray Vec3ToJson(Vector3 v) => new(v.X, v.Y, v.Z);
    private static JsonArray QuatToJson(Quaternion q) => new(q.W, q.X, q.Y, q.Z);

    private static MapShapeType ShapeFromString(string s) => s switch
    {
        "box" => MapShapeType.Box,
        "sphere" => MapShapeType.Sphere,
        "capsule" => MapShapeType.Capsule,
        "plane" => MapShapeType.Plane,
        "trimesh" => MapShapeType.Trimesh,
        _ => MapShapeType.None
    };

    private static string ShapeToString(MapShapeType t) => t switch
    {
        MapShapeType.Box => "box",
        MapShapeType.Sphere => "sphere",
        MapShapeType.Capsule => "capsule",
        MapShapeType.Plane => "plane",
        MapShapeType.Trimesh => "trimesh",
        _ => "none"
    };
}