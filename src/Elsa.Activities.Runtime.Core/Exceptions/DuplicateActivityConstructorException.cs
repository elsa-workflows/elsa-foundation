namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Thrown at registry-build (startup) time when two distinct constructors claim the same descriptor
/// type. The descriptor type is the dispatch key, so a duplicate makes construction ambiguous —
/// caught loudly at composition time rather than silently overwritten (one-constructor-per-type
/// invariant).
/// </summary>
public sealed class DuplicateActivityConstructorException(string descriptorType, Type existing, Type attempted)
    : Exception($"Activity constructor for descriptor type '{descriptorType}' is already registered with '{existing.FullName}'; refusing to overwrite with '{attempted.FullName}'.")
{
    public string DescriptorType { get; } = descriptorType;
}
