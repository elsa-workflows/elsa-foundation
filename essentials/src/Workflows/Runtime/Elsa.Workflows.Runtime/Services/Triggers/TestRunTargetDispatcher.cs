using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Dispatches an external driver's request through the ordinary TestRun start boundary.</summary>
internal sealed class TestRunTargetDispatcher(
    ITestRunTargetStore targets,
    IWorkflowTestScopeStore scopes,
    IWorkflowStartDispatcher startDispatcher,
    TimeProvider timeProvider,
    IPersistenceAccessContextAccessor? accessContextAccessor = null,
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null) : ITestRunTargetDispatcher
{
    public ValueTask<TestRunTargetDispatchResult> DispatchAsync(
        string testRunId,
        JsonElement? stimulusInput,
        CancellationToken cancellationToken = default) =>
        DispatchAsync(testRunId, stimulusInput, dispatchOptions: null, cancellationToken: cancellationToken);

    public async ValueTask<TestRunTargetDispatchResult> DispatchAsync(
        string testRunId,
        JsonElement? stimulusInput,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        if (stimulusInput is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A trigger stimulus must be a defined JSON value when provided.", nameof(stimulusInput));

        var now = timeProvider.GetUtcNow();
        var target = await targets.FindAsync(testRunId, cancellationToken)
                     ?? throw new KeyNotFoundException("The workflow test target was not found or has expired.");
        EnsureCallerContext(target.Scope);
        var scope = await scopes.FindAsync(testRunId, cancellationToken);
        EnsureOpen(scope, target.Scope, now);

        var digest = Digest(stimulusInput);
        var admission = await targets.BeginDispatchAsync(testRunId, digest, cancellationToken);
        switch (admission.Disposition)
        {
            case TestRunTargetDisposition.AlreadyDispatched:
                return new TestRunTargetDispatchResult(admission.Target!.DispatchResult!.Dispatch, AlreadyDispatched: true);
            case TestRunTargetDisposition.PayloadConflict:
                throw new InvalidOperationException("This prepared workflow test target was already fired with different data.");
            case TestRunTargetDisposition.Stopped:
            case TestRunTargetDisposition.Expired:
            case TestRunTargetDisposition.NotFound:
                throw new TestScopeAdmissionException("The workflow test target is no longer open.");
            case TestRunTargetDisposition.Ready:
                target = admission.Target!;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(admission.Disposition));
        }

        // The checkpoint provider performs the final scope admission fence in its atomic write. This earlier
        // check gives a prompt refusal; it is not the race-safety boundary.
        var request = new WorkflowExecutionStartDispatchRequest(
            artifactId: target.Executable.ArtifactId,
            requestedBy: target.RequestedBy,
            workflowExecutionId: target.WorkflowExecutionId,
            idempotencyKey: target.IdempotencyKey,
            metadata: new Dictionary<string, string>
            {
                ["runtime.scope"] = "test-run",
                ["runtime.testRunId"] = target.TestRunId,
                ["runtime.sourceReferenceId"] = target.SourceReferenceId,
                ["runtime.sourceDefinitionId"] = target.Executable.DefinitionId,
                ["runtime.sourceDefinitionVersionId"] = target.Executable.DefinitionVersionId
            },
            variables: target.Variables,
            inputs: target.Inputs.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal),
            stimulusInput: stimulusInput,
            triggerNodeId: target.Binding.ExecutableNodeId,
            runKind: WorkflowRunKind.TestRun,
            sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: target.SourceReferenceId),
            provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
            parentWorkflowExecutionId: null,
            correlationId: null,
            tenantId: target.Scope.TenantId,
            partition: target.Scope.Partition,
            authority: null,
            startAuthority: null,
            dispatchNestingDepth: 0,
            testScope: target.Scope,
            triggerMetadata: target.Binding.Metadata);

        var dispatched = await startDispatcher.DispatchAsync(
            request,
            WorkflowExecutableReferenceScope.TestRun,
            dispatchOptions,
            cancellationToken: cancellationToken);
        var result = new TestRunTargetDispatchResult(dispatched, AlreadyDispatched: false);
        await targets.CompleteDispatchAsync(testRunId, result, cancellationToken);
        return result;
    }

    private static void EnsureOpen(WorkflowTestScopeRecord? record, WorkflowTestScope expected, DateTimeOffset now)
    {
        if (record is null || record.State != WorkflowTestScopeState.Open || record.Scope.IsExpired(now) ||
            !WorkflowTestScope.ContextEquals(record.Scope, expected))
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
    }

    private void EnsureCallerContext(WorkflowTestScope expected)
    {
        var currentTenantId = accessContextAccessor?.Current.Scope?.Value;
        var currentPartition = partitionAccessor?.Current ??
                               new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);
        if (!StringComparer.Ordinal.Equals(currentTenantId, expected.TenantId) ||
            !Equals(currentPartition, expected.Partition))
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
    }

    private static string Digest(JsonElement? input)
    {
        var payload = input is { } element ? JsonSerializer.SerializeToUtf8Bytes(element) : "null"u8.ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }
}
