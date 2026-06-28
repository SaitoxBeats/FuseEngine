using System;

namespace Fuse.Behaviours;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class BehaviourAttribute : Attribute
{
    public string TypeName { get; }

    public BehaviourAttribute(string typeName)
    {
        TypeName = typeName;
    }
}
