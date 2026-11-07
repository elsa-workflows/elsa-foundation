using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Explicit policy double for the known in-memory entry/context witnesses. It grants no external effects,
/// secrets or production support profile. Whole-case profile preparation remains a separate delivery gate.
/// </summary>
internal sealed class TestOnlyPureEffectPolicy : IRuntimeTestEffectPolicySource
{
    public ValueTask ValidateNodeAsync(WorkflowTestEntry entry, RuntimeTestNodeRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ValidateActivityAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask<RuntimeSecretReference> MapSecretAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation,
        string inputKey, RuntimeSecretReference reference, CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeSecretReference>(Unavailable());
    public ValueTask<RuntimeTestEffectAdmission> BeginAsync(WorkflowTestEntry entry, RuntimeTestEffectRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeTestEffectAdmission>(Unavailable());
    public ValueTask RevalidateAsync(WorkflowTestEntry entry, RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default) => ValueTask.FromException(Unavailable());
    public ValueTask CompleteAsync(WorkflowTestEntry entry, RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default) => ValueTask.FromException(Unavailable());
    private static RuntimeTestEffectUnavailableException Unavailable() => new("test-only.external-unavailable", "The pure component witness has no external effect profile.");
}
