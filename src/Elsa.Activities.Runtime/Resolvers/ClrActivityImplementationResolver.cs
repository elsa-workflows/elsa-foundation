using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Resolvers;

/// <summary>
/// Resolves <see cref="ClrImplementationDescriptor"/> to a live <see cref="Type"/> by loading
/// the wrapped <see cref="Primitives.Models.TypeInformation"/>. Owns the well-known kind
/// string <c>"Clr"</c> (the same value <see cref="ClrImplementationDescriptor.Kind"/> returns).
/// </summary>
public sealed class ClrActivityImplementationResolver : IActivityImplementationResolver<ClrImplementationDescriptor>
{
    public const string KindValue = "Clr";

    public string Kind => KindValue;

    public Type Resolve(ClrImplementationDescriptor descriptor) => descriptor.TypeInfo.LoadType();
}
