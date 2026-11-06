using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider trusted invocation/effect boundary. Ordinary invocations keep their existing behavior; a
/// testing-owned invocation requires retained exact policy and durable receipts. Authored metadata grants nothing.
/// Missing policy, unknown activity effects or changed mappings refuse before constructors, secrets or I/O.
/// </summary>
public interface IRuntimeTestEffectGate
{
    /// <summary>Refuses unqualified nodes before expressions, input materializers or intrinsic work run.</summary>
    ValueTask ValidateNodeAsync(RuntimeTestNodeRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new RuntimeTestEffectUnavailableException("runtime.test-effects.node-unavailable", "The node has no qualified test effect profile."));

    /// <summary>Returns canonical physical invocation facts after validating the supplied contract and descriptor.</summary>
    ValueTask<RuntimeTestInvocation> ValidateActivityAsync(RuntimeTestActivationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Maps only an explicitly authorized test reference before the ordinary resolver sees it.</summary>
    ValueTask<RuntimeSecretReference> MapSecretAsync(RuntimeTestInvocation invocation, string inputKey,
        RuntimeSecretReference reference, CancellationToken cancellationToken = default);

    /// <summary>Atomically admits and records an attempted operation under the normal scope closure fence.</summary>
    ValueTask<RuntimeTestEffectAdmission> BeginAsync(RuntimeTestEffectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Rechecks exact current ownership/policy before additional work such as credential-refresh retries.</summary>
    ValueTask RevalidateAsync(RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default);
}
