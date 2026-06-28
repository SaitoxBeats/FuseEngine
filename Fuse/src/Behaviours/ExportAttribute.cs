using System;

namespace Fuse.Behaviours;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public class ExportAttribute : Attribute
{
    // Opcional: pode adicionar min, max, descricoes no futuro
}
