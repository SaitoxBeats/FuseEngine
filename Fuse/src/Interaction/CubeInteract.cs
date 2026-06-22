using Fuse.Core;

namespace Fuse.Interaction;

[InteractableType("CubeInteract")]
public sealed class CubeInteract : IInteractable
{
    public Renderer.Entity? Entity { get; set; }
    public Physics.PhysicsWorld? World { get; set; }


    public void OnInteract() => Logger.Info("CUBE INTERACT!");
    public void Update(float dt) { }
}
