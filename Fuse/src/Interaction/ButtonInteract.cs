using Fuse.Core;

namespace Fuse.Interaction;

[InteractableType("ButtonInteract")]
public sealed class ButtonInteract : IInteractable
{
    public void OnInteract() => Logger.Info("BUTTON INTERACT!");
}
