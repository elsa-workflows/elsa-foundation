using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityOperationKeyResolverTests
{
    [Fact]
    public void An_assigned_key_in_the_scheduling_provenance_wins()
    {
        var state = State(
            provenanceMetadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.OperationKey] = "scope/step/1",
                [RuntimeMetadataKeys.TargetOperationKey] = "scope/step/0"
            },
            stateMetadata: new Dictionary<string, string> { [RuntimeMetadataKeys.OperationKey] = "state-key" },
            lineage: new ActivityExecutionAttemptLineage(2, "first", "previous"));

        Assert.Equal("scope/step/1", ActivityOperationKeyResolver.ResolveOperationKey(state));
        Assert.Equal("scope/step/0", ActivityOperationKeyResolver.ResolveTargetOperationKey(state));
    }

    [Fact]
    public void An_assigned_key_in_the_state_metadata_is_the_fallback_for_assignment()
    {
        var state = State(stateMetadata: new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.OperationKey] = "state-key",
            [RuntimeMetadataKeys.TargetOperationKey] = "state-target"
        });

        Assert.Equal("state-key", ActivityOperationKeyResolver.ResolveOperationKey(state));
        Assert.Equal("state-target", ActivityOperationKeyResolver.ResolveTargetOperationKey(state));
    }

    [Fact]
    public void Without_an_assigned_key_the_first_attempt_identity_is_used()
    {
        Assert.Equal("first", ActivityOperationKeyResolver.ResolveOperationKey(State(lineage: new ActivityExecutionAttemptLineage(3, "first", "second"))));
        Assert.Equal("first-in-provenance", ActivityOperationKeyResolver.ResolveOperationKey(State(provenanceLineage: new ActivityExecutionAttemptLineage(2, "first-in-provenance", null))));
    }

    [Fact]
    public void Without_an_assigned_key_or_lineage_the_invocation_id_is_used_and_there_is_no_target()
    {
        var state = State(provenanceMetadata: new Dictionary<string, string> { [RuntimeMetadataKeys.OperationKey] = " " });

        Assert.Equal("invocation", ActivityOperationKeyResolver.ResolveOperationKey(state));
        Assert.Null(ActivityOperationKeyResolver.ResolveTargetOperationKey(state));
    }

    [Fact]
    public void The_context_carries_the_resolved_keys_and_defaults_to_the_invocation_id()
    {
        var context = new ActivityExecutionContext("workflow", "invocation", "attempt", "node", CancellationToken.None);
        Assert.Equal("invocation", context.OperationKey);
        Assert.Null(context.TargetOperationKey);

        var withKeys = context.WithResolvedOperationKeys(State(provenanceMetadata: new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.OperationKey] = "key",
            [RuntimeMetadataKeys.TargetOperationKey] = "target"
        }));

        Assert.Equal("key", withKeys.OperationKey);
        Assert.Equal("target", withKeys.TargetOperationKey);
        Assert.Same(context, context.WithResolvedOperationKeys(null));
        Assert.Equal("invocation", context.WithOperationKeys(" ", "target").OperationKey);
    }

    private static ActivityExecutionState State(
        IReadOnlyDictionary<string, string>? provenanceMetadata = null,
        IReadOnlyDictionary<string, string>? stateMetadata = null,
        ActivityExecutionAttemptLineage? lineage = null,
        ActivityExecutionAttemptLineage? provenanceLineage = null) =>
        new(
            new ActivityExecution("invocation", "workflow", "node", "node", "test", "1"),
            ActivityExecutionStatus.Running, null, 1, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
            ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, "test", provenanceMetadata, provenanceLineage),
            null, [], [], 0, 0, stateMetadata ?? new Dictionary<string, string>())
        {
            Attempt = lineage
        };
}
