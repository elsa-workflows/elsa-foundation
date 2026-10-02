using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A host composes more than one <see cref="IRuntimeSecretResolver"/>, a replacement contract with at most one
/// implementation per container (framework constitution §2.6.2). Thrown at shell activation, so the host does not start
/// rather than resolving secrets through whichever registration came last.
/// </summary>
public sealed class MultipleRuntimeSecretResolversException(IReadOnlyList<string> implementations)
    : Exception(
        $"{implementations.Count} {nameof(IRuntimeSecretResolver)} registrations are composed in this host " +
        $"({string.Join(", ", implementations)}). {nameof(IRuntimeSecretResolver)} is a replacement contract with at most " +
        "one implementation per container, and a second one is never resolved by last-write-wins. Compose one secret resolver.")
{
    /// <summary>The composed registrations' implementations, in registration order.</summary>
    public IReadOnlyList<string> Implementations { get; } = implementations;
}
