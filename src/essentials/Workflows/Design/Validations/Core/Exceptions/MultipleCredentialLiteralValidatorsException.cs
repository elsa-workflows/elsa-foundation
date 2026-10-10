using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Design.Validations.Core.Exceptions;

/// <summary>
/// A host composes more than one <see cref="ICredentialLiteralValidator"/>, a replacement contract with at most one
/// implementation per container (framework constitution §2.6.2). Thrown at shell activation, so the host does not start
/// rather than judging credential literals through whichever registration came last.
/// </summary>
public sealed class MultipleCredentialLiteralValidatorsException(IReadOnlyList<string> implementations)
    : Exception(
        $"{implementations.Count} {nameof(ICredentialLiteralValidator)} registrations are composed in this host " +
        $"({string.Join(", ", implementations)}). {nameof(ICredentialLiteralValidator)} is a replacement contract with at most " +
        "one implementation per container, and a second one is never resolved by last-write-wins. Compose one credential-literal validator.")
{
    /// <summary>The composed registrations' implementations, in registration order.</summary>
    public IReadOnlyList<string> Implementations { get; } = implementations;
}
