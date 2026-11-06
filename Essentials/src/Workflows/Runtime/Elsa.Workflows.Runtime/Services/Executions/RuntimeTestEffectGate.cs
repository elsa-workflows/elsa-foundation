using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Validates actual execution ownership before delegating to one retained testing-policy owner.</summary>
internal sealed class RuntimeTestEffectGate(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables, IWorkflowTestScopeStore scopes,
    IEnumerable<IWorkflowTestEntryContextResolver> entries, IEnumerable<IRuntimeTestEffectPolicySource> policies,
    TimeProvider clock) : IRuntimeTestEffectGate
{
    public ValueTask ValidateNodeAsync(RuntimeTestNodeRequest request, CancellationToken cancellationToken = default) =>
        Protect(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var context = await ReadAsync(new(request.WorkflowExecutionId, request.ActivityExecutionId, "", request.Node.ExecutableNodeId),
                false, cancellationToken, requireAttempt: false);
            if (context.Entry is { } entry)
            {
                if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(context.Entry.Executable, request.Executable) ||
                    !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(context.Node), JsonSerializer.SerializeToElement(request.Node)))
                    throw Refused("runtime.test-effects.node-mismatch", "The supplied node differs from the exact admitted executable.");
                await Policy().ValidateNodeAsync(entry, request, cancellationToken);
            }
        }, cancellationToken);

    public ValueTask<RuntimeTestInvocation> ValidateActivityAsync(RuntimeTestActivationRequest request, CancellationToken cancellationToken = default) =>
        Protect(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var activity = await activities.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken)
                ?? throw Refused("runtime.test-effects.invocation-unavailable", "The executing activity is unavailable in the current scope.");
            var invocation = new RuntimeTestInvocation(request.WorkflowExecutionId, request.ActivityExecutionId,
                request.AttemptId, activity.Execution.ExecutableNodeId);
            var context = await ReadAsync(invocation, false, cancellationToken);
            if (context.Entry is { } entry)
            {
                var descriptor = request.Descriptor ?? new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor(
                    request.Contract.DescriptorKind, "1", request.Contract.DescriptorPayload);
                if (context.Node.ActivityContract?.SchemaFingerprint != request.Contract.SchemaFingerprint ||
                    context.Node.Descriptor.ConsumerKey != descriptor.ConsumerKey || context.Node.Descriptor.SchemaVersion != descriptor.SchemaVersion ||
                    !JsonElement.DeepEquals(context.Node.Descriptor.Payload, descriptor.Payload))
                    throw Refused("runtime.test-effects.activation-mismatch", "The supplied activation differs from the admitted pinned node.");
                await Policy().ValidateNodeAsync(entry, new(invocation.WorkflowExecutionId, invocation.ActivityExecutionId,
                    entry.Executable, context.Node), cancellationToken);
                await Policy().ValidateActivityAsync(entry, invocation, cancellationToken);
            }
            return invocation;
        }, cancellationToken);

    public ValueTask<RuntimeSecretReference> MapSecretAsync(RuntimeTestInvocation invocation, string inputKey,
        RuntimeSecretReference reference, CancellationToken cancellationToken = default) => Protect(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
            ArgumentNullException.ThrowIfNull(reference);
            var context = await ReadAsync(invocation, false, cancellationToken);
            return context.Entry is { } entry
                ? await Policy().MapSecretAsync(entry, invocation, inputKey, reference, cancellationToken)
                    ?? throw Refused("runtime.test-effects.secret-unavailable", "The admitted test secret mapping is unavailable.")
                : reference;
        }, cancellationToken);

    public ValueTask<RuntimeTestEffectAdmission> BeginAsync(RuntimeTestEffectRequest request,
        CancellationToken cancellationToken = default) => Protect(async () =>
        {
            ValidateRequest(request);
            var context = await ReadAsync(request.Invocation, false, cancellationToken);
            if (context.Entry is not { } entry)
                return new RuntimeTestEffectAdmission(request, RuntimeTestEffectMode.Ordinary, "elsa.ordinary-effect/1",
                    JsonSerializer.SerializeToElement<object?>(null), null, null, null, 0, true);
            var admitted = await Policy().BeginAsync(entry, request, cancellationToken);
            ValidateAdmission(entry, request, admitted);
            return admitted;
        }, cancellationToken);

    public ValueTask RevalidateAsync(RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default) =>
        Protect(async () =>
        {
            ArgumentNullException.ThrowIfNull(admission);
            ValidateRequest(admission.Request);
            var context = await ReadAsync(admission.Request.Invocation, false, cancellationToken);
            if (context.Entry is { } entry)
            {
                ValidateAdmission(entry, admission.Request, admission);
                if (!admission.CanExecute) throw Refused("runtime.test-effects.replay-unavailable", "The retained operation cannot execute again.");
                await Policy().RevalidateAsync(entry, admission, cancellationToken);
            }
            else if (admission.Mode != RuntimeTestEffectMode.Ordinary || admission.ContextId is not null || admission.ReceiptId is not null)
                throw Refused("runtime.test-effects.receipt-mismatch", "The effect receipt differs from the executing context.");
        }, cancellationToken);

    public ValueTask CompleteAsync(RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default) =>
        Protect(async () =>
        {
            ArgumentNullException.ThrowIfNull(completion);
            var admission = completion.Admission;
            ValidateRequest(admission.Request);
            if (!Enum.IsDefined(completion.Disposition))
                throw Refused("runtime.test-effects.disposition-invalid", "The effect disposition is unsupported.");
            // Settlement accepts a previously admitted obligation after close/revocation; it grants no new work.
            var context = await ReadAsync(admission.Request.Invocation, true, cancellationToken);
            if (context.Entry is { } entry)
            {
                ValidateAdmission(entry, admission.Request, admission);
                await Policy().CompleteAsync(entry, completion, cancellationToken);
            }
            else if (admission.Mode != RuntimeTestEffectMode.Ordinary || admission.ContextId is not null || admission.ReceiptId is not null)
                throw Refused("runtime.test-effects.receipt-mismatch", "The effect receipt differs from the executing context.");
        }, cancellationToken);

    private async ValueTask<(WorkflowExecutionState Workflow, WorkflowTestEntry? Entry, ExecutableNode Node)> ReadAsync(
        RuntimeTestInvocation invocation, bool settlement, CancellationToken ct, bool requireAttempt = true)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var workflow = await workflows.FindAsync(invocation.WorkflowExecutionId, ct)
            ?? throw Refused("runtime.test-effects.execution-unavailable", "The executing workflow is unavailable in the current scope.");
        var activity = await activities.FindAsync(invocation.WorkflowExecutionId, invocation.ActivityExecutionId, ct)
            ?? throw Refused("runtime.test-effects.invocation-unavailable", "The executing activity is unavailable in the current scope.");
        var activeAttempts = activity.Attempts?.Where(x => x.EndedAt is null).ToArray() ?? [];
        if (workflow.WorkflowExecutionId != invocation.WorkflowExecutionId ||
            activity.Execution.WorkflowExecutionId != invocation.WorkflowExecutionId || activity.InvocationId != invocation.ActivityExecutionId ||
            activity.Execution.ExecutableNodeId != invocation.ExecutableNodeId ||
            !settlement && requireAttempt && (activeAttempts.Length != 1 || activeAttempts[0].AttemptId != invocation.AttemptId ||
                activity.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimed) && claimed != invocation.AttemptId) ||
            requireAttempt && activity.Attempts?.Any(x => x.AttemptId == invocation.AttemptId && x.InvocationId == invocation.ActivityExecutionId &&
                (settlement || x.EndedAt is null)) != true ||
            !settlement && (workflow.Status.IsTerminal() || activity.Completion is not null))
            throw Refused("runtime.test-effects.invocation-mismatch", "The effect caller does not own the current invocation.");
        var executable = await executables.FindAsync(workflow.PinnedExecutable.ArtifactId, ct)
            ?? throw Refused("runtime.test-effects.artifact-unavailable", "The exact executing artifact is unavailable.");
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, executable.Identity) ||
            !executable.NodesById.ContainsKey(invocation.ExecutableNodeId))
            throw Refused("runtime.test-effects.artifact-mismatch", "The invocation differs from the exact executing artifact.");
        if (workflow.TestEntry is not { } entry)
        {
            if (workflow.TestContext is not null)
                throw Refused("runtime.test-effects.entry-missing", "The retained test context has no authorized entry.");
            return (workflow, null, executable.NodesById[invocation.ExecutableNodeId]);
        }
        if (workflow.RunKind != WorkflowRunKind.TestRun || !WorkflowTestScope.ContextEquals(entry.Scope, workflow.TestScope) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(entry.Executable, executable.Identity) ||
            workflow.Authority is not { } authority || !WorkflowExecutionAuthoritySnapshot.Matches(entry.Authority,
                authority.SystemIdentity, authority.RootInitiator, authority.Metadata) ||
            workflow.Partition != entry.Scope.Partition || workflow.TenantId != entry.Scope.TenantId)
            throw Refused("runtime.test-effects.context-mismatch", "The invocation differs from its admitted test context.");
        var testContext = workflow.TestContext;
        var owned = executable.NodesById[entry.ExecutableNodeId].DescendantsAndSelf()
            .Select(x => x.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);
        if (testContext is null || testContext.ContextId != entry.ContextId || testContext.ContextFingerprint != entry.ContextFingerprint ||
            testContext.OwnedNodeIds.Count != owned.Count || !owned.SetEquals(testContext.OwnedNodeIds) ||
            !owned.Contains(invocation.ExecutableNodeId))
            throw Refused("runtime.test-effects.owned-context-mismatch", "The invocation is outside its initialized selected test context.");
        if (!settlement)
        {
            var resolvers = entries.ToArray();
            if (resolvers.Length != 1 || !entry.Matches(await resolvers[0].FindAsync(entry.ContextId, ct)))
                throw Refused("runtime.test-effects.context-unavailable", "The retained test context is no longer available.");
            var current = await scopes.FindAsync(entry.Scope.ScopeId, ct);
            if (entry.Scope.IsExpired(clock.GetUtcNow()) || current is not { State: WorkflowTestScopeState.Open } ||
                !WorkflowTestScope.ContextEquals(current.Scope, entry.Scope))
                throw Refused("runtime.test-effects.scope-closed", "The test scope no longer admits work.");
        }
        return (workflow, entry, executable.NodesById[invocation.ExecutableNodeId]);
    }

    private IRuntimeTestEffectPolicySource Policy()
    {
        var sources = policies.ToArray();
        return sources.Length == 1 ? sources[0] :
            throw Refused("runtime.test-effects.policy-unavailable", "Exactly one retained test effect policy owner is required.");
    }

    private static void ValidateRequest(RuntimeTestEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.EffectKey) || string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.RequestFingerprint))
            throw Refused("runtime.test-effects.request-invalid", "An effect requires its stable operation and request fingerprint.");
    }

    private static void ValidateAdmission(WorkflowTestEntry entry, RuntimeTestEffectRequest request, RuntimeTestEffectAdmission? admission)
    {
        if (admission is null || admission.Request != request || admission.Mode == RuntimeTestEffectMode.Ordinary ||
            admission.ContextId != entry.ContextId || admission.ContextFingerprint != entry.ContextFingerprint ||
            string.IsNullOrWhiteSpace(admission.ReceiptId))
            throw Refused("runtime.test-effects.admission-mismatch", "The effect policy did not return the exact retained test admission.");
    }

    private static async ValueTask Protect(Func<ValueTask> operation, CancellationToken ct)
    {
        try { await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (RuntimeTestEffectUnavailableException) { throw; }
        catch (Exception exception) { throw new RuntimeTestEffectUnavailableException("runtime.test-effects.infrastructure-failed", "The owning effect infrastructure could not complete the operation.", exception); }
    }

    private static async ValueTask<T> Protect<T>(Func<ValueTask<T>> operation, CancellationToken ct)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (RuntimeTestEffectUnavailableException) { throw; }
        catch (Exception exception) { throw new RuntimeTestEffectUnavailableException("runtime.test-effects.infrastructure-failed", "The owning effect infrastructure could not complete the operation.", exception); }
    }

    private static RuntimeTestEffectUnavailableException Refused(string code, string message) => new(code, message);
}
