using System.Reflection;
using Fuse.Interaction;

namespace Fuse.Behaviours;

public static class BehaviourSystem
{
    private static Dictionary<string, Type> _behaviourTypes = [];

    static BehaviourSystem()
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            var attr = type.GetCustomAttribute<BehaviourAttribute>();
            if (attr != null && typeof(IBehaviour).IsAssignableFrom(type))
                _behaviourTypes[attr.TypeName] = type;
        }
    }

    public static IBehaviour? Create(string typeName)
    {
        if (_behaviourTypes.TryGetValue(typeName, out var type))
            return Activator.CreateInstance(type) as IBehaviour;
        return null;
    }

    public static IEnumerable<string> GetAvailableBehaviours() => _behaviourTypes.Keys;

    public static Type? GetBehaviourType(string typeName)
    {
        _behaviourTypes.TryGetValue(typeName, out var t);
        return t;
    }
}