using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Runtime-side registry mapping an exact consumer key/schema pair to its
/// <see cref="IActivityConstructor"/>. Populated once at startup via the Registry + StartUp Task
/// sub-pattern (framework §2.6.1) — an <c>OnActivityConstructorsInitializing</c> event is published,
/// the single aggregating handler adds every contributed constructor, and the startup task flushes
/// them here. Consumed synchronously afterward by <see cref="IActivityFactory"/>.
/// </summary>
/// <remarks>
/// Invariant: exactly one constructor per consumer key/schema pair.
/// </remarks>
public interface IActivityConstructorRegistry
{
    /// <summary>Register a constructor for every schema it declares.</summary>
    void Add(IActivityConstructor constructor);

    /// <summary>Register many constructors (each subject to the one-per-key/schema invariant).</summary>
    void AddAll(IEnumerable<IActivityConstructor> constructors);

    IActivityConstructor Resolve(string consumerKey, string schemaVersion);

    bool TryResolve(string consumerKey, string schemaVersion, out IActivityConstructor? constructor);

    IReadOnlyCollection<string> GetSupportedSchemaVersions(string consumerKey);

    IActivityConstructor Resolve(RuntimeActivityDescriptor descriptor) => Resolve(descriptor.ConsumerKey, descriptor.SchemaVersion);
}
