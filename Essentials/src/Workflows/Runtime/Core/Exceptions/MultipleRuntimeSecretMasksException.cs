using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A host composes more than one <see cref="IRuntimeSecretMask"/>, a replacement contract with at most one
/// implementation per container (framework constitution §2.6.2). Thrown at shell activation, so the host does not start
/// rather than masking through whichever registration came last.
/// </summary>
public sealed class MultipleRuntimeSecretMasksException(IReadOnlyList<string> implementations)
    : Exception(
        $"{implementations.Count} {nameof(IRuntimeSecretMask)} registrations are composed in this host " +
        $"({string.Join(", ", implementations)}). {nameof(IRuntimeSecretMask)} is a replacement contract with at most " +
        "one implementation per container, and a second one is never resolved by last-write-wins. The runtime registers " +
        "a default; replace it with services.Replace(...) instead of adding another registration.")
{
    /// <summary>The composed registrations' implementations, in registration order.</summary>
    public IReadOnlyList<string> Implementations { get; } = implementations;
}
