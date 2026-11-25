using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_native_start_keeps_policy_normalized_inputs_in_the_physical_seal_across_publication_and_retry(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var validator = new WorkflowExecutableInputValidator(new SavedStartPolicyTypes());
        // This declaration is optional: requiredness is checked before defaults.
        // Its explicit null default needs the original native DefaultOnNull policy.
        var contract = new WorkflowExecutableInputContract(1,
            [new("count", new("Int32"), false, JsonSerializer.SerializeToElement<object?>(null))]);
        var original = SavedPolicyMaterial(Pin, contract, new(DefaultOnNull: true));
        await harness.Artifacts.SaveAsync(original);
        ConfigureSavedPolicyStore(harness.Delivery, validator);
        // A retained native source binds physical scope. The legacy publisher fixture deliberately takes no-seal admission.
        // This test qualifies saved-start sealing, not the earlier publisher's admission.
        var publisher = Publisher with { Partition = new(Scope.Value), Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime.publish-test"),
            StimulusDeliveryOrigin = new(1, Scope, "earlier-source", new string('a', 64), "earlier-binding") };
        Assert.True(publisher.HasRetainedSourceScope(Scope));
        await harness.SeedRetainedPublisherForValuePolicies(publisher);
        var source = new PostCommitDeliveryContext(await harness.Seed(publisher: publisher), Scope);
        await harness.Delivery.InstallAsync(source, AdmissionSnapshot(source, publisher));
        var selected = (await harness.References.FindAsync("selected-reference"))!;
        var key = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId);
        var actors = new StartActors { ShedFirst = true };
        var dispatcher = new WorkflowStartDispatcher(harness.Artifacts, harness.References,
            actors, new NoStartIds(), harness.Clock, null, new StartPolicy(), null,
            harness.Executions, null, harness.Delivery, [new SavedStartIntentAdapter()], validator);

        var first = await dispatcher.DispatchAsync(SavedRequest(source, publisher: publisher));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, first.CommandDispatch.Status);
        var firstEnvelope = Assert.Single(actors.Envelopes);
        var firstPayload = firstEnvelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>(Elsa.Serialization.Core.JsonBaseline.Options)!;
        Assert.Equal(Pin, firstPayload.PinnedExecutable);
        Assert.Equal(SelectedSource(), firstPayload.PinnedSource);
        Assert.Equal(0, Assert.Single(firstPayload.Inputs).Value.GetInt32());
        var installed = (await harness.Lookup.FindDurableAsync(source.SourceOutboxItemId))!;
        var firstSeal = SavedStimulusStartSeal.Read(installed, key.WorkflowExecutionId);
        Assert.True(JsonElement.DeepEquals(firstEnvelope.Command.Payload.Value, firstSeal.Command.Payload!.Value));

        // A distinct, valid native publication now has different policies/defaults.
        // Retain the original immutable material and change only source/publication selection.
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var laterPin = Pin with
        {
            ArtifactId = "later-policy-artifact", ArtifactHash = "later-policy-hash",
            DefinitionVersionId = "later-policy-version"
        };
        await harness.Artifacts.SaveAsync(SavedPolicyMaterial(laterPin,
            new(1, [new("count", new("Int32"), false, JsonSerializer.SerializeToElement(9))]),
            WorkflowValuePolicies.Off));
        await harness.References.SaveAsync(selected with
        {
            SourceReferenceId = "later-policy-reference", ArtifactId = laterPin.ArtifactId,
            SourceId = laterPin.DefinitionVersionId, SourceVersion = "3",
            DefinitionVersionId = laterPin.DefinitionVersionId, ArtifactVersion = "later-label",
            ActivationId = "later-policy-publication"
        });

        var retried = await dispatcher.DispatchAsync(SavedRequest(source, publisher: publisher));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, retried.CommandDispatch.Status);
        Assert.Equal(Pin, retried.PinnedExecutable);
        Assert.Equal(SelectedSource(), retried.PinnedSource);
        Assert.Equal(2, actors.Envelopes.Count);
        var retryEnvelope = actors.Envelopes[1];
        Assert.Equal(firstEnvelope.EnvelopeId, retryEnvelope.EnvelopeId);
        Assert.Equal(firstEnvelope.Command.CommandId, retryEnvelope.Command.CommandId);
        Assert.Equal(firstEnvelope.IdempotencyKey, retryEnvelope.IdempotencyKey);
        Assert.True(JsonElement.DeepEquals(firstEnvelope.Command.Payload.Value, retryEnvelope.Command.Payload!.Value));

        var retained = (await harness.Lookup.FindDurableAsync(source.SourceOutboxItemId))!;
        var retrySeal = SavedStimulusStartSeal.Read(retained, key.WorkflowExecutionId);
        var retainedPayload = retrySeal.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>(Elsa.Serialization.Core.JsonBaseline.Options)!;
        Assert.Equal(0, retainedPayload.Inputs["count"].GetInt32());
        Assert.Equal(Pin, retainedPayload.PinnedExecutable);
        Assert.Equal(SelectedSource(), retainedPayload.PinnedSource);
        Assert.True(JsonElement.DeepEquals(firstSeal.Command.Payload!.Value, retrySeal.Command.Payload.Value));
    }

    private static WorkflowExecutable SavedPolicyMaterial(WorkflowExecutableIdentity identity,
        WorkflowExecutableInputContract contract, WorkflowValuePolicies policies)
    {
        var template = Material(identity);
        return new(template.Identity, template.RootActivity, template.ResumeTargets,
            template.CreatedAt, template.CompatibilityMetadata, contract, template.Dependencies,
            template.RuntimeRequirements, template.StorageDriverRequirements, template.IncidentStrategy,
            template.PersistenceMode, template.WorkflowVariables, policies);
    }

    private static void ConfigureSavedPolicyStore(IStimulusDeliveryStore store,
        IWorkflowExecutableInputValidator validator)
    {
        switch (store)
        {
            case InMemoryRuntimeCheckpointCommitStore memory:
                memory.StimulusAdapters = [new SavedStartIntentAdapter()];
                memory.StimulusInputValidator = validator;
                break;
            case EfRuntimePostCommitOutboxStore sql:
                sql.StimulusAdapters = [new SavedStartIntentAdapter()];
                sql.StimulusInputValidator = validator;
                break;
            default:
                throw new InvalidOperationException("This fixture must use its actual memory or EF saved-start provider.");
        }
    }

    private sealed partial class Harness
    {
        internal async Task SeedRetainedPublisherForValuePolicies(WorkflowExecutionState publisher)
        {
            // Construct retained header state only; no evidence of this publisher's earlier initial admission is claimed.
            Assert.True(publisher.HasRetainedSourceScope(Scope));
            await EnsureMaterial(publisher);
            if (Context is null)
            {
                await memory.ExecuteWriteGateAsync(async token =>
                {
                    using var fixtureWrite = memory.AuthorizeIngressCheckpointWrite(publisher);
                    return await publishers.SaveAsync(publisher, token);
                });
            }
            else
            {
                var id = EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(Scope.Value, publisher.WorkflowExecutionId);
                Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntityForWriteVersion(
                    publisher, Scope.Value, id, 1, RuntimeWorkflowExecutionEfModule.IngressSchemaVersion));
                await Context.SaveChangesAsync(); Context.ChangeTracker.Clear();
            }
            Assert.True((await FindPublisher())!.HasRetainedSourceScope(Scope));
        }
    }

    private sealed class SavedStartPolicyTypes : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = type == typeof(int) ? "Int32" : null!; return alias is not null; }
        public bool TryGetType(string alias, out Type type) { type = alias == "Int32" ? typeof(int) : null!; return type is not null; }
        public IEnumerable<Type> ListTypes() => [typeof(int)];
        public string GetAliasOrDefault(Type type) => type == typeof(int) ? "Int32" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => alias == "Int32" ? typeof(int) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
