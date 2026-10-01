using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Pins the frozen v1 keyed start identity (#2195). Execution ids derived this way are persisted, and a redelivery is
/// recognized only by deriving the same id again, so these literals must never change. A new derivation is a new version.
/// </summary>
public sealed class KeyedWorkflowStartIdentityTests
{
    // The idempotency key PublishStimulusStagingBuffer gives the first publish of activity execution "actexec-publish".
    private const string IdempotencyKey = "publish-stimulus:actexec-publish:0";
    private const string ArtifactId = "artifact-1";
    private const string Digest = "2755f6772df5c4c8cd01f7cddcc9df13db6423c88924244549340a7337ca65cb";

    [Fact]
    public void The_v1_ids_for_a_fixed_key_and_artifact_never_change()
    {
        var identity = KeyedWorkflowStartIdentity.For(IdempotencyKey, ArtifactId);

        Assert.Equal("v1", KeyedWorkflowStartIdentity.Version);
        Assert.Equal("publish-stimulus:actexec-publish:0:start:artifact-1", identity.StartKey);
        Assert.Equal($"wfexec:start:v1:{Digest}", identity.WorkflowExecutionId);
        Assert.Equal($"command:start:v1:{Digest}", identity.CommandId);
        Assert.Equal($"envelope:start:v1:{Digest}", identity.EnvelopeId);
    }

    [Fact]
    public void A_request_is_a_keyed_start_only_when_its_execution_id_is_the_one_derived_from_its_key()
    {
        var identity = KeyedWorkflowStartIdentity.For(IdempotencyKey, ArtifactId);

        var keyed = KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, identity.StartKey));

        Assert.Equal(identity.WorkflowExecutionId, keyed?.WorkflowExecutionId);
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request("wfexec-caller-named", identity.StartKey)));
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, idempotencyKey: null)));
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request(workflowExecutionId: null, identity.StartKey)));
    }

    [Fact]
    public void One_key_names_a_distinct_start_per_artifact()
    {
        Assert.NotEqual(
            KeyedWorkflowStartIdentity.For(IdempotencyKey, "artifact-1").WorkflowExecutionId,
            KeyedWorkflowStartIdentity.For(IdempotencyKey, "artifact-2").WorkflowExecutionId);
    }

    private static WorkflowExecutionStartDispatchRequest Request(string? workflowExecutionId, string? idempotencyKey) =>
        new(ArtifactId, "runtime-test", workflowExecutionId: workflowExecutionId, idempotencyKey: idempotencyKey);
}
