using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Descriptor for activity versions whose implementation is a CLR type. The wrapped
/// <see cref="TypeInfo"/> is the persistence-stable handle (assembly name + namespace + type
/// name + assembly version) — not a live <see cref="System.Type"/>. The CLR resolver
/// (in the activities runtime feature) loads the live type at construction time.
/// </summary>
public sealed record ClrImplementationDescriptor(TypeInformation TypeInfo) : IImplementationDescriptor
{
    public const string KindValue = "Clr";

    public string Kind => KindValue;
}
