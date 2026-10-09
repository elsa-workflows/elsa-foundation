using System.Reflection;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Validator rules that no commit built through <see cref="RuntimeCheckpointCommitter"/> can break: the committer folds a
/// commit's outbox from the commit's own intents, so intents and outbox always agree there. These rules guard that
/// folding, and are exercised against the validator directly. Every rule a committer-built commit can break is covered
/// against every store by the checkpoint validation contract tests.
/// </summary>
public sealed class RuntimeCheckpointCommitValidatorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly RuntimePostCommitIntent _intent = Intent("intent-a", "test.intent");

    [Fact]
    public void Intents_with_their_folded_pending_outbox_are_valid()
    {
        var commit = Commit([_intent]);

        RuntimeCheckpointCommitValidator.Validate(commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        });
    }

    [Fact]
    public void Intents_without_their_folded_outbox_are_rejected()
    {
        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() => RuntimeCheckpointCommitValidator.Validate(Commit([_intent])));

        Assert.Equal("A checkpoint with post-commit intents must include their pending outbox state changes in the same atomic unit.", exception.Message);
    }

    [Fact]
    public void A_folded_outbox_item_that_differs_from_its_intent_is_rejected()
    {
        var commit = Commit([_intent]);
        var folded = Assert.Single(RuntimePostCommitOutboxItems.CreatePendingChanges(commit));
        var substituted = folded with
        {
            State = new RuntimePostCommitOutboxItem(folded.StateId, Intent("intent-a", "other.kind"), RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt)
        };

        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() => RuntimeCheckpointCommitValidator.Validate(commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox([substituted])
        }));

        Assert.Equal($"Post-commit outbox item '{folded.StateId}' does not match its checkpoint intent.", exception.Message);
    }

    [Fact]
    public void Conflicting_intents_with_the_same_identity_are_rejected()
    {
        var commit = Commit([_intent, Intent("intent-a", "other.kind")]);

        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() => RuntimeCheckpointCommitValidator.Validate(commit));

        Assert.Equal(
            $"Post-commit intent '{RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, "intent-a")}' occurs more than once with conflicting content.",
            exception.Message);
    }

    /// <summary>
    /// The encryption backstop (spec 188, FR-010, T065): producer withholding keeps a value whose policy requires
    /// encryption out of state, and only a producer that skipped it, such as an imported artifact, can put one into a
    /// commit. The refusal names the state and the value's key, never the value.
    /// </summary>
    [Theory]
    [MemberData(nameof(EncryptionRequiredRows))]
    public void A_present_value_that_requires_encryption_is_refused_wherever_the_commit_carries_it(string location, string payload)
    {
        var place = Locations[location];

        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() =>
            RuntimeCheckpointCommitValidator.Validate(Commit(place.Build(Payloads[payload]()))));

        Assert.Equal($"VF-ACT-005: {place.Subject} requires encryption, so it cannot be committed in plain text.", exception.Message);
        Assert.DoesNotContain(Sentinel, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guards the backstop's hand-maintained scan: every member of a workflow or activity execution state that can carry
    /// a <see cref="ValueEnvelope"/>, found by walking the two state types and the types they compose, must be the member
    /// of a <see cref="Locations"/> row, and the refusal theory above proves the validator scans each row. A new
    /// envelope-bearing member turns this red until a row, and the scan it proves, are added.
    /// </summary>
    [Fact]
    public void Every_state_member_that_can_carry_an_envelope_is_a_scanned_location()
    {
        var discovered = EnvelopeMembers(typeof(WorkflowExecutionState))
            .Concat(EnvelopeMembers(typeof(ActivityExecutionState)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(discovered);
        Assert.Equal(discovered, Locations.Values.Select(location => location.Member).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public void A_value_with_nothing_to_encrypt_or_nothing_that_requires_it_is_accepted(string location, string value)
    {
        RuntimeCheckpointCommitValidator.Validate(Commit(Locations[location].Build(AcceptedValues[value]())));
    }

    [Theory]
    [InlineData(DurableValueStorage.Inline)]
    [InlineData(DurableValueStorage.External)]
    public void A_durable_value_that_requires_encryption_is_refused_with_its_payload(DurableValueStorage storage)
    {
        var exception = Assert.Throws<RuntimeCheckpointCommitValidationException>(() =>
            RuntimeCheckpointCommitValidator.Validate(Commit(DurableValueChanges(DurableValue(storage, requiresEncryption: true)))));

        Assert.Equal("VF-ACT-005: Durable value 'durable-output:token' requires encryption, so it cannot be committed in plain text.", exception.Message);
        Assert.DoesNotContain(Sentinel, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_durable_value_that_does_not_require_encryption_is_accepted() =>
        RuntimeCheckpointCommitValidator.Validate(Commit(DurableValueChanges(DurableValue(DurableValueStorage.Inline, requiresEncryption: false))));

    [Fact]
    public void A_durable_explicit_null_that_requires_encryption_is_accepted() =>
        RuntimeCheckpointCommitValidator.Validate(Commit(DurableValueChanges(DurableValue(
            DurableValueStorage.Inline,
            requiresEncryption: true,
            JsonSerializer.SerializeToElement<object?>(null)))));

    [Fact]
    public void A_deleted_durable_value_is_not_written_so_it_is_not_judged() =>
        RuntimeCheckpointCommitValidator.Validate(Commit(DurableValueChanges(
            DurableValue(DurableValueStorage.Inline, requiresEncryption: true),
            RuntimeStateChangeOperation.Delete)));

    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueProtectionPolicy EncryptionRequired =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
    private static readonly ValueProtectionPolicy SensitiveOnly =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
    private const string WorkflowExecutionId = "wfexec-1";
    private const string ActivityExecutionId = "actexec-1";

    private static readonly IReadOnlyDictionary<string, Func<ValueEnvelope>> Payloads = new Dictionary<string, Func<ValueEnvelope>>
    {
        ["inline"] = () => ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(Sentinel), EncryptionRequired),
        ["external"] = () => ValueEnvelope.External(StringType, ExternalReference(), EncryptionRequired)
    };

    private static readonly IReadOnlyDictionary<string, Func<ValueEnvelope>> AcceptedValues = new Dictionary<string, Func<ValueEnvelope>>
    {
        ["withheld for encryption"] = () => ValueEnvelope.Withheld(StringType, new WithheldValue(WithheldValueKind.PolicyRequiresEncryption), EncryptionRequired),
        ["withheld secret reference"] = () => ValueEnvelope.Withheld(StringType, WithheldValue.SecretReference(new RuntimeSecretReference("payments.api-key"), null), EncryptionRequired),
        ["explicit null"] = () => ValueEnvelope.Null(StringType, EncryptionRequired),
        // Sensitive values are masked by the surfaces that render them; the backstop is about encryption only.
        ["sensitive only"] = () => ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(Sentinel), SensitiveOnly)
    };

    private static readonly IReadOnlyDictionary<string, Location> Locations = new Dictionary<string, Location>
    {
        ["input"] = new("ActivityExecutionState.InputSnapshot.Values[]", $"Activity execution '{ActivityExecutionId}' input 'apiKey'", value => ActivityChanges(ActivityState() with
        {
            InputSnapshot = new ActivityInputSnapshot(ActivityExecutionId, "contract", "binding", new Dictionary<string, ValueEnvelope> { ["apiKey"] = value }, OccurredAt)
        })),
        ["completion result"] = new("ActivityExecutionState.Completion.Result", $"Activity execution '{ActivityExecutionId}' completion result", value => ActivityChanges(ActivityState() with
        {
            Completion = new ActivityCompletion(ActivityExecutionId, "attempt-1", value, "Done", OccurredAt, "contract")
        })),
        ["private state"] = new("ActivityExecutionState.PrivateState.Value", $"Activity execution '{ActivityExecutionId}' private state", value => ActivityChanges(ActivityState() with
        {
            PrivateState = new ActivityPrivateState(ActivityExecutionId, 1, value, "attempt-1", OccurredAt)
        })),
        ["trigger delivery"] = new("ActivityExecutionState.TriggerDeliveries[].Payload", $"Activity execution '{ActivityExecutionId}' trigger delivery 'delivery-1'", value => ActivityChanges(ActivityState() with
        {
            TriggerDeliveries =
            [
                new ActivityTriggerDelivery("delivery-1", "registration-1", StringType, value, "provider", OccurredAt, "dedup-1", ActivityTriggerDeliveryStatus.Received)
            ]
        })),
        ["variable"] = new("ActivityExecutionState.VariableFrame.Values[]", $"Activity execution '{ActivityExecutionId}' variable 'token'", value => ActivityChanges(ActivityState() with
        {
            VariableFrame = Frame(VariableFrameKind.Container, value)
        })),
        ["iteration variable"] = new("ActivityExecutionState.IterationVariableFrame.Values[]", $"Activity execution '{ActivityExecutionId}' iteration variable 'token'", value => ActivityChanges(ActivityState() with
        {
            IterationVariableFrame = Frame(VariableFrameKind.Iteration, value)
        })),
        ["iteration variable request"] = new("ActivityExecutionState.IterationFrameRequest.Values[]", $"Activity execution '{ActivityExecutionId}' iteration variable request 'token'", value => ActivityChanges(ActivityState() with
        {
            IterationFrameRequest = new LoopIterationScopeRequest("loop", "iteration-1", new Dictionary<string, ValueEnvelope> { ["token"] = value })
        })),
        ["root variable"] = new("WorkflowExecutionState.RootVariableFrame.Values[]", $"Workflow execution '{WorkflowExecutionId}' root variable 'token'", value => new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, WorkflowState(Frame(VariableFrameKind.Root, value)), new Dictionary<string, string>()),
            null, [], [], [], [], []))
    };

    public static TheoryData<string, string> EncryptionRequiredRows() => Rows(Payloads.Keys);

    public static TheoryData<string, string> AcceptedRows() => Rows(AcceptedValues.Keys);

    private static TheoryData<string, string> Rows(IEnumerable<string> values)
    {
        var rows = new TheoryData<string, string>();
        foreach (var location in Locations.Keys)
        foreach (var value in values)
            rows.Add(location, value);
        return rows;
    }

    private static RuntimeCheckpointCommit Commit(RuntimeCheckpointStateChangeSet changes) => new(
        "commit-a",
        new RuntimeCheckpoint("checkpoint-a", "Checkpoint", WorkflowExecutionId, OccurredAt, [], new Dictionary<string, string>()),
        changes,
        [],
        new Dictionary<string, string>());

    private static RuntimeCheckpointStateChangeSet ActivityChanges(ActivityExecutionState state) =>
        new(null, null, [new RuntimeStateChange<ActivityExecutionState>(ActivityExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>())], [], [], [], []);

    private static RuntimeCheckpointStateChangeSet DurableValueChanges(
        DurableValueState state,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert) =>
        new(null, null, [], [], [new RuntimeStateChange<DurableValueState>(state.DurableValueId, operation, state, new Dictionary<string, string>())], [], []);

    private static DurableValueState DurableValue(DurableValueStorage storage, bool requiresEncryption, JsonElement? inlineValue = null)
    {
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.OutputName] = "token" };
        if (requiresEncryption)
            metadata[RuntimeMetadataKeys.RequiresEncryption] = bool.TrueString;
        var external = storage == DurableValueStorage.External;
        return new DurableValueState(
            "durable-output:token",
            WorkflowExecutionId,
            "output:token",
            new RuntimeValueTypeDescriptor("alias", "String", null),
            DurableValueLifecycle.Instance,
            storage,
            external ? null : inlineValue ?? JsonSerializer.SerializeToElement(Sentinel),
            external ? ExternalReference() : null,
            ActivityExecutionId,
            OccurredAt,
            metadata);
    }

    private static DurableValueExternalReference ExternalReference() =>
        new("payloads", "locator-1", new Dictionary<string, string>());

    private static VariableFrameState Frame(VariableFrameKind kind, ValueEnvelope value) =>
        new("frame-1", "scope-1", "activation-1", kind == VariableFrameKind.Root ? null : "frame-root", kind, new Dictionary<string, ValueEnvelope> { ["token"] = value });

    private static WorkflowExecutionState WorkflowState(VariableFrameState rootFrame) =>
        new(
            WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Running,
            null,
            OccurredAt,
            OccurredAt,
            OccurredAt,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        {
            RootVariableFrame = rootFrame
        };

    private static ActivityExecutionState ActivityState() =>
        new(
            new ActivityExecution(ActivityExecutionId, WorkflowExecutionId, "node-1", "node-1", "Test.Activity", "1.0.0"),
            ActivityExecutionStatus.Running,
            null,
            OccurredAt,
            OccurredAt,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    /// <summary>
    /// Where a commit carries a value: the state member, as <see cref="EnvelopeMembers(Type)"/> names it, the subject the
    /// refusal names for it, and a commit that carries the value there.
    /// </summary>
    private sealed record Location(string Member, string Subject, Func<ValueEnvelope, RuntimeCheckpointStateChangeSet> Build);

    /// <summary>
    /// Every public instance property path under <paramref name="root"/> that can hold a <see cref="ValueEnvelope"/>,
    /// descending into Elsa types and into the elements of collections and dictionary values (written <c>[]</c>), and
    /// stopping at the envelope itself. A type already on the path is not entered again.
    /// </summary>
    private static IEnumerable<string> EnvelopeMembers(Type root) => EnvelopeMembers(root, root.Name, new HashSet<Type> { root });

    private static IEnumerable<string> EnvelopeMembers(Type type, string path, IReadOnlySet<Type> onPath) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .SelectMany(property => EnvelopeCarriers(property.PropertyType, $"{path}.{property.Name}", onPath));

    private static IEnumerable<string> EnvelopeCarriers(Type type, string path, IReadOnlySet<Type> onPath)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(ValueEnvelope))
            return [path];
        if (ElementType(type) is { } element)
            return EnvelopeCarriers(element, $"{path}[]", onPath);
        if (type.IsEnum || onPath.Contains(type) || type.Namespace?.StartsWith("Elsa.", StringComparison.Ordinal) != true)
            return [];
        return EnvelopeMembers(type, path, new HashSet<Type>(onPath) { type });
    }

    // A dictionary's value type, or an enumerable's element type; a dictionary is matched first, because it is also an
    // enumerable of key-value pairs.
    private static Type? ElementType(Type type)
    {
        if (type == typeof(string))
            return null;
        var interfaces = type.GetInterfaces().Append(type).Where(candidate => candidate.IsGenericType).ToArray();
        var dictionary = interfaces.FirstOrDefault(candidate => candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        if (dictionary is not null)
            return dictionary.GetGenericArguments()[1];
        return interfaces.FirstOrDefault(candidate => candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private static RuntimeCheckpointCommit Commit(IReadOnlyList<RuntimePostCommitIntent> intents) => new(
        "commit-a",
        new RuntimeCheckpoint("checkpoint-a", "Checkpoint", "wfexec-1", OccurredAt, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        intents,
        new Dictionary<string, string>());

    private static RuntimePostCommitIntent Intent(string intentId, string kind) =>
        new(intentId, "wfexec-1", kind, OccurredAt, null, null, null);
}
