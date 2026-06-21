namespace Fuse.Interaction;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InteractableTypeAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
}
