namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Runtime-side registry mapping a descriptor type's <c>FullName</c> → its
/// <see cref="IActivityConstructor"/>. Populated once at startup via the Registry + StartUp Task
/// sub-pattern (framework §2.6.1) — an <c>OnActivityConstructorsInitializing</c> event is published,
/// the single aggregating handler adds every contributed constructor, and the startup task flushes
/// them here. Consumed synchronously afterward by <see cref="IActivityFactory"/>.
/// </summary>
/// <remarks>
/// Invariant: exactly one constructor per descriptor type. Registering a second constructor for an
/// already-claimed type throws <c>DuplicateActivityConstructorException</c> (loud, not last-wins).
/// </remarks>
public interface IActivityConstructorRegistry
{
    /// <summary>Register a single constructor. Throws if its descriptor type is already claimed by a different constructor type.</summary>
    void Add(IActivityConstructor constructor);

    /// <summary>Register many constructors (each subject to the one-per-type invariant).</summary>
    void AddAll(IEnumerable<IActivityConstructor> constructors);

    /// <summary>Resolve the constructor for a descriptor type. Throws <c>UnknownDescriptorTypeException</c> (a domain failure) when none is registered.</summary>
    IActivityConstructor Resolve(string descriptorType);
}
