using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Protects a Runtime executable before Foundation can commit the corresponding actor birth.</summary>
public sealed record RuntimeKernelExecutablePin(PersistenceScope Scope, string Tenant, string ActorReference,
    WorkflowExecutableIdentity Executable, string Issuance)
{
    /// <summary>The isolated provider tenant of a Test Run. <see cref="Tenant"/> remains its owning Runtime tenant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IsolatedTenant { get; init; }
}

/// <summary>Open pins from one physical scope, in stable cursor order.</summary>
public sealed record RuntimeKernelExecutablePinPage(IReadOnlyList<RuntimeKernelExecutablePin> Items, string? Next);

/// <summary>The outcome of confirming one exact pin with the Foundation provider that issued it.</summary>
public enum RuntimeKernelExecutablePinProof
{
    /// <summary>The birth may exist or may still commit, or the provider cannot prove its disposition.</summary>
    Unresolved,
    /// <summary>The exact physical birth is durably retired.</summary>
    BirthRetired,
    /// <summary>One consistent read proves the exact birth absent and its issuance permanently fenced.</summary>
    IssuanceFencedAbsent
}

/// <summary>The Runtime root store for trusted, provider-issued actor births.</summary>
public interface IRuntimeKernelExecutablePinStore
{
    /// <summary>
    /// Protects all births in one Runtime transaction. Replays require the same immutable identity and issuance.
    /// False means an exact pin has already been released and the caller must refuse the entire birth batch.
    /// </summary>
    ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken cancellationToken = default);

    /// <summary>Protects one prepared root and verifies its exact live publication in the same Runtime transaction.</summary>
    ValueTask<bool> TryPinPublishedAsync(RuntimeKernelExecutablePin pin, WorkflowActivationSlot slot,
        WorkflowExecutableSourceReference source, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new NotSupportedException("The Runtime provider does not fence published executable pins."));

    /// <summary>Protects one prepared test root and verifies its live source and open exact test scope in the same transaction.</summary>
    ValueTask<bool> TryPinTestRunAsync(RuntimeKernelExecutablePin pin, WorkflowTestScope scope,
        WorkflowExecutableSourceReference source, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new NotSupportedException("The Runtime provider does not fence Test Run executable pins."));

    /// <summary>Returns the inclusive upper cursor for this cleanup pass, or null when there are no open pins.</summary>
    ValueTask<string?> ReadOpenTailAsync(PersistenceScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bounded page from one physical scope. Start the next cleanup pass without an after cursor because
    /// births created during this pass can sort before the last cursor.
    /// </summary>
    ValueTask<RuntimeKernelExecutablePinPage> ListOpenAsync(PersistenceScope scope, string? after, string through, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the exact pin after its issuing provider proves retirement or fenced absence. The callback must
    /// verify the stored issuance, tenant and birth. A lookup miss, expired lease or unreachable provider is unresolved.
    /// </summary>
    ValueTask<bool> TryReleaseAsync(RuntimeKernelExecutablePin pin,
        Func<RuntimeKernelExecutablePin, CancellationToken, ValueTask<RuntimeKernelExecutablePinProof>> confirmAsync,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every distinct artifact rooted by an open pin in the selected ordinary Runtime scope.</summary>
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default);
}
