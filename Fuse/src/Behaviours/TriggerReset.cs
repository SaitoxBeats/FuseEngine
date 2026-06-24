using Fuse.Core;
using Fuse.Interaction;

namespace Fuse.Behaviours;

[InteractableType("TrReset")]
public sealed class TriggerReset : IBehaviour
{
    public Renderer.Entity? Entity { get; set; }
    public Physics.PhysicsWorld? World { get; set; }
    public bool PendingReset;

    public void Update(float dt) { }

    public void OnTriggerEnter(string otherEntityId)
    {
        if (otherEntityId == "player")
        {
            PendingReset = true;
            Logger.Info("RESET MAP");
        }
    }

    public void OnTriggerExit(string otherEntityId) { }
}