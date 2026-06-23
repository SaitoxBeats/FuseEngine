namespace Fuse.Behaviours;

public interface IBehaviour
{
    void Update(float dt);
    Renderer.Entity? Entity { get; set; }
    Physics.PhysicsWorld? World { get; set; }

    void OnTriggerEnter(string otherEntityId) { }
    void OnTriggerExit(string otherEntityId) { }
}