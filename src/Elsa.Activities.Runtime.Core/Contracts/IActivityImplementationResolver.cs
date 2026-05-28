using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Non-generic marker — the kind-typed counterpart is
/// <see cref="IActivityImplementationResolver{TDescriptor}"/>. The registry stores instances
/// behind this marker; the factory casts at dispatch time via the typed interface.
/// </summary>
public interface IActivityImplementationResolver
{
    /// <summary>
    /// The kind this resolver handles. Matches the <c>Kind</c> on every descriptor type
    /// the resolver is registered against (one resolver per kind per host).
    /// </summary>
    string Kind { get; }
}

/// <summary>
/// Kind-typed resolver: returns the CLR <see cref="Type"/> for a given concrete descriptor.
/// The factory activates the type and applies state — the resolver does not activate.
/// </summary>
public interface IActivityImplementationResolver<in TDescriptor> : IActivityImplementationResolver
    where TDescriptor : class, IImplementationDescriptor
{
    Type Resolve(TDescriptor descriptor);
}
