using System.Text.Json.Nodes;

namespace Fuse.Behaviours;

public class BehaviourData
{
    public string Type { get; set; } = "";
    public JsonObject Properties { get; set; } = new JsonObject();
    
    public BehaviourData Clone()
    {
        return new BehaviourData
        {
            Type = Type,
            Properties = Properties != null ? (JsonObject)JsonNode.Parse(Properties.ToJsonString())! : new JsonObject()
        };
    }
}
