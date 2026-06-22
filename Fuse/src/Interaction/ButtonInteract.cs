using Fuse.Core;

namespace Fuse.Interaction;

[InteractableType("ButtonInteract")]
public sealed class ButtonInteract : IInteractable
{
    public Renderer.Entity? Entity { get; set; }
    public Physics.PhysicsWorld? World { get; set; }


    public void OnInteract() => Logger.Info("BUTTON INTERACT!");
    public void Update(float dt) { }
}
