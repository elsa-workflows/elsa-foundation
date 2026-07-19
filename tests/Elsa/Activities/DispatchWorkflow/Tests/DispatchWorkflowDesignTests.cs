using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Design.Services;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowDesignTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Options_include_only_visible_definitions_with_one_live_published_source()
    {
        var definitions = new StubDefinitionStore(
            Definition("visible", "Visible"),
            Definition("missing", "Missing"),
            Definition("ambiguous", "Ambiguous"),
            Definition("test-run", "Test run"),
            Definition("retired", "Retired"));
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Source("visible-ref", "visible", "visible-artifact"));
        await references.SaveAsync(Source("ambiguous-a", "ambiguous", "ambiguous-a-artifact"));
        await references.SaveAsync(Source("ambiguous-b", "ambiguous", "ambiguous-b-artifact"));
        await references.SaveAsync(Source("test-ref", "test-run", "test-artifact", WorkflowExecutableReferenceScope.TestRun));
        await references.SaveAsync(Source("retired-ref", "retired", "retired-artifact") with { DeletedAt = Now.AddMinutes(-1) });
        var provider = new WorkflowDefinitionOptionsProvider(definitions, references, new FixedTimeProvider(Now));

        var options = await provider.GetOptionsAsync(null!);

        var option = Assert.Single(options);
        Assert.Equal("Visible", option.Label);
        Assert.Equal("visible", option.Value.GetString());
        Assert.Equal(DispatchWorkflowConstants.WorkflowDefinitionOptionsKey, provider.Key);
    }

    [Fact]
    public async Task Pin_contribution_carries_exact_executable_and_source_identity()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        // Content-addressed executables may retain source facts from the first definition that produced the
        // shared behavior. The selected source reference, not those deduplicated identity facts, owns target provenance.
        var executable = Executable("child-artifact", "first-content-owner", node);
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var source = Source("published-source", "child-definition", executable.Identity.ArtifactId);
        await executables.SaveAsync(executable);
        await references.SaveAsync(source);
        var sourceProvider = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));

        var context = Context(node);
        var compileContribution = await sourceProvider.GetContributionAsync(
            new ExecutableCompilationContext(context.Request, context.Source, context.RootActivity));
        var contributions = await sourceProvider.GetMetadataAsync(context);

        var contribution = Assert.Single(contributions);
        Assert.Equal(node.ExecutableNodeId, contribution.ExecutableNodeId);
        Assert.Equal(DispatchWorkflowConstants.PinnedTargetMetadataKey, contribution.Key);
        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(contribution.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(pin);
        Assert.Equal(executable.Identity, pin!.Executable);
        Assert.Equal(source.SourceReferenceId, pin.Source!.SourceReferenceId);
        Assert.Equal(source.SourceKind, pin.Source.SourceKind);
        Assert.Equal(source.SourceId, pin.Source.SourceId);
        Assert.Equal(source.SourceVersion, pin.Source.SourceVersion);
        Assert.Equal(source.DefinitionId, pin.Source.DefinitionId);
        Assert.Equal(source.DefinitionVersionId, pin.Source.DefinitionVersionId);
        Assert.Equal(source.ArtifactVersion, pin.Source.ArtifactVersion);
        Assert.Equal(source.PublicationId, pin.Source.PublicationId);
        Assert.Equal(source.SlotId, pin.Source.SlotId);
        var dependency = Assert.Single(compileContribution.Dependencies);
        Assert.Equal(node.ExecutableNodeId, dependency.ExecutableNodeId);
        Assert.Equal(executable.Identity.ArtifactId, dependency.ArtifactId);
        Assert.Equal(executable.Identity.ArtifactHash, dependency.ArtifactHash);
    }

    [Fact]
    public async Task Registered_source_enriches_dispatch_node_through_named_event_and_real_workflow_executable_compiler()
    {
        const string activityVersionId = "dispatch-activity-version";
        var workflowVersion = new WorkflowDefinitionVersion("parent-definition", "1.0.0")
        {
            Id = "parent-version",
            Definition = new WorkflowDefinition { Id = "parent-definition", Name = "Parent" },
            State = new WorkflowDefinitionState(
                [],
                new ActivityNode(
                    "dispatch-node",
                    activityVersionId,
                    [new ArgumentState(
                        nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                        new ArgumentValue("child-definition", "Literal"),
                        null,
                        null,
                        null,
                        null)],
                    []),
                [],
                [],
                null)
        };
        var activityVersion = new ActivityDefinitionVersion("1.0.0", "dispatch-activity-definition")
        {
            Id = activityVersionId,
            Definition = new ActivityDefinition
            {
                Id = "dispatch-activity-definition",
                ActivityTypeKey = DispatchWorkflowConstants.ActivityType,
                Category = "Workflows"
            },
            ProviderKey = "test.dispatch-descriptor",
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = "test.dispatch-descriptor",
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new { type = DispatchWorkflowConstants.ActivityType }),
            Inputs =
            [
                new InputDefinition(
                    nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                    nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.WorkflowDefinitionId),
                    new TypeReference("String"),
                    null,
                    "Workflow definition",
                    null,
                    false)
            ]
        };
        var childExecutable = Executable("child-artifact", "content-owner", DispatchNode("child-root", "unused"));
        var childSource = Source("child-source", "child-definition", childExecutable.Identity.ArtifactId);
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executableStore.SaveAsync(childExecutable);
        await sourceStore.SaveAsync(childSource);
        var typeRegistry = new WellKnownTypeRegistry();
        typeRegistry.RegisterType(typeof(string), "String");
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new StubWorkflowVersionStore(workflowVersion));
        services.AddSingleton<IActivityDefinitionVersionStore>(new StubActivityVersionStore(activityVersion));
        services.AddSingleton<IActivityDefinitionVersionPublicationStore, EmptyActivityPublicationStore>();
        services.AddSingleton<IExecutableActivityTemplateStore, InMemoryExecutableActivityTemplateStore>();
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        services.AddSingleton<IWellKnownTypeRegistry>(typeRegistry);
        services.AddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
        services.AddSingleton<IWorkflowExecutableStore>(executableStore);
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(sourceStore);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        new DispatchWorkflowDesignFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.Contains(
            scope.ServiceProvider.GetServices<IExecutableCompilationSource>(),
            source => source is DispatchPinSource);
        Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<OnExecutableCompilationCollecting>>());

        var executable = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(
            new WorkflowExecutableCompileRequest(
                "parent-version",
                WorkflowExecutableReferenceScope.Published,
                Now,
                Now,
                null,
                "artifact-parent-"));

        var serializedPin = executable.RootActivity.Metadata[DispatchWorkflowConstants.PinnedTargetMetadataKey];
        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(serializedPin, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(pin);
        Assert.Equal(childExecutable.Identity, pin.Executable);
        Assert.Equal(WorkflowExecutableSourceProvenance.From(childSource), pin.Source);
        var dependency = Assert.Single(executable.Dependencies);
        Assert.Equal(childExecutable.Identity.ArtifactId, dependency.ArtifactId);
        Assert.Equal(childExecutable.Identity.ArtifactHash, dependency.ArtifactHash);
        Assert.Equal(["dispatch-node"], dependency.DispatchNodeIds);
    }

    [Fact]
    public async Task Pin_contribution_fails_closed_for_ambiguous_live_sources()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Source("source-a", "child-definition", "artifact-a"));
        await references.SaveAsync(Source("source-b", "child-definition", "artifact-b"));
        var sourceProvider = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sourceProvider.GetMetadataAsync(Context(node)));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pin_contribution_rejects_missing_stale_and_unpublished_targets()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var pinSource = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));

        var missing = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("accessible live Published artifact", missing.Message, StringComparison.Ordinal);

        await references.SaveAsync(Source("child-source", "child-definition", "stale-artifact") with { ExpiresAt = Now });
        var stale = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("accessible live Published artifact", stale.Message, StringComparison.Ordinal);

        await references.SaveAsync(Source(
            "child-source",
            "child-definition",
            "test-run-artifact",
            WorkflowExecutableReferenceScope.TestRun));
        var neverPublished = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("accessible live Published artifact", neverPublished.Message, StringComparison.Ordinal);

        await references.SaveAsync(Source("child-source", "child-definition", "retired-artifact") with { DeletedAt = Now });
        var unpublished = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("accessible live Published artifact", unpublished.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_run_parent_pins_the_live_published_child_and_ignores_test_run_child_source()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var publishedChild = Executable("published-child-artifact", "child-definition", DispatchNode("published-root", "unused"));
        var testRunChild = Executable("test-run-child-artifact", "child-definition", DispatchNode("test-run-root", "unused"));
        var publishedSource = Source("published-child-source", "child-definition", publishedChild.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published);
        var testRunSource = Source("test-run-child-source", "child-definition", testRunChild.Identity.ArtifactId, WorkflowExecutableReferenceScope.TestRun);
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executables.SaveAsync(publishedChild);
        await executables.SaveAsync(testRunChild);
        await references.SaveAsync(publishedSource);
        await references.SaveAsync(testRunSource);
        var pinSource = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));
        var context = Context(node) with
        {
            Request = new WorkflowExecutableCompileRequest(
                "parent-version",
                WorkflowExecutableReferenceScope.TestRun,
                Now,
                Now,
                Now.AddMinutes(5),
                "artifact-")
        };

        var contribution = await pinSource.GetContributionAsync(
            new ExecutableCompilationContext(context.Request, context.Source, context.RootActivity));
        var dependency = Assert.Single(contribution.Dependencies);

        Assert.Equal(publishedChild.Identity.ArtifactId, dependency.ArtifactId);
        Assert.Equal(publishedChild.Identity.ArtifactHash, dependency.ArtifactHash);
        Assert.NotEqual(testRunChild.Identity.ArtifactId, dependency.ArtifactId);
    }

    [Fact]
    public async Task Pin_contribution_rejects_missing_and_inconsistent_executables()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Source("child-source", "child-definition", "expected-artifact"));

        var missingSource = new DispatchPinSource(
            references,
            new InMemoryWorkflowExecutableStore(),
            InputValidator(),
            new FixedTimeProvider(Now));
        var missing = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await missingSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("missing executable 'expected-artifact'", missing.Message, StringComparison.Ordinal);

        var wrongExecutable = Executable("different-artifact", "content-owner", DispatchNode("child-root", "unused"));
        var inconsistentSource = new DispatchPinSource(
            references,
            new FindOnlyExecutableStore(wrongExecutable),
            InputValidator(),
            new FixedTimeProvider(Now));
        var inconsistent = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await inconsistentSource.GetContributionAsync(CompilationContext(node)));
        Assert.Contains("inconsistent executable/source identity", inconsistent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pin_contribution_allows_multiple_live_references_to_the_same_exact_artifact()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var executable = Executable("child-artifact", "content-owner", node);
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executables.SaveAsync(executable);
        await references.SaveAsync(Source("source-a", "child-definition", executable.Identity.ArtifactId));
        await references.SaveAsync(Source("source-b", "child-definition", executable.Identity.ArtifactId));

        var contribution = await new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now))
            .GetContributionAsync(CompilationContext(node));

        Assert.Single(contribution.Dependencies);
    }

    [Fact]
    public async Task Resolution_then_replacement_keeps_the_first_exact_pin_and_resolves_the_new_artifact_next_time()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var oldExecutable = Executable("child-v1", "content-owner", DispatchNode("child-root-v1", "unused"));
        var newExecutable = Executable("child-v2", "content-owner", DispatchNode("child-root-v2", "unused"));
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executables.SaveAsync(oldExecutable);
        await executables.SaveAsync(newExecutable);
        await references.SaveAsync(Source("child-v1-source", "child-definition", oldExecutable.Identity.ArtifactId));
        var pinSource = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));

        var first = await pinSource.GetContributionAsync(CompilationContext(node));

        await references.RetireAsync("child-v1-source", Now, "replaced");
        await references.SaveAsync(Source("child-v2-source", "child-definition", newExecutable.Identity.ArtifactId));
        var second = await pinSource.GetContributionAsync(CompilationContext(node));

        AssertExactPin(first, oldExecutable.Identity, "child-v1-source");
        AssertExactPin(second, newExecutable.Identity, "child-v2-source");
        AssertExactPin(first, oldExecutable.Identity, "child-v1-source");
    }

    [Fact]
    public async Task Pin_contribution_rejects_cross_tenant_and_legacy_targets()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var legacy = LegacyExecutable("legacy-artifact", "content-owner", node);
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executables.SaveAsync(legacy);
        await references.SaveAsync(Source(
            "tenant-b-source",
            "child-definition",
            legacy.Identity.ArtifactId,
            tenantId: "tenant-b"));
        var pinSource = new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now));

        var inaccessible = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node, tenantId: "tenant-a")));
        Assert.Contains("accessible", inaccessible.Message, StringComparison.Ordinal);

        await references.SaveAsync(Source(
            "tenant-a-source",
            "child-definition",
            legacy.Identity.ArtifactId,
            tenantId: "tenant-a"));
        var legacyError = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await pinSource.GetContributionAsync(CompilationContext(node, tenantId: "tenant-a")));
        Assert.Contains("recompiled and republished", legacyError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pin_contribution_rejects_literal_unknown_input_without_exposing_its_value()
    {
        const string rejectedValue = "publication-secret";
        var node = DispatchNodeWithLiteralInputs(
            "dispatch-node",
            "child-definition",
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["unknown"] = rejectedValue }));
        var contract = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("known", new TypeReference("String"), false)]);

        var exception = await AssertPinInputFailureAsync(node, contract);

        Assert.Contains("not declared", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pin_contribution_rejects_blank_duplicate_missing_required_and_incompatible_literal_inputs()
    {
        var cases = new[]
        {
            (
                Inputs: Json("{\" \" : \"value\"}"),
                Contract: Contract(new WorkflowDeclaredInput("known", new TypeReference("String"), false)),
                Expected: "cannot be blank"),
            (
                Inputs: Json("{\"known\":\"one\",\"known\":\"two\"}"),
                Contract: Contract(new WorkflowDeclaredInput("known", new TypeReference("String"), false)),
                Expected: "more than once"),
            (
                Inputs: Json("{}"),
                Contract: Contract(new WorkflowDeclaredInput("required", new TypeReference("String"), true)),
                Expected: "was not supplied"),
            (
                Inputs: Json("{\"count\":\"not-an-integer\"}"),
                Contract: Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), false)),
                Expected: "incompatible")
        };

        foreach (var (inputs, contract, expected) in cases)
        {
            var node = DispatchNodeWithLiteralInputs("dispatch-node", "child-definition", inputs);
            var exception = await AssertPinInputFailureAsync(node, contract);
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Pin_contribution_rejects_unknown_aliases_for_literal_and_dynamic_inputs()
    {
        var validNode = DispatchNodeWithDynamicInputs("dispatch-node", "child-definition");
        var validContract = Contract(new WorkflowDeclaredInput("required", new TypeReference("String"), true));
        var validContribution = await GetPinContributionAsync(validNode, validContract);
        Assert.Single(validContribution.Dependencies);

        var invalidContract = Contract(new WorkflowDeclaredInput("required", new TypeReference("Extension.Unknown"), true));
        var dynamicException = await AssertPinInputFailureAsync(validNode, invalidContract);
        Assert.Contains("unknown type alias", dynamicException.Message, StringComparison.Ordinal);

        var literalNode = DispatchNodeWithLiteralInputs(
            "dispatch-node",
            "child-definition",
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["required"] = "value" }));
        var literalException = await AssertPinInputFailureAsync(literalNode, invalidContract);
        Assert.Contains("unknown type alias", literalException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declared_runtime_like_names_remain_ordinary_inputs_during_tenant_scoped_publication()
    {
        const string publicationTenant = "publication-tenant";
        var node = DispatchNodeWithLiteralInputs(
            "dispatch-node",
            "child-definition",
            JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["tenant"] = "attempted-tenant-override",
                ["authority"] = "attempted-authority-override"
            }));
        var contract = Contract(
            new WorkflowDeclaredInput("tenant", new TypeReference("String"), true),
            new WorkflowDeclaredInput("authority", new TypeReference("String"), true));

        var contribution = await GetPinContributionAsync(
            node,
            contract,
            publicationTenant,
            referenceTenant: publicationTenant);

        Assert.Single(contribution.Dependencies);
        var pin = DeserializePin(contribution);
        Assert.Equal("child-definition", pin.Source!.DefinitionId);
    }

    [Fact]
    public async Task Generic_enricher_rejects_two_owners_for_one_node_metadata_key()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var enricher = Enricher(
            new FirstStubSource(new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "first")),
            new SecondStubSource(new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "second")));
        var context = Context(node);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await enricher.EnrichAsync(context.Request, context.Source, node));

        Assert.Contains("unequal values from owners", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generic_enricher_treats_equal_duplicate_contributions_as_idempotent()
    {
        var node = DispatchNode("dispatch-node", "child-definition");
        var contribution = new ExecutableNodeMetadataContribution(node.ExecutableNodeId, "shared-key", "same");
        var enricher = Enricher(new FirstStubSource(contribution), new SecondStubSource(contribution));
        var context = Context(node);

        var enriched = await enricher.EnrichAsync(context.Request, context.Source, node);

        Assert.Equal("same", enriched.Metadata["shared-key"]);
    }

    private static ExecutableNodeMetadataContext Context(ExecutableNode node, string? tenantId = null)
    {
        var request = new WorkflowExecutableCompileRequest(
            "parent-version",
            WorkflowExecutableReferenceScope.Published,
            Now,
            Now,
            null,
            "artifact-",
            TenantId: tenantId);
        var source = new WorkflowExecutableCompileSource(
            "parent-definition",
            "parent-version",
            "1.0.0",
            WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion",
            "parent-version",
            "1.0.0");
        return new ExecutableNodeMetadataContext(request, source, node);
    }

    private static ExecutableCompilationContext CompilationContext(ExecutableNode node, string? tenantId = null)
    {
        var context = Context(node, tenantId);
        return new ExecutableCompilationContext(context.Request, context.Source, context.RootActivity);
    }

    private static ExecutableNodeMetadataEnricher Enricher(params IExecutableNodeMetadataSource[] sources) =>
        new(new CollectingInlineEventPublisher(sources));

    private static ExecutableNode DispatchNode(string nodeId, string definitionId) =>
        new(
            nodeId,
            nodeId,
            DispatchWorkflowConstants.ActivityType,
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test.dispatch-descriptor",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = DispatchWorkflowConstants.ActivityType })),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["WorkflowDefinitionId"] = new(
                    "WorkflowDefinitionId",
                    ValueType(typeof(string)),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        ValueType(typeof(string)),
                        JsonSerializer.SerializeToElement(definitionId),
                        ValueProtectionPolicy.InstanceInline))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["authoredNodeId"] = nodeId });

    private static ExecutableNode DispatchNodeWithLiteralInputs(string nodeId, string definitionId, JsonElement inputs) =>
        DispatchNodeWithInputs(
            nodeId,
            definitionId,
            new RuntimeInputBinding(
                "Inputs",
                ValueType(typeof(IReadOnlyDictionary<string, JsonElement>)),
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(
                    ValueType(typeof(IReadOnlyDictionary<string, JsonElement>)),
                    inputs,
                    ValueProtectionPolicy.InstanceInline)));

    private static ExecutableNode DispatchNodeWithDynamicInputs(string nodeId, string definitionId) =>
        DispatchNodeWithInputs(
            nodeId,
            definitionId,
            new RuntimeInputBinding(
                "Inputs",
                ValueType(typeof(IReadOnlyDictionary<string, JsonElement>)),
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Expression,
                expression: new RuntimeExpressionBinding("JavaScript", "dynamicInputs")));

    private static ValueTypeDescriptor ValueType(Type type)
    {
        var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
        return new ValueTypeDescriptor(reference.Alias, reference.CollectionKind);
    }

    private static ExecutableNode DispatchNodeWithInputs(
        string nodeId,
        string definitionId,
        RuntimeInputBinding inputsBinding)
    {
        var node = DispatchNode(nodeId, definitionId);
        var bindings = node.InputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        bindings["Inputs"] = inputsBinding;
        return new ExecutableNode(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Descriptor,
            bindings,
            node.OutputCaptures,
            node.Metadata,
            node.ChildSlots,
            node.Structure);
    }

    private static WorkflowExecutable Executable(string artifactId, string definitionId, ExecutableNode root) =>
        new(
            new WorkflowExecutableIdentity(artifactId, definitionId, "definition-version", "1.0.0", $"sha256:{artifactId}"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            []);

    private static WorkflowExecutable LegacyExecutable(string artifactId, string definitionId, ExecutableNode root) =>
        new(
            new WorkflowExecutableIdentity(artifactId, definitionId, "definition-version", "1.0.0", $"sha256:{artifactId}"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>());

    private static WorkflowExecutableInputContract Contract(params WorkflowDeclaredInput[] inputs) =>
        new(WorkflowExecutableInputContract.CurrentVersion, inputs);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<ExecutableCompilationContribution> GetPinContributionAsync(
        ExecutableNode node,
        WorkflowExecutableInputContract inputContract,
        string? publicationTenant = null,
        string? referenceTenant = null)
    {
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var executable = new WorkflowExecutable(
            new WorkflowExecutableIdentity("child-artifact", "content-owner", "definition-version", "1.0.0", "sha256:child"),
            DispatchNode("child-root", "unused"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            inputContract,
            []);
        await executables.SaveAsync(executable);
        await references.SaveAsync(Source(
            "child-source",
            "child-definition",
            executable.Identity.ArtifactId,
            tenantId: referenceTenant));
        return await new DispatchPinSource(references, executables, InputValidator(), new FixedTimeProvider(Now))
            .GetContributionAsync(CompilationContext(node, publicationTenant));
    }

    private static async Task<ArgumentException> AssertPinInputFailureAsync(
        ExecutableNode node,
        WorkflowExecutableInputContract inputContract) =>
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await GetPinContributionAsync(node, inputContract));

    private static void AssertExactPin(
        ExecutableCompilationContribution contribution,
        WorkflowExecutableIdentity expectedIdentity,
        string expectedSourceReferenceId)
    {
        var pin = DeserializePin(contribution);
        Assert.Equal(expectedIdentity, pin.Executable);
        Assert.Equal(expectedSourceReferenceId, pin.Source!.SourceReferenceId);
        var dependency = Assert.Single(contribution.Dependencies);
        Assert.Equal(expectedIdentity.ArtifactId, dependency.ArtifactId);
        Assert.Equal(expectedIdentity.ArtifactHash, dependency.ArtifactHash);
    }

    private static DispatchWorkflowPin DeserializePin(ExecutableCompilationContribution contribution)
    {
        var metadata = Assert.Single(contribution.NodeMetadata);
        Assert.Equal(DispatchWorkflowConstants.PinnedTargetMetadataKey, metadata.Key);
        return Assert.IsType<DispatchWorkflowPin>(JsonSerializer.Deserialize<DispatchWorkflowPin>(metadata.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static WorkflowExecutableSourceReference Source(
        string sourceReferenceId,
        string definitionId,
        string artifactId,
        WorkflowExecutableReferenceScope scope = WorkflowExecutableReferenceScope.Published,
        string? tenantId = null) =>
        new(
            sourceReferenceId,
            artifactId,
            "WorkflowDefinitionVersion",
            $"{definitionId}-version",
            "1.0.0",
            definitionId,
            $"{definitionId}-version",
            "1.0.0",
            Now,
            scope == WorkflowExecutableReferenceScope.Published ? Now : null,
            scope,
            TenantId: tenantId);

    private static WorkflowDefinition Definition(string id, string name) => new() { Id = id, Name = name };

    private static IWorkflowExecutableInputValidator InputValidator()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(int), "Int32");
        registry.RegisterType(typeof(bool), "Boolean");
        registry.RegisterType(typeof(object), "Object");
        return new WorkflowExecutableInputValidator(registry);
    }

    private sealed class StubDefinitionStore(params WorkflowDefinition[] definitions) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.Single(definition => definition.Id == id));

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.SingleOrDefault(definition => definition.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(definitions);
    }

    private sealed class StubWorkflowVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(version);

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version.Id == versionId ? version : null);

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Unknown workflow version '{versionId}'.");

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version.DefinitionId == definitionId ? version : null);

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(version.DefinitionId == definitionId ? [version] : []);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey);
    }

    private sealed class StubActivityVersionStore(ActivityDefinitionVersion version) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetWithDefinitionAsync(versionId, cancellationToken);

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Unknown activity version '{versionId}'.");

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersion?>(
                version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey ? version : null);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(version.DefinitionId == definitionId ? [version] : []);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(
                definitionIds.Contains(version.DefinitionId, StringComparer.Ordinal) ? [version] : []);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([version]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptyActivityPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(
            string definitionVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    private sealed class FindOnlyExecutableStore(WorkflowExecutable executable) : IWorkflowExecutableStore
    {
        public ValueTask SaveAsync(WorkflowExecutable value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
            string artifactId,
            string leaseId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ReleaseRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
            string artifactId,
            string operationId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> CancelDeletionAsync(
            WorkflowExecutableDeletionGuard guard,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(
            WorkflowExecutableDeletionGuard guard,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutable?>(executable);

        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private abstract class StubMetadataSource(params ExecutableNodeMetadataContribution[] contributions) : IExecutableNodeMetadataSource
    {
        public ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
            ExecutableNodeMetadataContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ExecutableNodeMetadataContribution>>(contributions);
    }

    private sealed class FirstStubSource(params ExecutableNodeMetadataContribution[] contributions) : StubMetadataSource(contributions)
    {
    }

    private sealed class SecondStubSource(params ExecutableNodeMetadataContribution[] contributions) : StubMetadataSource(contributions)
    {
    }

    private sealed class CollectingInlineEventPublisher(IEnumerable<IExecutableNodeMetadataSource> sources) : IInlineEventPublisher
    {
        private readonly CollectExecutableCompilation _handler = new([], sources);

        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            _handler.Handle(Assert.IsType<OnExecutableCompilationCollecting>(@event), cancellationToken);
    }
}
