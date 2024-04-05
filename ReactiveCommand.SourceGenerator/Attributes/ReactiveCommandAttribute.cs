using System;

namespace ReactiveCommandSourceGenerator.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class ReactiveCommand : Attribute
//To write ¡°nameof(ReactiveCommand)¡± code, remove the tail of ReactiveCommandAttribute.
//ReactiveCommandSourceGenerator's ReactiveCommandAttribute is not actually used in user code.
{
}