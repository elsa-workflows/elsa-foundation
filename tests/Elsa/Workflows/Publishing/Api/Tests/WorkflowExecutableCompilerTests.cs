using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using CronActivity = Elsa.Activities.Scheduling.Activities.Cron;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowExecutableCompilerTests
{
    [Fact]
    public void Compiler_preserves_the_pre_metadata_enricher_constructor()
    {
        var constructor = typeof(WorkflowExecutableCompiler).GetConstructor(
        [
            typeof(IWorkflowDefinitionVersionStore),
            typeof(IActivityDefinitionVersionStore),
            typeof(WorkflowExecutableHasher),
            typeof(ActivityTreeProjector),
            typeof(ExecutableNodeCompiler)
        ]);

        Assert.NotNull(constructor);
    }

    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _writeLinesActivity = ActivityVersion("activity-write-lines", "Lines", new TypeReference("String", CollectionKind.List));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion("activity-sequence", typeof(SequenceActivity).FullName!);
    private readonly ActivityDefinitionVersion _legacyTriggerActivity = LegacyTriggerActivityVersion();
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();

    [Fact]
    public async Task CompilesPublishedWorkflowExecutableWithPublishedScope()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        Assert.StartsWith("artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
    }

    [Fact]
    public async Task Compiler_applies_event_collected_node_metadata_before_executable_assembly()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("hello")));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            MetadataEnricher(new FixedMetadataSource("write-one", "runtime.pin", "pinned-value")));

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("pinned-value", executable.RootActivity.Metadata["runtime.pin"]);
    }

    [Fact]
    public async Task Compiler_projects_canonical_versioned_workflow_input_contract_into_behavioral_hash()
    {
        var first = WorkflowVersion(
            Node("write-one", Text("hello")),
            [
                WorkflowInput("zeta", new TypeReference("String"), isRequired: false),
                WorkflowInput(
                    "alpha",
                    new TypeReference("Object"),
                    isRequired: true,
                    JsonSerializer.SerializeToElement(new { b = 2, a = 1 }),
                    "Literal")
            ]);
        var reordered = WorkflowVersion(
            Node("write-one", Text("hello")),
            [
                WorkflowInput(
                    "alpha",
                    new TypeReference("Object"),
                    isRequired: true,
                    JsonSerializer.SerializeToElement(new { a = 1, b = 2 }),
                    "Literal"),
                WorkflowInput("zeta", new TypeReference("String"), isRequired: false)
            ]);
        var changed = WorkflowVersion(
            Node("write-one", Text("hello")),
            [WorkflowInput("zeta", new TypeReference("Int32"), isRequired: false)]);

        var firstExecutable = await Compiler(first).CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var reorderedExecutable = await Compiler(reordered).CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedExecutable = await Compiler(changed).CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, firstExecutable.InputContract!.Version);
        Assert.Equal(["alpha", "zeta"], firstExecutable.InputContract.Inputs.Select(input => input.Name));
        Assert.Equal(firstExecutable.Identity.ArtifactHash, reorderedExecutable.Identity.ArtifactHash);
        Assert.NotEqual(firstExecutable.Identity.ArtifactHash, changedExecutable.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Structured_behavioral_hash_distinguishes_values_that_contain_legacy_delimiters()
    {
        var executable = await Compiler(WorkflowVersion(Node("write-one", Text("hello"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var hasher = new WorkflowExecutableHasher();
        var singleEncodedInput = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("a:String:Single:False:<none>|b", new TypeReference("String"), false)]);
        var separateInputs = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [
                new WorkflowDeclaredInput("a", new TypeReference("String"), false),
                new WorkflowDeclaredInput("b", new TypeReference("String"), false)
            ]);
        var singleEncodedNode = new WorkflowExecutableDependency("child", "sha256:child", ["a,b"]);
        var separateNodes = new WorkflowExecutableDependency("child", "sha256:child", ["a", "b"]);

        var singleInputHash = hasher.ComputeHash(executable.RootActivity, singleEncodedInput, []);
        var separateInputHash = hasher.ComputeHash(executable.RootActivity, separateInputs, []);
        var singleNodeHash = hasher.ComputeHash(executable.RootActivity, executable.InputContract!, [singleEncodedNode]);
        var separateNodeHash = hasher.ComputeHash(executable.RootActivity, executable.InputContract!, [separateNodes]);

        Assert.NotEqual(singleInputHash, separateInputHash);
        Assert.NotEqual(singleNodeHash, separateNodeHash);
    }

    [Fact]
    public async Task Structured_behavioral_hash_distinguishes_absent_default_from_explicit_json_null()
    {
        var executable = await Compiler(WorkflowVersion(Node("write-one", Text("hello"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var hasher = new WorkflowExecutableHasher();
        var absentDefault = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("value", new TypeReference("Object"), false)]);
        var explicitNullDefault = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput("value", new TypeReference("Object"), false, JsonSerializer.SerializeToElement<object?>(null))]);

        var absentHash = hasher.ComputeHash(executable.RootActivity, absentDefault, []);
        var explicitNullHash = hasher.ComputeHash(executable.RootActivity, explicitNullDefault, []);

        Assert.NotEqual(absentHash, explicitNullHash);
    }

    [Fact]
    public async Task Compiler_assembles_canonical_direct_dependencies_and_hashes_child_behavior()
    {
        var workflow = WorkflowVersion(SequenceNode("sequence", [Node("dispatch-b"), Node("dispatch-a")]));
        var first = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            MetadataEnricher(new FixedDependencySource(
            [
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:one"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:one")
            ])));
        var reordered = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            MetadataEnricher(new FixedDependencySource(
            [
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:one"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:one")
            ])));
        var changed = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            MetadataEnricher(new FixedDependencySource(
                [new ExecutableDependencyClaim("dispatch-a", "child", "sha256:two")])));

        var firstExecutable = await first.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var reorderedExecutable = await reordered.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedExecutable = await changed.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        var dependency = Assert.Single(firstExecutable.Dependencies);
        Assert.Equal("child", dependency.ArtifactId);
        Assert.Equal("sha256:one", dependency.ArtifactHash);
        Assert.Equal(["dispatch-a", "dispatch-b"], dependency.DispatchNodeIds);
        Assert.Equal(firstExecutable.Identity.ArtifactHash, reorderedExecutable.Identity.ArtifactHash);
        Assert.NotEqual(firstExecutable.Identity.ArtifactHash, changedExecutable.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_canonicalizes_duplicate_exact_node_dependency_claims()
    {
        var workflow = WorkflowVersion(SequenceNode("sequence", [Node("dispatch-b"), Node("dispatch-a")]));
        var unique = await CompileWithDependenciesAsync(
            workflow,
            [
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child")
            ]);
        var duplicated = await CompileWithDependenciesAsync(
            workflow,
            [
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-a", "child", "sha256:child"),
                new ExecutableDependencyClaim("dispatch-b", "child", "sha256:child")
            ]);

        var dependency = Assert.Single(duplicated.Dependencies);
        Assert.Equal(["dispatch-a", "dispatch-b"], dependency.DispatchNodeIds);
        Assert.Equal(unique.Identity.ArtifactHash, duplicated.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_produces_same_parent_hash_for_equivalent_shared_diamond_orders()
    {
        var shared = await Compiler(WorkflowVersion(Node("shared", Text("shared behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var left = await CompileWithDependenciesAsync(
            WorkflowVersion(Node("left", Text("left behavior"))),
            [new ExecutableDependencyClaim("left", shared.Identity.ArtifactId, shared.Identity.ArtifactHash)]);
        var right = await CompileWithDependenciesAsync(
            WorkflowVersion(Node("right", Text("right behavior"))),
            [new ExecutableDependencyClaim("right", shared.Identity.ArtifactId, shared.Identity.ArtifactHash)]);
        var parentWorkflow = WorkflowVersion(SequenceNode(
            "parent",
            [Node("dispatch-left"), Node("dispatch-right")]));

        var leftFirst = await CompileWithDependenciesAsync(
            parentWorkflow,
            [
                new ExecutableDependencyClaim("dispatch-left", left.Identity.ArtifactId, left.Identity.ArtifactHash),
                new ExecutableDependencyClaim("dispatch-right", right.Identity.ArtifactId, right.Identity.ArtifactHash)
            ]);
        var rightFirst = await CompileWithDependenciesAsync(
            parentWorkflow,
            [
                new ExecutableDependencyClaim("dispatch-right", right.Identity.ArtifactId, right.Identity.ArtifactHash),
                new ExecutableDependencyClaim("dispatch-left", left.Identity.ArtifactId, left.Identity.ArtifactHash)
            ]);

        Assert.Equal(2, leftFirst.Dependencies.Count);
        Assert.Equal(leftFirst.Identity.ArtifactHash, rightFirst.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Grandchild_behavior_change_propagates_through_child_hash_into_parent_hash()
    {
        var firstGrandchild = await Compiler(WorkflowVersion(Node("grandchild", Text("first behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var changedGrandchild = await Compiler(WorkflowVersion(Node("grandchild", Text("changed behavior"))))
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
        var childWorkflow = WorkflowVersion(Node("dispatch-grandchild"));
        var firstChild = await CompileWithDependenciesAsync(
            childWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-grandchild",
                firstGrandchild.Identity.ArtifactId,
                firstGrandchild.Identity.ArtifactHash)]);
        var changedChild = await CompileWithDependenciesAsync(
            childWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-grandchild",
                changedGrandchild.Identity.ArtifactId,
                changedGrandchild.Identity.ArtifactHash)]);
        var parentWorkflow = WorkflowVersion(Node("dispatch-child"));
        var firstParent = await CompileWithDependenciesAsync(
            parentWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-child",
                firstChild.Identity.ArtifactId,
                firstChild.Identity.ArtifactHash)]);
        var changedParent = await CompileWithDependenciesAsync(
            parentWorkflow,
            [new ExecutableDependencyClaim(
                "dispatch-child",
                changedChild.Identity.ArtifactId,
                changedChild.Identity.ArtifactHash)]);

        Assert.NotEqual(firstGrandchild.Identity.ArtifactHash, changedGrandchild.Identity.ArtifactHash);
        Assert.NotEqual(firstChild.Identity.ArtifactHash, changedChild.Identity.ArtifactHash);
        Assert.NotEqual(firstParent.Identity.ArtifactHash, changedParent.Identity.ArtifactHash);
    }

    [Fact]
    public async Task Compiler_rejects_a_malformed_stored_exact_artifact_cycle_with_a_deterministic_full_identity_path()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var firstIdentity = StoredIdentity("artifact-a", "sha256:a");
        var secondIdentity = StoredIdentity("artifact-b", "sha256:b");
        await store.SaveAsync(StoredExecutable(
            firstIdentity,
            new WorkflowExecutableDependency(secondIdentity.ArtifactId, secondIdentity.ArtifactHash, ["node-root"])));
        await store.SaveAsync(StoredExecutable(
            secondIdentity,
            new WorkflowExecutableDependency(firstIdentity.ArtifactId, firstIdentity.ArtifactHash, ["node-root"])));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", firstIdentity.ArtifactId, firstIdentity.ArtifactHash)],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.Cycle, graphFailure.Kind);
        Assert.Contains("artifact-a@sha256:a -> artifact-b@sha256:b -> artifact-a@sha256:a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_candidate_full_identity_recurrence_in_a_stored_child_closure()
    {
        var childIdentity = StoredIdentity("artifact-child", "sha256:child");
        var workflow = WorkflowVersion(Node("dispatch-child"));
        var claims = new[] { new ExecutableDependencyClaim("dispatch-child", childIdentity.ArtifactId, childIdentity.ArtifactHash) };
        var candidate = await CompileWithDependenciesAsync(workflow, claims);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(StoredExecutable(candidate.Identity));
        await store.SaveAsync(StoredExecutable(
            childIdentity,
            new WorkflowExecutableDependency(
                candidate.Identity.ArtifactId,
                candidate.Identity.ArtifactHash,
                ["node-root"])));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            ValidatedDependencyCompiler(workflow, claims, store)
                .CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains(candidate.Identity.ArtifactId, exception.Message, StringComparison.Ordinal);
        Assert.Contains(candidate.Identity.ArtifactHash, exception.Message, StringComparison.Ordinal);
        Assert.Contains("recurs", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"artifact-child@sha256:child -> {candidate.Identity.ArtifactId}@{candidate.Identity.ArtifactHash}",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_a_truncated_dependency_artifact_id_instead_of_prefix_matching()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var childIdentity = StoredIdentity("artifact-abcdef", "sha256:child-full");
        await store.SaveAsync(StoredExecutable(childIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", "artifact-abc", childIdentity.ArtifactHash)],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.MissingArtifact, graphFailure.Kind);
        Assert.Contains("artifact-abc@sha256:child-full", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_rejects_a_dependency_hash_mismatch_without_truncated_hash_matching()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var childIdentity = StoredIdentity("artifact-child", "sha256:child-full-hash");
        await store.SaveAsync(StoredExecutable(childIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-child")),
            [new ExecutableDependencyClaim("dispatch-child", childIdentity.ArtifactId, "sha256:child")],
            store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        var graphFailure = Assert.IsType<WorkflowExecutableDependencyGraphException>(exception.InnerException);
        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.HashMismatch, graphFailure.Kind);
        Assert.Contains("sha256:child-full-hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiler_allows_same_definition_dependency_when_the_full_artifact_identity_is_different()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var olderIdentity = StoredIdentity(
            "artifact-older",
            "sha256:older",
            definitionId: "definition-1",
            definitionVersionId: "version-older");
        await store.SaveAsync(StoredExecutable(olderIdentity));
        var compiler = ValidatedDependencyCompiler(
            WorkflowVersion(Node("dispatch-older")),
            [new ExecutableDependencyClaim("dispatch-older", olderIdentity.ArtifactId, olderIdentity.ArtifactHash)],
            store);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal("definition-1", executable.Identity.DefinitionId);
        Assert.NotEqual(olderIdentity.ArtifactId, executable.Identity.ArtifactId);
        Assert.Equal(olderIdentity.ArtifactId, Assert.Single(executable.Dependencies).ArtifactId);
    }

    [Fact]
    public async Task Compiler_rejects_non_literal_workflow_input_defaults()
    {
        var workflow = WorkflowVersion(
            Node("write-one"),
            [WorkflowInput(
                "message",
                new TypeReference("String"),
                isRequired: false,
                JsonSerializer.SerializeToElement("expression"),
                "JavaScript")]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Compiler(workflow).CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());

        Assert.Contains("unsupported default syntax", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyClrTriggerCatalogRow_CompilesWithDeclaredIdentityAndTriggerExecutionType()
    {
        var compiler = Compiler(WorkflowVersion(new ActivityNode("legacy-trigger", "activity-legacy-trigger", [], [])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: DateTimeOffset.UtcNow,
            PublishedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        Assert.Equal(LegacyTriggerActivity.ActivityType, executable.RootActivity.ActivityType);
        Assert.Equal("Trigger", executable.RootActivity.Metadata[TriggerNodeMetadata.ExecutionTypeKey]);
    }

    public static TheoryData<Type, string> FirstPartyClrTriggers => new()
    {
        { typeof(EventActivity), EventActivity.ActivityType },
        { typeof(TimerActivity), TimerActivity.ActivityType },
        { typeof(CronActivity), CronActivity.ActivityType },
        { typeof(HttpEndpoint), HttpEndpoint.ActivityType }
    };

    [Theory]
    [MemberData(nameof(FirstPartyClrTriggers))]
    public async Task LegacyFirstPartyClrTriggerCatalogRow_CompilesWithTriggerProjection(Type activityType, string declaredActivityType)
    {
        var activityVersion = LegacyTriggerActivityVersion(activityType);
        var workflowVersion = WorkflowVersion(new ActivityNode("legacy-trigger", activityVersion.Id, [], []));
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([activityVersion]),
            _activityStructureService,
            registry);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        Assert.Equal(declaredActivityType, executable.RootActivity.ActivityType);
        Assert.Equal("Trigger", executable.RootActivity.Metadata[TriggerNodeMetadata.ExecutionTypeKey]);
    }

    [Fact]
    public async Task CompilesJavaScriptBoundInputIntoRuntimeExpressionBinding()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText("\"Hello \" + \"World\""))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Null(binding.LiteralValue);
        var expression = binding.Expression;
        Assert.NotNull(expression);
        Assert.Equal("JavaScript", expression!.Language);
        Assert.Equal("\"Hello \" + \"World\"", expression.Expression);
        Assert.StartsWith("System.String", expression.ResultType?.Id, StringComparison.Ordinal);
        Assert.StartsWith("System.String", binding.Metadata["typeName"], StringComparison.Ordinal);
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
    }

    [Fact]
    public async Task CompilesVariableReferenceInputIntoRuntimeExpressionBinding()
    {
        // #206: a structured Variable reference input must compile into a runtime expression binding
        // whose language is "Variable" and whose expression text round-trips the reference (reference
        // key + declaring scope), so the runtime VariableExpressionHandler resolves it at execution time.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var reference = JsonSerializer.SerializeToElement(new { referenceKey = "var-counter", declaringScopeId = "node-sequence" });
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(reference))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Null(binding.LiteralValue);
        var expression = binding.Expression;
        Assert.NotNull(expression);
        Assert.Equal("Variable", expression!.Language);

        var parsed = JsonSerializer.Deserialize<JsonElement>(expression.Expression);
        Assert.Equal("var-counter", parsed.GetProperty("referenceKey").GetString());
        Assert.Equal("node-sequence", parsed.GetProperty("declaringScopeId").GetString());
    }

    [Fact]
    public async Task CompilesBareVariableReferenceKeyInputIntoRuntimeExpressionBinding()
    {
        // A Variable input may carry just a bare reference key string (workflow-scope reference).
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(JsonSerializer.SerializeToElement("var-counter")))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Variable", binding.Expression!.Language);
        var parsed = JsonSerializer.Deserialize<JsonElement>(binding.Expression.Expression);
        Assert.Equal("var-counter", parsed.GetProperty("referenceKey").GetString());
    }

    [Fact]
    public async Task CompilingVariableInputWithoutReferenceKeyThrows()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-var", VariableText(JsonSerializer.SerializeToElement(new { declaringScopeId = "node-sequence" })))));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());
    }

    [Fact]
    public async Task CompilingUnknownVersionIdThrowsTypedCompilationException()
    {
        // #397: version-source resolution used to run before the try block, so a store lookup failure for an
        // unknown VersionId escaped as a raw ArgumentException that the publish path could not distinguish from
        // a real compilation error. Resolution now runs inside the guarded region, so the failure surfaces as a
        // typed WorkflowExecutableCompilationException (DefinitionId/VersionId unknown because resolution failed).
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "missing-version",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());

        Assert.Null(exception.DefinitionId);
        Assert.Null(exception.DefinitionVersionId);
        Assert.Contains("missing-version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompilingExpressionInputWithoutExpressionTextThrows()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-js", JavaScriptText(""))));

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-")).AsTask());
    }

    [Fact]
    public async Task CompilesBoundTextInputIntoRuntimeLiteralBinding()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-hello", Text("Hello World!"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Text", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal("Text", binding.InputName);
        Assert.Equal("Hello World!", binding.LiteralValue?.GetString());
        Assert.Equal("Text", binding.Metadata["referenceKey"]);
        Assert.StartsWith("System.String", binding.Metadata["typeName"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompiledBoundTextInputMaterializesAuthoredValueForTheRuntime()
    {
        // End-to-end proof for the WriteLine "blank line" bug: the authored text must survive
        // compilation as a runtime input binding and materialize back into the value the runtime
        // feeds to WriteLine.Text. A regression here is exactly what prints a blank line.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-hello", Text("Hello World!"))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var materialized = await new RuntimeActivityInputMaterializer().MaterializeInputsAsync(executable.RootActivity);

        var textInput = Assert.Single(materialized);
        Assert.Equal("Text", textInput.Name);
        Assert.Equal("Hello World!", textInput.Value);
    }

    [Fact]
    public async Task CompiledObjectCollectionInputMaterializesAuthoredArrayForTheRuntime()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(WriteLinesNode("write-lines", ObjectLines(["Hello", "World"]))));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var binding = Assert.Contains("Lines", (IReadOnlyDictionary<string, RuntimeInputBinding>)executable.RootActivity.InputBindings);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(JsonValueKind.Array, binding.LiteralValue?.ValueKind);

        var materialized = await new RuntimeActivityInputMaterializer().MaterializeInputsAsync(executable.RootActivity);

        var linesInput = Assert.Single(materialized);
        var lines = Assert.IsAssignableFrom<ICollection<string>>(linesInput.Value);
        Assert.Equal(["Hello", "World"], lines);
    }

    [Fact]
    public async Task CompilesTransientWorkflowExecutableWithExpirationAndMetadata()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = now.AddMinutes(30);
        var compiler = Compiler(WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.TestRun,
            CreatedAt: now,
            PublishedAt: null,
            ExpiresAt: expiresAt,
            ArtifactIdPrefix: "test-artifact-",
            CompatibilityMetadata: new Dictionary<string, string> { ["runtime.testRunId"] = "testrun-1" }));

        // Scope/expiry are reference facts now (ADR 0040); the compiled artifact is pure behavior. The compile
        // request still carries them (the publish/test-run handlers stamp them onto the reference), but the
        // executable itself only exposes behavior + compatibility metadata.
        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("testrun-1", executable.CompatibilityMetadata["runtime.testRunId"]);
        Assert.Equal(3, executable.Nodes.Count);
    }

    [Fact]
    public async Task CompilesDraftSnapshotWithoutReadingDurableWorkflowVersion()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = TestCompiler.Create(
            new ThrowingVersionStore(),
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "draft:snapshot-1",
            Scope: WorkflowExecutableReferenceScope.TestRun,
            CreatedAt: now,
            PublishedAt: null,
            ExpiresAt: now.AddMinutes(30),
            ArtifactIdPrefix: "test-artifact-")
        {
            Source = new WorkflowExecutableCompileSource(
                DefinitionId: "definition-1",
                DefinitionVersionId: "draft:snapshot-1",
                ArtifactVersion: "draft",
                State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null, null),
                SourceKind: "WorkflowDraftSnapshot",
                SourceId: "snapshot-1",
                SourceVersion: "draft")
        });

        Assert.StartsWith("test-artifact-", executable.Identity.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("definition-1", executable.Identity.DefinitionId);
        Assert.Equal("draft:snapshot-1", executable.Identity.DefinitionVersionId);
    }

    [Fact]
    public async Task CompilesContainerScopedVariablesIntoExecutableStructure()
    {
        // Publishing/runtime materialization (#207): container-scoped variable declarations authored
        // on a Sequence must survive compilation into the executable structure so the runtime can
        // read them without re-reading the design document.
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var counter = new Elsa.Expressions.Core.Models.VariableDefinition(
            ReferenceKey: "var-counter",
            Name: "Counter",
            Type: new TypeReference("String"),
            StorageDriverType: null,
            Default: new ArgumentValue("0", "Literal"));
        var compiler = Compiler(WorkflowVersion(SequenceNode(
            "sequence",
            [Node("write-one", Text("one"))],
            [counter])));

        var executable = await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-"));

        var structure = executable.RootActivity.Structure;
        Assert.NotNull(structure);
        var executableStructure = structure!.Payload.Deserialize<SequenceExecutableStructure>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(executableStructure);
        var materializedVariable = Assert.Single(executableStructure!.Variables);
        Assert.Equal("var-counter", materializedVariable.ReferenceKey);
        Assert.Equal("Counter", materializedVariable.Name);
    }

    [Fact]
    public async Task IndexesResumeTargetHandlerIntoExecutable()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(WorkflowVersion(ResumeNode("delay-1")), _resumeProbeActivity);

        var executable = await compiler.CompileAsync(NewRequest(now));

        var resumeTarget = Assert.Contains(
            "resume-target:probe",
            (IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>)executable.ResumeTargets);
        Assert.Equal("delay-1", resumeTarget.ExecutableNodeId);
        Assert.Equal(nameof(ResumeProbeActivity.OnResumeAsync), resumeTarget.HandlerKey);
    }

    [Fact]
    public async Task ActivitiesWithoutResumeTargetsProduceEmptyResumeTargetMap()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = Compiler(WorkflowVersion(Node("write-one", Text("hello"))));

        var executable = await compiler.CompileAsync(NewRequest(now));

        Assert.Empty(executable.ResumeTargets);
    }

    [Fact]
    public async Task DuplicateResumeTargetIdAcrossNodesFailsCompilation()
    {
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var compiler = ResumeCompiler(
            WorkflowVersion(SequenceNode("seq", [ResumeNode("delay-1"), ResumeNode("delay-2")])),
            _resumeProbeActivity,
            _sequenceActivity);

        await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(NewRequest(now)).AsTask());
    }

    private static WorkflowExecutableCompileRequest NewRequest(DateTimeOffset now) =>
        new(
            VersionId: "version-1",
            Scope: WorkflowExecutableReferenceScope.Published,
            CreatedAt: now,
            PublishedAt: now,
            ExpiresAt: null,
            ArtifactIdPrefix: "artifact-");

    private async Task<WorkflowExecutable> CompileWithDependenciesAsync(
        WorkflowDefinitionVersion workflow,
        IReadOnlyCollection<ExecutableDependencyClaim> dependencies)
    {
        var compiler = TestCompiler.Create(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create(),
            MetadataEnricher(new FixedDependencySource(dependencies)));

        return await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
    }

    private WorkflowExecutableCompiler ValidatedDependencyCompiler(
        WorkflowDefinitionVersion workflow,
        IReadOnlyCollection<ExecutableDependencyClaim> dependencies,
        IWorkflowExecutableStore executableStore) =>
        new(
            new FakeVersionStore(workflow),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            new WorkflowExecutableHasher(),
            new ActivityTreeProjector(_activityStructureService),
            new ExecutableNodeCompiler(
                _activityStructureService,
                TestWellKnownTypeRegistry.Create(),
                new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create())),
            MetadataEnricher(new FixedDependencySource(dependencies)),
            executableStore);

    private WorkflowExecutable StoredExecutable(
        WorkflowExecutableIdentity identity,
        params WorkflowExecutableDependency[] dependencies) =>
        TestExecutable.Create(identity, dependencies);

    private static WorkflowExecutableIdentity StoredIdentity(
        string artifactId,
        string artifactHash,
        string definitionId = "definition-child",
        string definitionVersionId = "version-child") =>
        TestExecutable.Identity(artifactId, artifactHash, definitionId, definitionVersionId);

    private WorkflowExecutableCompiler ResumeCompiler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activities)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ResumeProbeActivity), typeof(ResumeProbeActivity).FullName!);
        registry.RegisterType(typeof(SequenceActivity), typeof(SequenceActivity).FullName!);

        return TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([.. activities]),
            _activityStructureService,
            registry);
    }

    private static ActivityNode ResumeNode(string nodeId) => new(nodeId, "activity-probe", Inputs: [], Outputs: []);

    private readonly ActivityDefinitionVersion _resumeProbeActivity = ActivityVersion("activity-probe", typeof(ResumeProbeActivity).FullName!);

    // A minimal type carrying a [ResumeTarget] handler. The compiler reflects the attribute off the node's
    // resolved CLR type, so the type need not be a full activity for this indexing test.
    private sealed class ResumeProbeActivity
    {
        [ResumeTarget("resume-target:probe")]
        public ValueTask OnResumeAsync() => ValueTask.CompletedTask;
    }

    private WorkflowExecutableCompiler Compiler(WorkflowDefinitionVersion workflowVersion)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(LegacyTriggerActivity), TypeAliasConvention.CanonicalAlias(typeof(LegacyTriggerActivity)));
        return TestCompiler.Create(
            new FakeVersionStore(workflowVersion),
            new FakeActivityVersionStore([_writeLineActivity, _writeLinesActivity, _sequenceActivity, _legacyTriggerActivity]),
            _activityStructureService,
            registry);
    }

    private static WorkflowDefinitionVersion WorkflowVersion(
        ActivityNode? rootActivity,
        IReadOnlyCollection<InputDefinition>? inputs = null) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, inputs ?? [], [], null, null)
        };

    private static InputDefinition WorkflowInput(
        string name,
        TypeReference type,
        bool isRequired,
        JsonElement? defaultValue = null,
        string? defaultSyntax = null) =>
        new(
            ReferenceKey: $"input-{name}",
            Name: name,
            Type: type,
            StorageDriverType: null,
            DisplayName: name,
            Category: null,
            IsRequired: isRequired,
            DefaultValue: defaultValue,
            DefaultSyntax: defaultSyntax);

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static ActivityNode WriteLinesNode(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-lines", inputs, Outputs: []);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition>? variables = null) =>
        new(
            nodeId,
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities, variables))));

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static WorkflowArgumentState JavaScriptText(string expression) =>
        new("Text", new ArgumentValue(expression, "JavaScript"), null, null, null, null);

    private static WorkflowArgumentState VariableText(JsonElement reference) =>
        new("Text", new ArgumentValue(reference, "Variable"), null, null, null, null);

    private static WorkflowArgumentState ObjectLines(IReadOnlyCollection<string> lines) =>
        new("Lines", new ArgumentValue(JsonSerializer.SerializeToElement(lines), "Object"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(id, "Test.WriteLine", [new InputDefinition(inputName, inputName, inputType, null, inputName, null)]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey, IReadOnlyCollection<InputDefinition>? inputs = null) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = inputs ?? []
        };

    private static ActivityDefinitionVersion LegacyTriggerActivityVersion() =>
        LegacyTriggerActivityVersion(typeof(LegacyTriggerActivity));

    private static ActivityDefinitionVersion LegacyTriggerActivityVersion(Type activityType) =>
        new("1.0.0", "legacy-trigger-definition")
        {
            Id = "activity-legacy-trigger",
            Definition = new ActivityDefinition
            {
                Id = "legacy-trigger-definition",
                ActivityTypeKey = activityType.FullName!,
                Category = "Test"
            },
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExecutionType = ActivityExecutionType.Action,
            Inputs = []
        };

    [TriggerActivity]
    private sealed class LegacyTriggerActivity
    {
        public const string ActivityType = "Elsa.Test.LegacyTrigger";
    }

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Draft snapshot compilation must not read durable workflow definition versions.");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ExecutableNodeMetadataEnricher MetadataEnricher(params IExecutableCompilationSource[] sources) =>
        new(new CollectingInlineEventPublisher(sources));

    private sealed class FixedMetadataSource(string nodeId, string key, string value) : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationContribution(
                nodeMetadata: [new ExecutableNodeMetadataClaim(nodeId, key, value)]));
    }

    private sealed class FixedDependencySource(IReadOnlyCollection<ExecutableDependencyClaim> claims) : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationContribution(dependencies: claims));
    }

    private sealed class CollectingInlineEventPublisher(IEnumerable<IExecutableCompilationSource> sources) : IInlineEventPublisher
    {
        private readonly CollectExecutableCompilation _handler = new(sources);

        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            _handler.Handle(Assert.IsType<OnExecutableCompilationCollecting>(@event), cancellationToken);
    }
}
