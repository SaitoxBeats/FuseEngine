using Fuse.Core;

namespace Fuse.Interaction;

[InteractableType("CubeInteract")]
public sealed class CubeInteract : IInteractable
{
    public void OnInteract() => Logger.Info("CUBE INTERACT!");
}
