namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Polymorphic descriptor carried by every <c>ActivityDefinitionVersion</c>. The
/// <see cref="Kind"/> string is the registry lookup key — the implementation-descriptor
/// registry maps <see cref="Kind"/> → CLR descriptor type (for loading), and the resolver
/// registry maps <see cref="Kind"/> → resolver (for runtime construction). Each concrete
/// descriptor self-declares its kind; descriptor types are contributed at startup by the
/// module that owns the kind, so the core library never enumerates the set of known kinds.
/// </summary>
public interface IImplementationDescriptor
{
    /// <summary>
    /// Registry lookup key. Stable across the descriptor type's lifetime; the persisted
    /// <c>ImplementationKind</c> column carries the same value at the row level so the
    /// loading handler can resolve the deserialisation target before deserialising.
    /// </summary>
    string Kind { get; }
}
