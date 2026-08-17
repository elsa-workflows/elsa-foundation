using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Services;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class RuntimeRequirementPreflightTests
{
    private const string IntrinsicArtifactId = "artifact-intrinsic";

    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preflight_checks_every_live_retained_artifact_and_keeps_capability_kinds_separate()
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var templates = new InMemoryExecutableActivityTemplateStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        IRuntimeActivityConsumerCapability[] consumers =
        [
            new RuntimeActivityConsumerCapability("sample.available", ["1"]),
            new RuntimeActivityConsumerCapability("sample.schema", ["1"])
        ];
        var drivers = new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]);
        var executable = Executable(
            "artifact-1",
            [
                new("sample.available", "1"),
                new("sample.missing", "1"),
                new("sample.schema", "2")
            ],
            [new("elsa.json"), new("sample.external")]);
        await executables.SaveAsync(executable);
        var template = Template("template-1");
        await templates.SaveAsync(template);
        await references.SaveAsync(Reference("ref-live", executable.Identity.ArtifactId));
        await references.SaveAsync(Reference("ref-template", template.TemplateId) with
        {
            SourceKind = "ActivityDefinitionVersion",
            SourceId = "activity-version-1",
            DefinitionVersionId = "activity-version-1"
        });
        await references.SaveAsync(Reference("ref-retired", "artifact-retired") with { DeletedAt = Now.AddMinutes(-1), DeletedReason = "retired" });
        await references.SaveAsync(Reference("ref-missing", "artifact-missing"));
        var service = new RuntimeRequirementPreflight(references, executables, templates, new RuntimeRequirementChecker(consumers, drivers, new WellKnownTypeRegistry(), new JsonPayloadSerializer(new JsonPayloadConverterRegistry())), new FixedTimeProvider(Now));

        var result = await service.RunAsync(RuntimeRequirementPreflight.ActiveRetainedArtifactsScope, null);

        Assert.False(result.IsReady);
        Assert.Equal(3, result.CheckedArtifactCount);
        Assert.Collection(
            result.Requirements,
            requirement => Assert.Equal(("sample.available", "1", "Available", 2),
                (requirement.ConsumerKey, requirement.SchemaVersion, requirement.Status, requirement.AffectedArtifactCount)),
            requirement => Assert.Equal(("sample.missing", "1", "Missing", 1),
                (requirement.ConsumerKey, requirement.SchemaVersion, requirement.Status, requirement.AffectedArtifactCount)),
            requirement => Assert.Equal(("sample.schema", "2", "UnsupportedSchema", 1),
                (requirement.ConsumerKey, requirement.SchemaVersion, requirement.Status, requirement.AffectedArtifactCount)));
        Assert.Contains(result.Diagnostics, x => x.Code == "activity.preflight.artifact-missing" && x.Subject.Id == "artifact-missing");
        Assert.Contains(result.Diagnostics, x => x.Code == "activity.runtime.storage-driver-missing" && x.Subject.Id == "artifact-1");
        Assert.DoesNotContain(result.Diagnostics, x => x.Subject.Id == "artifact-retired");

        // Activity-consumer failures previously produced no diagnostic at all: BuildDiagnostics was
        // hardcoded to DurableValueStorageDriver, so an artifact could report IsReady == false with
        // nothing explaining which consumer was unavailable. Both statuses map to their own code,
        // matching ActivityPublicationReviewPolicy's vocabulary.
        var consumerMissing = Assert.Single(
            result.Diagnostics,
            x => x.Code == "activity.runtime.consumer-missing");
        Assert.Equal(ActivityDiagnosticSeverity.Error, consumerMissing.Severity);
        Assert.Equal("sample.missing", consumerMissing.Metadata["consumerKey"]);
        Assert.Equal("1", consumerMissing.Metadata["schemaVersion"]);

        var schemaUnsupported = Assert.Single(
            result.Diagnostics,
            x => x.Code == "activity.runtime.consumer-schema-unsupported");
        Assert.Equal("sample.schema", schemaUnsupported.Metadata["consumerKey"]);
        Assert.Equal("2", schemaUnsupported.Metadata["schemaVersion"]);
    }

    [Fact]
    public async Task Explicit_selection_only_checks_active_retained_artifacts_and_rejects_ambiguous_requests()
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var templates = new InMemoryExecutableActivityTemplateStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        IRuntimeActivityConsumerCapability[] consumers =
        [
            new RuntimeActivityConsumerCapability("sample.available", ["1"])
        ];
        await executables.SaveAsync(Executable("artifact-1", [new("sample.available", "1")], []));
        await references.SaveAsync(Reference("ref-live", "artifact-1"));
        var service = new RuntimeRequirementPreflight(
            references,
            executables,
            templates,
            new RuntimeRequirementChecker(
                consumers,
                new RuntimeDurableValueStorageDriverRegistry([]),
                new WellKnownTypeRegistry(),
                new JsonPayloadSerializer(new JsonPayloadConverterRegistry())),
            new FixedTimeProvider(Now));

        var selected = await service.RunAsync(
            RuntimeRequirementPreflight.ActiveRetainedArtifactsScope,
            ["artifact-unretained", "artifact-1"]);

        Assert.Equal(2, selected.CheckedArtifactCount);
        Assert.False(selected.IsReady);
        Assert.Contains(selected.Diagnostics, x =>
            x.Code == "activity.preflight.artifact-not-retained" && x.Subject.Id == "artifact-unretained");
        await Assert.ThrowsAsync<RuntimeRequirementPreflightRequestException>(() => service.RunAsync("Everything", null).AsTask());
        await Assert.ThrowsAsync<RuntimeRequirementPreflightRequestException>(() => service.RunAsync(
            RuntimeRequirementPreflight.ActiveRetainedArtifactsScope, []).AsTask());
        await Assert.ThrowsAsync<RuntimeRequirementPreflightRequestException>(() => service.RunAsync(
            RuntimeRequirementPreflight.ActiveRetainedArtifactsScope, ["artifact-1", "artifact-1"]).AsTask());
    }

    [Fact]
    public void Activation_classifier_marks_missing_consumers_as_deployment_recovery_not_activity_retry()
    {
        var handler = new ActivityActivationFailureHandler();

        var missing = handler.Classify(new UnknownActivityConsumerException("sample.missing", "1"), "artifact-1", "node-1");
        var unsupported = handler.Classify(new UnsupportedActivityDescriptorSchemaException("sample.schema", "2", ["1"]));
        var storageDriver = handler.Classify(new RuntimeDurableValueStorageDriverNotFoundException("sample.external"));

        Assert.NotNull(missing);
        Assert.Equal(ActivityActivationFailureKind.MissingConsumer, missing!.Kind);
        Assert.Equal("false", missing.Metadata[ActivityActivationFailureHandler.RetryEligibleMetadataKey]);
        Assert.Equal(ActivityActivationFailureHandler.DeploymentCorrectionRecoveryAction, missing.Metadata[ActivityActivationFailureHandler.RecoveryActionMetadataKey]);
        Assert.Equal(ActivityActivationFailureKind.UnsupportedSchema, unsupported!.Kind);
        Assert.Equal(ActivityActivationFailureKind.MissingStorageDriver, storageDriver!.Kind);
        Assert.Equal(RuntimeActivationCapabilityKind.DurableValueStorageDriver, storageDriver.CapabilityKind);
        Assert.Null(storageDriver.ConsumerKey);
        Assert.Equal("sample.external", storageDriver.StorageDriverKey);
        Assert.Null(handler.Classify(new InvalidOperationException("ordinary failure")));

        // A missing CLR activity type used to be the one activation failure Classify returned null
        // for, because UnknownActivityTypeException extended Exception directly rather than
        // ActivityResolutionException. It now classifies as a non-retryable deployment incident like
        // every sibling failure. The import gate (FR-B-005a) is the primary detection path; this is
        // the defense in depth behind it, for an artifact that somehow activates past the gate.
        var missingType = handler.Classify(new UnknownActivityTypeException("Acme.MissingActivity"), "artifact-1", "node-7");

        Assert.NotNull(missingType);
        Assert.Equal(ActivityActivationFailureKind.MissingActivityType, missingType!.Kind);
        Assert.Equal(RuntimeActivationCapabilityKind.ActivityConsumer, missingType.CapabilityKind);
        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, missingType.ConsumerKey);
        Assert.Equal("node-7", missingType.ExecutableNodeId);
        Assert.Equal("false", missingType.Metadata[ActivityActivationFailureHandler.RetryEligibleMetadataKey]);
        Assert.Equal(
            ActivityActivationFailureHandler.DeploymentCorrectionRecoveryAction,
            missingType.Metadata[ActivityActivationFailureHandler.RecoveryActionMetadataKey]);
    }

    // Every compiled workflow carries engine intrinsics (Set, Merge, Return, Control, SetCorrelationId, …), and
    // WorkflowExecutable's constructor *derives* RuntimeRequirements from every node's consumer key — so a
    // realistic preflight subject declares RuntimeRequirement("intrinsic", "1"). Until the engine advertised that
    // key, this endpoint reported every intrinsic-bearing workflow as not-ready, and this suite could not have
    // noticed: no subject here had ever contained an intrinsic node, so the counts were identical before and
    // after the fix. The pair below closes that blind spot from the publishing side.
    [Fact]
    public async Task Preflight_reports_an_intrinsic_bearing_artifact_ready_when_the_engine_advertises_the_intrinsic_consumer()
    {
        var service = await IntrinsicBearingPreflightAsync(
            new RuntimeActivityConsumerCapability("sample.available", ["1"]),
            // The production advertisement rather than a stand-in: this pins the exact key and schema version the
            // engine publishes, so narrowing either one fails here and not only in the Runtime suite.
            new WorkflowIntrinsicActivityConsumerCapability());

        var result = await service.RunAsync(RuntimeRequirementPreflight.ActiveRetainedArtifactsScope, null);

        // The load-bearing assertion is that the intrinsic requirement actually *reached* the checker. Without it,
        // a change that stopped deriving the requirement would leave IsReady == true and this test green while
        // proving nothing. The literal is deliberate alongside the constant: the consumer key is durable wire
        // content inside content-addressed artifacts, so changing its value is a breaking change, not a rename.
        var intrinsic = Assert.Single(result.Requirements, requirement => requirement.ConsumerKey == "intrinsic");
        Assert.Equal(WellKnownRuntimeActivityConsumers.Intrinsic, intrinsic.ConsumerKey);
        Assert.Equal(RuntimeActivityDescriptor.InitialSchemaVersion, intrinsic.SchemaVersion);
        Assert.Equal("Available", intrinsic.Status);
        Assert.Equal(1, intrinsic.AffectedArtifactCount);
        Assert.True(result.IsReady);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Metadata.TryGetValue("consumerKey", out var key) &&
                          StringComparer.Ordinal.Equals(key, WellKnownRuntimeActivityConsumers.Intrinsic));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Preflight_reports_an_intrinsic_bearing_artifact_unready_when_the_intrinsic_consumer_is_unadvertised()
    {
        // The counter-assertion that keeps the test above from passing vacuously. Withdraw the advertisement and
        // this endpoint must go back to reporting the artifact unready naming 'intrinsic' — which is exactly the
        // state every intrinsic-bearing workflow was in before the engine advertised the consumer.
        var service = await IntrinsicBearingPreflightAsync(new RuntimeActivityConsumerCapability("sample.available", ["1"]));

        var result = await service.RunAsync(RuntimeRequirementPreflight.ActiveRetainedArtifactsScope, null);

        Assert.False(result.IsReady);
        var intrinsic = Assert.Single(result.Requirements, requirement => requirement.ConsumerKey == "intrinsic");
        Assert.Equal("Missing", intrinsic.Status);
        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "activity.runtime.consumer-missing");
        Assert.Equal(ActivityDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("intrinsic", diagnostic.Metadata["consumerKey"]);
        Assert.Equal(RuntimeActivityDescriptor.InitialSchemaVersion, diagnostic.Metadata["schemaVersion"]);
        Assert.Equal(IntrinsicArtifactId, diagnostic.Subject.Id);
    }

    /// <summary>
    /// Wires the endpoint over one retained, available intrinsic-bearing executable. The advertised capability set
    /// is the only thing that differs between the two intrinsic tests, so it is the only parameter.
    /// </summary>
    private static async Task<RuntimeRequirementPreflight> IntrinsicBearingPreflightAsync(
        params IRuntimeActivityConsumerCapability[] consumers)
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executables.SaveAsync(IntrinsicBearingExecutable());
        await references.SaveAsync(Reference("ref-intrinsic", IntrinsicArtifactId));
        return new(
            references,
            executables,
            new InMemoryExecutableActivityTemplateStore(),
            new RuntimeRequirementChecker(
                consumers,
                new RuntimeDurableValueStorageDriverRegistry([]),
                new WellKnownTypeRegistry(),
                new JsonPayloadSerializer(new JsonPayloadConverterRegistry())),
            new FixedTimeProvider(Now));
    }

    /// <summary>
    /// An executable in the shape the compiler actually emits: an engine intrinsic in a child slot beside an
    /// ordinary root. No requirement is declared by hand — <see cref="WorkflowExecutable"/> derives both from the
    /// nodes, which is the derivation that puts "intrinsic" in front of the checker in production.
    /// </summary>
    private static WorkflowExecutable IntrinsicBearingExecutable()
    {
        var root = new ExecutableNode(
            "root",
            "root",
            "sample.available",
            "1",
            new("sample.available", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [IntrinsicNode("node-correlate")])]);
        return new(
            new(IntrinsicArtifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    /// <summary>
    /// An engine-intrinsic node in the shape <c>ExecutableNodeCompiler</c> emits: the reserved <c>"intrinsic"</c>
    /// descriptor type and no explicit descriptor schema version, so <see cref="ExecutableNode"/> defaults it to
    /// <see cref="RuntimeActivityDescriptor.InitialSchemaVersion"/> exactly as a compiled node does.
    /// </summary>
    /// <remarks>
    /// The shape is reproduced from <c>ArtifactClosureFixture.IntrinsicNode</c> rather than shared: that fixture
    /// belongs to the Runtime reconciliation suite, and a handful of duplicated lines beats a project reference
    /// between two test projects.
    /// </remarks>
    private static ExecutableNode IntrinsicNode(string nodeId)
    {
        const WorkflowIntrinsicKind kind = WorkflowIntrinsicKind.SetCorrelationId;
        var valueType = new ValueTypeDescriptor("String");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: $"elsa.intrinsic.{kind.ToString().ToLowerInvariant()}",
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.Intrinsic,
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = kind.ToString(), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
            {
                [WorkflowIntrinsicInputKeys.Value] = new(
                    WorkflowIntrinsicInputKeys.Value,
                    valueType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        valueType,
                        JsonSerializer.SerializeToElement("correlation-42"),
                        ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            intrinsicKind: kind);
    }

    private static WorkflowExecutable Executable(
        string artifactId,
        IReadOnlyCollection<RuntimeRequirement> requirements,
        IReadOnlyCollection<RuntimeStorageDriverRequirement> drivers)
    {
        var root = new ExecutableNode(
            "root",
            "root",
            "sample.available",
            "1",
            new("sample.available", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new(
            new(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference,
            requirements,
            drivers);
    }

    private static WorkflowExecutableSourceReference Reference(string id, string artifactId) => new(
        id,
        artifactId,
        "WorkflowDefinitionVersion",
        "version-1",
        "1.0.0",
        "definition-1",
        "version-1",
        "1.0.0",
        Now,
        Now,
        WorkflowExecutableReferenceScope.Published);

    private static ExecutableActivityTemplate Template(string templateId)
    {
        var root = new ExecutableNode(
            "template-root",
            "template-root",
            "sample.available",
            "1",
            new("sample.available", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new(
            templateId,
            "sha256:template",
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [],
            [],
            [new("sample.available", "1")],
            "sample/1",
            new Dictionary<string, string>(),
            Now,
            [new("elsa.json")]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
