using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class RuntimeRequirementPreflightTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preflight_checks_every_live_retained_artifact_and_keeps_capability_kinds_separate()
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var templates = new InMemoryExecutableActivityTemplateStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var constructors = new ActivityConstructorRegistry();
        constructors.AddAll([
            new TestConstructor("sample.available", "1"),
            new TestConstructor("sample.schema", "1")
        ]);
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
        var service = new RuntimeRequirementPreflight(references, executables, templates, constructors, drivers, new FixedTimeProvider(Now));

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
    }

    [Fact]
    public async Task Explicit_selection_only_checks_active_retained_artifacts_and_rejects_ambiguous_requests()
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var templates = new InMemoryExecutableActivityTemplateStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var constructors = new ActivityConstructorRegistry();
        constructors.AddAll([new TestConstructor("sample.available", "1")]);
        await executables.SaveAsync(Executable("artifact-1", [new("sample.available", "1")], []));
        await references.SaveAsync(Reference("ref-live", "artifact-1"));
        var service = new RuntimeRequirementPreflight(
            references,
            executables,
            templates,
            constructors,
            new RuntimeDurableValueStorageDriverRegistry([]),
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

    private sealed class TestConstructor(string consumerKey, params string[] schemas) : IActivityConstructor
    {
        public string ConsumerKey { get; } = consumerKey;
        public IReadOnlySet<string> SupportedSchemaVersions { get; } = schemas.ToHashSet(StringComparer.Ordinal);

        public ValueTask<IActivity> ConstructAsync(
            RuntimeActivityDescriptor descriptor,
            IReadOnlyDictionary<string, InputArgument> inputs,
            IReadOnlyDictionary<string, OutputArgument> outputs,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
