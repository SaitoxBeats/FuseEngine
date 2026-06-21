using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Fuse.Physics;

namespace Fuse.Interaction;

public static class InteractionSystem
{
    public static GCHandle RegisterInteractable(BodyInterface bi, BodyID bodyId, IInteractable interactable)
    {
        var gcHandle = GCHandle.Alloc(interactable);
        nint ptr = GCHandle.ToIntPtr(gcHandle);
        bi.SetUserData(bodyId, (ulong)ptr);
        return gcHandle;
    }

    public static IInteractable? GetInteractable(BodyInterface bi, BodyID bodyId, ulong userData)
    {
        if (userData == 0) return null;
        var gcHandle = GCHandle.FromIntPtr((nint)userData);
        return gcHandle.Target as IInteractable;
    }

    public static void UnregisterInteractable(BodyInterface bi, BodyID bodyId, ulong userData)
    {
        if (userData != 0)
        {
            var gcHandle = GCHandle.FromIntPtr((nint)userData);
            gcHandle.Free();
            bi.SetUserData(bodyId, 0);
        }
    }

    public static IInteractable? RaycastInteractable(
        PhysicsWorld world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        using var bpFilter = new Physics.DefaultBroadPhaseLayerFilter();
        using var olFilter = new Physics.DefaultObjectLayerFilter();
        using var bodyFilter = new Physics.DefaultBodyFilter();

        Vector3 dirScaled = direction * maxDistance;
        var ray = new Ray(ref origin, ref dirScaled);

        if (!world.NarrowPhaseQuery.CastRay(ray, out var hit, bpFilter, olFilter, bodyFilter))
            return null;

        if (!hit.BodyID.IsValid) return null;

        ulong userData = world.BodyInterface.GetUserData(hit.BodyID);
        return GetInteractable(world.BodyInterface, hit.BodyID, userData);
    }
}
