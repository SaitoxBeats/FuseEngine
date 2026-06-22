namespace Fuse.Interaction;

public interface IInteractable
{
    void OnInteract();
    void Update(float dt);
    Renderer.Entity? Entity { get; set; }
    Physics.PhysicsWorld? World { get; set; }
}
