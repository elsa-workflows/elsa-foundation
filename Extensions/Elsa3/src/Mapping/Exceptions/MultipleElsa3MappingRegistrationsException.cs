namespace Elsa3.Mapping.Exceptions;

/// <summary>
/// A host composes more than one registration of an Elsa 3 mapping replacement contract, which has at most one
/// implementation per container. Thrown at shell activation, so the host does not start rather than converting through
/// whichever registration came last.
/// </summary>
public sealed class MultipleElsa3MappingRegistrationsException(Type contract, IReadOnlyList<string> implementations)
    : Exception(
        $"{implementations.Count} {contract.Name} registrations are composed in this host " +
        $"({string.Join(", ", implementations)}). {contract.Name} is a replacement contract with at most one " +
        "implementation per container, and a second one is never resolved by last-write-wins. The Elsa3Mapping " +
        "feature registers a default; register your own before the feature, or replace it with services.Replace(...), " +
        "instead of adding another registration.")
{
    /// <summary>The replacement contract registered more than once.</summary>
    public Type Contract { get; } = contract;

    /// <summary>The composed registrations' implementations, in registration order.</summary>
    public IReadOnlyList<string> Implementations { get; } = implementations;
}
