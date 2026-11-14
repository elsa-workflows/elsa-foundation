using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Microsoft.Extensions.Options;
using Xunit;
using Process = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>Projection and authorization controls; actual host execution is a separate integration gate.</summary>
public sealed class BpmnAuthoredDirectPublisherTests
{
    // Match the normal JsonPayloadSerializer writer: missing nullable defaults are omitted.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authored_explicit_null_defaults_are_refused_before_lossy_projection_or_publication(bool immutableSource)
    {
        var fixture = new Fixture();
        fixture.Version.State = fixture.Version.State with
        {
            Inputs = [new("value", "value", new("String"), null, "Value", null, false,
                DefaultValue: JsonSerializer.SerializeToElement<string?>(null), IsRequired: true)]
        };
        if (immutableSource) fixture.Version.StateSource = JsonSerializer.Serialize(fixture.Version.State, Web);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Contains("explicit-null defaults", error.Message);
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Authored_null_default_case_aliases_are_refused_before_nullable_deserialization()
    {
        var fixture = new Fixture();
        var source = JsonSerializer.SerializeToNode(fixture.Version.State, Web)!.AsObject();
        source.Remove("inputs");
        source["Inputs"] = JsonSerializer.SerializeToNode(new[] { new
        {
            referenceKey = "value", name = "value", displayName = "Value", type = new { alias = "String" },
            isNullable = false, isRequired = true, DefaultValue = (string?)null
        } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        fixture.Version.StateSource = source.ToJsonString();
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Contains("explicit-null defaults", error.Message); Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Publication_uses_the_immutable_version_and_presentation_without_mutating_authoring()
    {
        var fixture = new Fixture();
        var before = JsonSerializer.Serialize(fixture.Version.State, Web);
        var outcome = await fixture.Publish();
        Assert.StartsWith("activation-ref:bpmn-publication:", outcome.SourceReferenceId);
        Assert.Equal(1, fixture.Publications);
        Assert.Equal(["version", "definition", "authorize:bpmn:publish", "authorize:workflows/publishing:manage", "catalog", "visible", "layout", "prepare", "activate"], fixture.Calls);
        Assert.Equal("process-definition", outcome.Publication!.DefinitionId);
        Assert.Equal("version-1", outcome.Publication.VersionId);
        Assert.Equal("default", outcome.Publication.SlotName);
        Assert.Equal(new BpmnModelCaller("author", null), fixture.Caller);
        Assert.Equal("version-1", fixture.Source!.VersionId);
        Assert.Equal("1.0.0", fixture.Source.Version);
        Assert.Equal("Frozen process label", Assert.Single(fixture.Source.ActivityPresentation).DisplayName);
        Assert.Contains(fixture.Source.Layout, record => record.NodeId == "bpmn-diagram:root" &&
            record.AdditionalProperties!.Value.GetProperty("shapes").GetProperty("human").GetProperty("x").GetInt32() == 20);
        Assert.Equal("keep", fixture.Source.Layout[0].AdditionalProperties!.Value.GetProperty("opaque").GetString());
        Assert.Equal(1, fixture.Model!.FormatVersion);
        Assert.Equal(["human:human"], fixture.Model.HumanBindings);
        var process = fixture.Model.Process;
        Assert.Equal("process-definition", process.GetProperty("processId").GetString());
        var human = process.GetProperty("elements").EnumerateArray().Single(value => value.GetProperty("elementId").GetString() == "human");
        Assert.Equal("human:human", human.GetProperty("bindingRef").GetString());
        Assert.Empty(human.GetProperty("properties").EnumerateObject());
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Version.State, Web));
    }

    [Theory]
    [InlineData("{\"formatVersion\":99,\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":\"1\",\"kind\":\"human\"}")]
    [InlineData("{\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"nativeService\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":\"false\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"unknownMeaning\":true}")]
    public async Task Unsupported_binding_meaning_is_refused_before_runtime_publication(string metadata)
    {
        var fixture = new Fixture(metadata);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Claim_required_human_work_selects_the_explicit_claim_format(bool claimRequired)
    {
        var fixture = new Fixture("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":" + (claimRequired ? "true" : "false") + "}");
        await fixture.Publish();
        Assert.Equal(1, fixture.Publications);
        Assert.Equal(claimRequired ? 6 : 1, fixture.Model!.FormatVersion);
        Assert.Equal(["human:human"], fixture.Model.HumanBindings);
        if (claimRequired) Assert.Equal(["human:human"], fixture.Model.ClaimBindings);
        else Assert.Null(fixture.Model.ClaimBindings);
        var human = fixture.Model.Process.GetProperty("elements").EnumerateArray().Single(value => value.GetProperty("elementId").GetString() == "human");
        Assert.Empty(human.GetProperty("properties").EnumerateObject());
    }

    [Theory]
    [InlineData("bpmn:publish")]
    [InlineData("workflows/publishing:manage")]
    public async Task Resource_denial_precedes_source_parsing_catalog_and_layout_reads(string denied)
    {
        var fixture = new Fixture { DeniedPermission = denied };
        fixture.Version.StateSource = "malformed source that must not be parsed for a denied caller";
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
        Assert.DoesNotContain("catalog", fixture.Calls);
        Assert.DoesNotContain("layout", fixture.Calls);
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("definition")]
    [InlineData("layout")]
    public async Task Foreign_tenant_sources_never_reach_publication(string foreign)
    {
        var fixture = new Fixture();
        if (foreign == "version") fixture.Version.TenantId = "other-tenant";
        if (foreign == "definition") fixture.Definition.TenantId = "other-tenant";
        if (foreign == "layout") fixture.Layout.TenantId = "other-tenant";
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData(false, "current")]
    [InlineData(true, "current")]
    [InlineData(false, "scope")]
    [InlineData(true, "scope")]
    [InlineData(false, "replacement")]
    [InlineData(true, "replacement")]
    [InlineData(false, "slot-address")]
    [InlineData(true, "slot-address")]
    [InlineData(false, "missing-authority")]
    [InlineData(true, "missing-authority")]
    public async Task Authored_worker_selection_proves_the_actual_logical_slot_for_closed_and_input_models(bool inputs, string change)
    {
        var fixture = new Fixture();
        fixture.UseService(ServiceMetadata().ToJsonString());
        if (inputs) fixture.Version.State = fixture.Version.State with
        {
            Inputs = [new("text", "text", new("String"), null, "Text", null, false, IsRequired: true)]
        };
        const string definition = "logical-worker", publication = "worker-publication";
        fixture.WorkerSlot = new(WorkflowActivationSlotIdentity.Create(definition, "blue"), definition, "blue", publication,
            WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow);
        fixture.WorkerSource = fixture.WorkerSource! with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(publication), DefinitionId = definition,
            DefinitionVersionId = "logical-version", ActivationId = publication, SlotId = fixture.WorkerSlot.SlotId,
            PublicationScope = new("scope")
        };
        var metadata = ServiceMetadata(); metadata["sourceReferenceId"] = fixture.WorkerSource.SourceReferenceId;
        fixture.UseService(metadata.ToJsonString());
        if (change == "scope") fixture.WorkerSource = fixture.WorkerSource with { PublicationScope = new("other") };
        if (change == "replacement") fixture.WorkerSlot = fixture.WorkerSlot with { ActiveActivationId = "later" };
        if (change == "slot-address")
        {
            fixture.WorkerSource = fixture.WorkerSource with { SlotId = "forged" };
            fixture.WorkerSlot = fixture.WorkerSlot with { SlotId = "forged" };
        }
        if (change == "missing-authority") fixture.WorkerSlot = null;
        if (change == "current")
        {
            await fixture.Publish();
            Assert.Equal("worker-definition", Assert.Single(fixture.Model!.ServiceBindings!).Executable.DefinitionId);
            Assert.Equal(1, fixture.Publications);
        }
        else
        {
            await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
            Assert.Equal(0, fixture.Publications);
            Assert.DoesNotContain("prepare", fixture.Calls);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invisible")]
    [InlineData("wrong-type")]
    [InlineData("new-contract")]
    public async Task A_shape_alone_cannot_impersonate_a_visible_compatible_root_activity(string invalid)
    {
        var fixture = new Fixture { MissingActivity = invalid == "missing", Visible = invalid != "invisible" };
        if (invalid == "wrong-type") fixture.CatalogDefinition = new ActivityDefinitionModel("activity", "Another.Activity", "Test", null, null);
        if (invalid == "new-contract") fixture.Activity = fixture.Activity with { Version = "2.0.0" };
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task A_version_reader_need_not_hydrate_its_parent_definition_for_publication()
    {
        var fixture = new Fixture();
        fixture.Activity = fixture.Activity with { Definition = null! };
        await fixture.Publish();
        Assert.Equal(1, fixture.Publications);
        Assert.Contains("visible", fixture.Calls);
    }

    [Fact]
    public async Task Catalog_validation_uses_the_actual_immutable_source_instead_of_a_conflicting_cached_state()
    {
        var fixture = new Fixture();
        fixture.Version.StateSource = JsonSerializer.Serialize(fixture.Version.State with
            { RootActivity = fixture.Version.State.RootActivity! with { ActivityVersionId = "not-a-bpmn-version" } }, Web);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Unknown_authored_fields_are_refused_instead_of_silently_dropped()
    {
        var fixture = new Fixture();
        var json = JsonSerializer.SerializeToNode(fixture.Version.State, Web)!;
        json["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!["script"] = "unrepresented behavior";
        fixture.Version.StateSource = json.ToJsonString();
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Native_child_intent_and_source_owned_content_are_not_reinterpreted()
    {
        var fixture = new Fixture();
        fixture.Definition.IsSourceOwned = true;
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        fixture.Definition.IsSourceOwned = false;
        var json = JsonSerializer.SerializeToNode(fixture.Version.State, Web)!;
        json["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!["childNodeId"] = "native-child";
        fixture.Version.StateSource = json.ToJsonString();
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("HoldForRetry", 3)]
    [InlineData("FinalFault", 1)]
    public async Task Authored_service_retains_its_exact_selected_pin_source_and_fault_policy(string disposition, int attempts)
    {
        var fixture = new Fixture();
        var metadata = ServiceMetadata();
        metadata["faultPolicy"]!["disposition"] = disposition;
        metadata["faultPolicy"]!["maxAttempts"] = attempts;
        fixture.UseService(metadata.ToJsonString());
        var before = fixture.Version.StateSource;

        await fixture.Publish();

        Assert.Equal(3, fixture.Model!.FormatVersion);
        Assert.Empty(fixture.Model.HumanBindings);
        var binding = Assert.Single(fixture.Model.ServiceBindings!);
        Assert.Equal("service:human", binding.BindingId);
        Assert.Equal(new("worker-artifact", "worker-definition", "worker-version", "1.0.0", new string('a', 64)), binding.Executable);
        Assert.Equal("etl-business-failure", binding.BusinessErrorCode);
        Assert.Equal(disposition, binding.FaultPolicy!.Disposition.ToString());
        Assert.Equal(attempts, binding.FaultPolicy.MaxAttempts);
        Assert.Contains("source:worker-source", fixture.Calls);
        Assert.Contains("authorize:workflows/runtime:read", fixture.Calls);
        var element = fixture.Model.Process.GetProperty("elements").EnumerateArray().Single(value => value.GetProperty("elementId").GetString() == "human");
        Assert.Equal("service:human", element.GetProperty("bindingRef").GetString());
        Assert.Empty(element.GetProperty("properties").EnumerateObject());
        Assert.Equal(before, fixture.Version.StateSource);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("retired")]
    [InlineData("expired")]
    [InlineData("test")]
    [InlineData("artifact")]
    [InlineData("definition")]
    [InlineData("version")]
    [InlineData("label")]
    [InlineData("identity")]
    [InlineData("denied")]
    public async Task Service_selection_requires_the_exact_live_authorized_published_source(string reason)
    {
        var fixture = new Fixture();
        fixture.UseService(ServiceMetadata().ToJsonString());
        var source = fixture.WorkerSource!;
        fixture.WorkerSource = reason switch
        {
            "missing" => null,
            "foreign" => source with { TenantId = "other-tenant" },
            "retired" => source with { DeletedAt = DateTimeOffset.MinValue },
            "expired" => source with { ExpiresAt = DateTimeOffset.MinValue },
            "test" => source with { Scope = WorkflowExecutableReferenceScope.TestRun },
            "artifact" => source with { ArtifactId = "another-artifact" },
            "definition" => source with { DefinitionId = "another-definition" },
            "version" => source with { DefinitionVersionId = "another-version" },
            "label" => source with { ArtifactVersion = "2.0.0" },
            "identity" => source with { SourceReferenceId = "another-source" },
            _ => source
        };
        if (reason == "denied") fixture.DeniedPermission = "workflows/runtime:read";

        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("pin-unknown")]
    [InlineData("pin-missing")]
    [InlineData("blank-source")]
    [InlineData("policy-missing")]
    [InlineData("policy-unknown")]
    [InlineData("policy-version")]
    [InlineData("policy-disposition")]
    [InlineData("policy-number")]
    [InlineData("policy-budget")]
    [InlineData("policy-final-budget")]
    [InlineData("duplicate")]
    [InlineData("duplicate-pin")]
    [InlineData("duplicate-policy")]
    public async Task Service_metadata_is_a_closed_versioned_shape_without_implicit_defaults(string invalid)
    {
        var fixture = new Fixture();
        var metadata = ServiceMetadata();
        switch (invalid)
        {
            case "unknown": metadata["other"] = true; break;
            case "pin-unknown": metadata["executable"]!["other"] = true; break;
            case "pin-missing": metadata["executable"]!.AsObject().Remove("artifactHash"); break;
            case "blank-source": metadata["sourceReferenceId"] = " "; break;
            case "policy-missing": metadata.Remove("faultPolicy"); break;
            case "policy-unknown": metadata["faultPolicy"]!["other"] = true; break;
            case "policy-version": metadata["faultPolicy"]!["formatVersion"] = 2; break;
            case "policy-disposition": metadata["faultPolicy"]!["disposition"] = "holdForRetry"; break;
            case "policy-number": metadata["faultPolicy"]!["maxAttempts"] = "3"; break;
            case "policy-budget": metadata["faultPolicy"]!["maxAttempts"] = 33; break;
            case "policy-final-budget": metadata["faultPolicy"]!["disposition"] = "FinalFault"; break;
        }
        var json = metadata.ToJsonString();
        if (invalid == "duplicate") json = json.Replace("\"kind\":", "\"kind\":\"nativeService\",\"kind\":", StringComparison.Ordinal);
        if (invalid == "duplicate-pin") json = json.Replace("\"artifactId\":", "\"artifactId\":\"other\",\"artifactId\":", StringComparison.Ordinal);
        if (invalid == "duplicate-policy") json = json.Replace("\"maxAttempts\":", "\"maxAttempts\":1,\"maxAttempts\":", StringComparison.Ordinal);
        fixture.UseService(json);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    private static JsonObject ServiceMetadata() => JsonSerializer.SerializeToNode(new
    {
        formatVersion = 1, kind = "nativeService", sourceReferenceId = "worker-source",
        executable = new WorkflowExecutableIdentity("worker-artifact", "worker-definition", "worker-version", "1.0.0", new string('a', 64)),
        businessErrorCode = "etl-business-failure", faultPolicy = new { formatVersion = 1, disposition = "HoldForRetry", maxAttempts = 3 }
    }, Web)!.AsObject();

    [Fact]
    public async Task Declared_model_inputs_and_version_two_worker_mapping_are_projected_without_native_bindings()
    {
        var fixture = new Fixture();
        fixture.Version.State = fixture.Version.State with
        {
            Inputs = [new("text", "text", new("String"), null, "Text", null, false, IsRequired: true)]
        };
        var metadata = ServiceMetadata();
        metadata["formatVersion"] = 2;
        metadata["inputs"] = new JsonObject
        {
            ["message"] = new JsonObject { ["kind"] = "Input", ["name"] = "text" },
            ["count"] = new JsonObject { ["kind"] = "Literal", ["value"] = 7 }
        };
        fixture.UseService(metadata.ToJsonString());
        await fixture.Publish();
        var declared = Assert.Single(fixture.InputContract!.Inputs);
        Assert.Equal("text", declared.Name);
        Assert.True(declared.IsRequired);
        var mapping = Assert.Single(fixture.Mappings!);
        Assert.Equal("service:human", mapping.BindingId);
        Assert.Equal("text", mapping.Inputs["message"].Name);
        Assert.Equal(7, mapping.Inputs["count"].Value!.Value.GetInt32());
        Assert.Equal("process-definition", fixture.Activation!.Reference.DefinitionId);
        Assert.Equal("producer-definition", fixture.Activation.Material.Identity.DefinitionId);
        Assert.Equal(fixture.Activation.Candidate.PublicationId, fixture.Activation.Reference.ActivationId);
        Assert.Equal(fixture.Activation.Candidate.SlotId, fixture.Activation.Reference.SlotId);
    }

    [Theory]
    [InlineData("{\"kind\":\"Expression\",\"name\":\"text\"}")]
    [InlineData("{\"kind\":\"Input\",\"name\":\"text\",\"value\":7}")]
    [InlineData("{\"kind\":\"Input\",\"name\":\"text\",\"name\":\"other\"}")]
    [InlineData("{\"kind\":\"Literal\"}")]
    public async Task Worker_input_sources_refuse_unversioned_or_ambiguous_meaning(string input)
    {
        var fixture = new Fixture();
        var metadata = ServiceMetadata().ToJsonString().Replace("\"formatVersion\":1,\"kind\":\"nativeService\"",
            "\"formatVersion\":2,\"kind\":\"nativeService\",\"inputs\":{\"message\":" + input + "}", StringComparison.Ordinal);
        fixture.UseService(metadata);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("publication")]
    [InlineData("revision")]
    [InlineData("omitted-revision")]
    [InlineData("intent")]
    [InlineData("action")]
    [InlineData("slot")]
    public async Task A_reused_operation_id_binds_the_exact_supplied_publication_intent(string change)
    {
        var fixture = new Fixture();
        var originalIntent = change is "action" or "slot" ? new PublicationRequestIntent(PublicationAction.Replace) : null;
        long? originalRevision = change == "omitted-revision" ? 0 : null;
        await fixture.Publish(originalIntent, originalRevision);
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => fixture.Publish(
            change switch
            {
                "intent" => new(PublicationAction.Replace),
                "action" => new(PublicationAction.PublishSideBySide),
                "slot" => new(PublicationAction.Replace, "default"),
                _ => originalIntent
            }, change == "revision" ? 0 : change == "omitted-revision" ? null : originalRevision,
            change == "publication" ? "different-serving-publication" : null));
        Assert.Equal(1, fixture.Publications);
    }

    [Fact]
    public async Task Exact_replay_keeps_its_accepted_slot_after_policy_and_authority_advance()
    {
        var fixture = new Fixture();
        var original = await fixture.Publish(expectedSlotRevision: 0);
        fixture.ResolvedSlotName = "new-default";
        fixture.Slot = fixture.Slot! with { Revision = 9, ActiveActivationId = "later-publication" };
        var replay = await fixture.Publish(expectedSlotRevision: 0);
        Assert.Equal(original.Publication!.PublicationId, replay.Publication!.PublicationId);
        Assert.Equal("default", replay.Publication.SlotName);
        Assert.Equal(1, fixture.Publications);
        Assert.Equal(1, fixture.PolicyResolutions);
    }

    [Theory]
    [InlineData(PublicationStatus.Candidate)]
    [InlineData(PublicationStatus.Failed)]
    [InlineData(PublicationStatus.Active)]
    [InlineData(PublicationStatus.Retired)]
    public async Task Every_retained_status_refuses_changed_or_unverifiable_intent(PublicationStatus status)
    {
        var fixture = new Fixture();
        await fixture.Publish();
        fixture.RetainedPublication = fixture.RetainedPublication! with { Status = status };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => fixture.Publish(expectedSlotRevision: 0));
        fixture.RetainedPublication = fixture.RetainedPublication with { RequestFingerprint = null };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => fixture.Publish());
        Assert.Equal(1, fixture.Publications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_create_losers_reconcile_the_actual_create_only_journal(bool changedIntent)
    {
        var fixture = new Fixture { Journal = new InMemoryPublicationRecordStore() };
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        fixture.BeforeSave = _ =>
        {
            if (Interlocked.Increment(ref entered) == 1) firstEntered.SetResult();
            else release.SetResult();
            return release.Task;
        };
        var first = fixture.Publish();
        await firstEntered.Task;
        var second = fixture.Publish(expectedSlotRevision: changedIntent ? 0 : null);
        if (changedIntent)
        {
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => Task.WhenAll(first, second));
            Assert.Single(new[] { first, second }, task => task.IsCompletedSuccessfully);
            Assert.Single(new[] { first, second }, task => task.Exception?.InnerException is RuntimeCheckpointReplayConflictException);
        }
        else
        {
            var results = await Task.WhenAll(first, second);
            Assert.Equal(results[0].Publication!.PublicationId, results[1].Publication!.PublicationId);
        }
        Assert.Equal(1, fixture.Publications);
    }

    [Fact]
    public async Task An_unrelated_failure_after_journaling_is_preserved_without_repeated_activation()
    {
        var failure = new InvalidOperationException("actual storage/lifecycle failure");
        var fixture = new Fixture { Journal = new InMemoryPublicationRecordStore(), AfterSave = () => throw failure };
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Publish()));
        Assert.Equal(1, fixture.Calls.Count(call => call == "activate"));
        Assert.Equal(0, fixture.Publications);
    }

    private sealed class Scope : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("scope")); }

    private sealed class Fixture : IWorkflowDefinitionVersionStore, IWorkflowDefinitionStore,
        IWorkflowDefinitionVersionLayoutStore, IActivityDefinitionLookup, IPermissionAuthorizationService, IBpmnModelRuntime,
        IWorkflowExecutableSourceReferenceReader
    {
        public List<string> Calls { get; } = [];
        public WorkflowDefinitionVersion Version { get; }
        public WorkflowDefinition Definition { get; } = new() { Id = "process-definition", Name = "Mutable definition label" };
        public WorkflowDefinitionVersionLayout Layout { get; } = new()
        {
            Id = "layout", WorkflowDefinitionVersionId = "version-1",
            Records = [new("root", 1, 2, AdditionalProperties: JsonSerializer.SerializeToElement(new { opaque = "keep" }))],
            ActivityPresentation = [new("root", "Frozen process label")]
        };
        public ActivityDefinitionVersionModel Activity { get; set; } = new("activity-version", "1.0.0", "activity", "clr", "1", "clr", "1",
            JsonSerializer.SerializeToElement(new { }), "CLR", "source",
            new ActivityDefinitionModel("activity", typeof(Process).FullName!, "BPMN", "BPMN Process", null), [], [], [], default, "hash");
        public IActivityDefinition CatalogDefinition { get; set; } = new ActivityDefinitionModel("activity", typeof(Process).FullName!, "BPMN", "BPMN Process", null);
        public string? DeniedPermission { get; set; }
        public bool MissingActivity { get; set; }
        public bool Visible { get; set; } = true;
        public int Publications { get; private set; }
        public PublicationRecord? RetainedPublication { get; set; }
        public IPublicationRecordStore? Journal { get; set; }
        public Func<PublicationMaterialActivationRequest, Task>? BeforeSave { get; set; }
        public Action? AfterSave { get; set; }
        public WorkflowActivationSlot? Slot { get; set; }
        public WorkflowActivationSlot? WorkerSlot { get; set; }
        public string ResolvedSlotName { get; set; } = "default";
        public int PolicyResolutions { get; private set; }
        public BpmnModelDefinition? Model { get; private set; }
        public BpmnModelPublicationProvenance? Source { get; private set; }
        public BpmnModelCaller? Caller { get; private set; }
        public string? RequestId { get; private set; }
        public WorkflowExecutableInputContract? InputContract { get; private set; }
        public IReadOnlyList<BpmnWorkerInputMapping>? Mappings { get; private set; }
        public PublicationMaterialActivationRequest? Activation { get; private set; }
        public WorkflowExecutableSourceReference? WorkerSource { get; set; } = new("worker-source", "worker-artifact", "workflow-definition", "worker-definition",
            "1.0.0", "worker-definition", "worker-version", "1.0.0", DateTimeOffset.MinValue, DateTimeOffset.MinValue, WorkflowExecutableReferenceScope.Published);

        public Fixture(string metadata = "{\"formatVersion\":1,\"kind\":\"human\"}")
        {
            var structure = new BpmnAuthoredStructure(elements:
                [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, name: "Review order",
                    properties: new Dictionary<string, string> { ["elsa.direct.binding"] = metadata }), new("end", BpmnElementTypes.EndEvent)],
                sequenceFlows: [new("to-human", "start", "human"), new("to-end", "human", "end")],
                diagram: JsonSerializer.SerializeToElement(new { shapes = new { human = new { x = 20 } } }));
            Version = new("process-definition", "1.0.0") { Id = "version-1", State = new([], new("root", "activity-version", [], [],
                new(Process.StructureKind, Process.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web))), [], [], null) };
        }

        public Task<BpmnPublishedModel> Publish(PublicationRequestIntent? intent = null, long? expectedSlotRevision = null,
            string? expectedPublicationId = null)
        {
            var validator = new NormalizedPrincipalValidator(Options.Create(new FoundationIdentityOptions
                { NormalizedAuthenticationTypes = new HashSet<string>(["test"]) }));
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "author"), new(IdentityClaimTypes.Normalized, "v1")], "test"));
            async ValueTask<PublicationActivationResult> Activate(PublicationMaterialActivationRequest request)
            {
                Calls.Add("activate");
                Activation = request;
                var candidate = request.Candidate;
                if (BeforeSave is not null) await BeforeSave(request);
                if (Journal is not null) await Journal.SaveAsync(candidate);
                AfterSave?.Invoke();
                Publications++;
                Source = new(request.Reference.SourceId, request.Reference.SourceVersion!, request.Reference.Layout,
                    request.Reference.ActivityPresentation);
                RetainedPublication = candidate with { Status = PublicationStatus.Active };
                if (Journal is not null) Assert.True(await Journal.TryTransitionAsync(RetainedPublication, PublicationStatus.Candidate));
                Slot = new(candidate.SlotId, candidate.WorkflowDefinitionId, candidate.SlotName, candidate.PublicationId,
                    WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow);
                return new(true, RetainedPublication, Slot);
            }
            var activator = Stub<IPublicationActivator>((method, args) =>
            {
                Assert.Equal("ActivateMaterialAsync", method.Name);
                return Activate((PublicationMaterialActivationRequest)args![0]!);
            });
            return new BpmnAuthoredDirectPublisher(this, this, this, this, validator, this, this, this, TimeProvider.System,
                activator, Journal ?? Stub<IPublicationRecordStore>((_, _) => ValueTask.FromResult(RetainedPublication)),
                Stub<IWorkflowActivationAuthority>((method, _) => method.Name == "ListByDefinitionAsync"
                    ? ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>(WorkerSlot is null ? [] : [WorkerSlot])
                    : (object)ValueTask.FromResult(Slot)),
                Stub<IPublicationPolicyStore>((_, _) => ValueTask.FromResult<PublicationPolicy?>(null)),
                Stub<IPublicationPolicyResolver>((_, args) =>
                {
                    PolicyResolutions++;
                    return new ResolvedPublicationAction((string)args![0]!, (string)args[1]!,
                        PublicationAction.Replace, ResolvedSlotName, PublicationPolicySource.Host, 0);
                }), new Scope())
                .PublishAsync("version-1", "operation", principal, default, intent, expectedSlotRevision, expectedPublicationId);
        }

        public void UseService(string metadata)
        {
            var state = JsonSerializer.SerializeToNode(Version.State, Web)!;
            var task = state["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!;
            task["elementType"] = BpmnElementTypes.ServiceTask;
            task["properties"]!["elsa.direct.binding"] = metadata;
            Version.StateSource = state.ToJsonString();
        }

        Task<WorkflowDefinitionVersion?> IWorkflowDefinitionVersionStore.FindByIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("version"); return Task.FromResult<WorkflowDefinitionVersion?>(Version); }
        Task<WorkflowDefinition?> IWorkflowDefinitionStore.FindByIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("definition"); return Task.FromResult<WorkflowDefinition?>(Definition); }
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("layout"); return Task.FromResult<WorkflowDefinitionVersionLayout?>(Layout); }
        public Task<IActivityDefinitionVersion?> FindVersion(string id, CancellationToken cancellationToken)
        { Calls.Add("catalog"); return Task.FromResult<IActivityDefinitionVersion?>(!MissingActivity && id == Activity.Id ? Activity : null); }
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
        { Calls.Add("visible"); return Task.FromResult<IEnumerable<IActivityDefinition>>(Visible ? [CatalogDefinition] : []); }
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken)
        { Calls.Add("authorize:" + context.Permission); Assert.Same(context.Permission == "workflows/runtime:read" ? (object?)WorkerSource : Version, context.Resource); return ValueTask.FromResult(context.Permission == DeniedPermission ? PermissionEvaluationResult.Denied() : PermissionEvaluationResult.Success); }
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken)
        { Calls.Add("source:" + sourceReferenceId); return ValueTask.FromResult(WorkerSource); }
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnPublishedModel> PublishFromSourceAsync(BpmnModelDefinition definition, BpmnModelPublicationProvenance source, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Authored publication cannot mint a raw source reference.");
        public ValueTask<RuntimeExecutionMaterial> PreparePublicationAsync(BpmnModelDefinition definition, WorkflowExecutableInputContract inputContract,
            IReadOnlyList<BpmnWorkerInputMapping> mappings, BpmnModelCaller caller, CancellationToken cancellationToken)
        {
            Calls.Add("prepare"); Model = definition; Caller = caller; InputContract = inputContract; Mappings = mappings;
            var model = new RuntimeModelMaterial(1, new("artifact", "producer-definition", "producer-version", "1.0.0", "hash"),
                new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), "profile", JsonSerializer.SerializeToElement(definition, Web), inputContract, [], [], []);
            return ValueTask.FromResult(new RuntimeExecutionMaterial(model, "incarnation"));
        }
        Task<WorkflowDefinitionVersion> IWorkflowDefinitionVersionStore.GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string id, string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<WorkflowDefinition> IWorkflowDefinitionStore.GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IActivityDefinition> GetDefinition(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> StartAsync(string source, string request, IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string id, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string id, RuntimeStorePageRequest request, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string id, string request, string invocation, string attempt, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string id, string request, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        var result = DispatchProxy.Create<T, PublicationStub>();
        ((PublicationStub)(object)result).Handler = invoke;
        return result;
    }
    public class PublicationStub : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
