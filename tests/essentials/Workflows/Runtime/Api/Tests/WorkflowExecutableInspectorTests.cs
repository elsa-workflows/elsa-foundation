using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Primitives.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED ownership and HTTP contract for moving executable inspection out of Publishing.</summary>
public sealed class WorkflowExecutableInspectorTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _executionStore = new();
    private readonly WorkflowExecutableInspector _inspector;

    public WorkflowExecutableInspectorTests()
    {
        _inspector = new WorkflowExecutableInspector(
            _executableStore,
            _referenceStore,
            _executionStore,
            new FakeTimeProvider(_now));
    }

    [Fact]
    public void Runtime_owns_the_self_contained_executable_inspector()
    {
        var inspector = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowExecutableInspector");
        Assert.NotNull(inspector);
        var dependencies = inspector!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();

        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableSourceReferenceStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore", dependencies);
        Assert.DoesNotContain(dependencies, dependency => dependency?.Contains("Design", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Source_kind_is_canonical_and_source_type_is_a_version_one_compatibility_alias()
    {
        var responseType = typeof(Models.ExecutableSourceReferenceView);
        var canonical = responseType.GetProperty(nameof(Models.ExecutableSourceReferenceView.SourceKind));
        var compatibilityAlias = responseType.GetProperty("SourceType");

        Assert.NotNull(canonical);
        Assert.NotNull(compatibilityAlias);
        var versionOneConstructor = Assert.Single(
            responseType.GetConstructors(),
            constructor => constructor.GetParameters().Any(parameter => parameter.Name == "SourceType"));
        var versionOneParameters = versionOneConstructor.GetParameters();
        Assert.Equal(
            [
                "SourceReferenceId", "ArtifactId", "Scope", "SourceType", "SourceKind", "SourceId", "SourceVersion",
                "DefinitionId", "DefinitionVersionId", "ArtifactVersion", "PublicationId", "SlotId", "CreatedAt",
                "PublishedAt", "ExpiresAt", "DeletedAt", "DeletedReason", "Live"
            ],
            versionOneParameters.Select(parameter => parameter.Name));
        Assert.Equal(
            [
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset),
                typeof(DateTimeOffset?), typeof(DateTimeOffset?), typeof(DateTimeOffset?), typeof(string), typeof(bool)
            ],
            versionOneParameters.Select(parameter => parameter.ParameterType));
        Assert.NotNull(compatibilityAlias!.SetMethod);
        Assert.True(compatibilityAlias.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
        var obsolete = Assert.Single(compatibilityAlias!.GetCustomAttributes<ObsoleteAttribute>());
        Assert.False(obsolete.IsError);
        Assert.Contains("Runtime API v2", obsolete.Message, StringComparison.Ordinal);

        var view = Models.ExecutableSourceReferenceView.From(
            Reference("reference-contract", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"),
            _now);
        var json = JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("WorkflowDefinitionVersion", json.GetProperty("sourceKind").GetString());
        Assert.Equal(json.GetProperty("sourceKind").GetString(), json.GetProperty("sourceType").GetString());
    }

    [Fact]
    public void Version_one_constructor_promotes_source_type_when_source_kind_is_absent()
    {
        var view = CreateVersionOneView("WorkflowDefinitionVersion", null);

        Assert.Equal("WorkflowDefinitionVersion", view.SourceKind);
#pragma warning disable CS0618 // The obsolete property is the compatibility contract under test.
        Assert.Equal(view.SourceKind, view.SourceType);
#pragma warning restore CS0618
        var json = JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(json.GetProperty("sourceKind").GetString(), json.GetProperty("sourceType").GetString());
    }

    [Fact]
    public void Version_one_source_type_initializer_promotes_the_canonical_source_kind()
    {
#pragma warning disable CS0618 // The obsolete init accessor is the compatibility contract under test.
        var view = new Models.ExecutableSourceReferenceView(
            SourceReferenceId: "reference-init",
            ArtifactId: "artifact-init",
            Scope: "Published",
            SourceKind: null,
            SourceId: "definition-version-init",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-init",
            DefinitionVersionId: "definition-version-init",
            ArtifactVersion: "1.0.0",
            PublicationId: null,
            SlotId: null,
            CreatedAt: _now,
            PublishedAt: _now,
            ExpiresAt: null,
            DeletedAt: null,
            DeletedReason: null,
            Live: true)
        {
            SourceType = "WorkflowDefinitionVersion"
        };
#pragma warning restore CS0618

        Assert.Equal("WorkflowDefinitionVersion", view.SourceKind);
        var json = JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(json.GetProperty("sourceKind").GetString(), json.GetProperty("sourceType").GetString());
    }

    [Fact]
    public void Version_one_alias_rejects_values_that_diverge_from_source_kind()
    {
        Assert.Throws<ArgumentException>(() => CreateVersionOneView("LegacySource", "CanonicalSource"));
        var canonical = Models.ExecutableSourceReferenceView.From(
            Reference("reference-divergent", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"),
            _now);

#pragma warning disable CS0618 // The obsolete init accessor is the compatibility contract under test.
        Assert.Throws<ArgumentException>(() => canonical with { SourceType = "LegacySource" });
#pragma warning restore CS0618
    }

    [Fact]
    public void Version_one_json_promotes_source_type_when_source_kind_is_absent()
    {
        const string payload = """
                               {
                                 "sourceReferenceId": "reference-json-v1",
                                 "artifactId": "artifact-json-v1",
                                 "scope": "Published",
                                 "sourceType": "WorkflowDefinitionVersion",
                                 "definitionId": "definition-json-v1",
                                 "definitionVersionId": "definition-version-json-v1",
                                 "artifactVersion": "1.0.0",
                                 "createdAt": "2026-07-13T12:00:00+00:00",
                                 "live": true
                               }
                               """;

        var view = JsonSerializer.Deserialize<Models.ExecutableSourceReferenceView>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(view);
        Assert.Equal("WorkflowDefinitionVersion", view.SourceKind);
        var json = JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(json.GetProperty("sourceKind").GetString(), json.GetProperty("sourceType").GetString());
    }

    [Fact]
    public void Version_one_deconstruction_preserves_the_exact_contract_and_equal_aliases()
    {
        var responseType = typeof(Models.ExecutableSourceReferenceView);
        var deconstruct = Assert.Single(
            responseType.GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "Deconstruct" && method.GetParameters().Length == 18);
        var parameters = deconstruct.GetParameters();
        Assert.Equal(
            [
                "SourceReferenceId", "ArtifactId", "Scope", "SourceType", "SourceKind", "SourceId", "SourceVersion",
                "DefinitionId", "DefinitionVersionId", "ArtifactVersion", "PublicationId", "SlotId", "CreatedAt",
                "PublishedAt", "ExpiresAt", "DeletedAt", "DeletedReason", "Live"
            ],
            parameters.Select(parameter => parameter.Name));
        Assert.Equal(
            [
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset),
                typeof(DateTimeOffset?), typeof(DateTimeOffset?), typeof(DateTimeOffset?), typeof(string), typeof(bool)
            ],
            parameters.Select(parameter => parameter.ParameterType.GetElementType()));
        Assert.All(parameters, parameter => Assert.True(parameter.IsOut));
        var obsolete = Assert.Single(deconstruct.GetCustomAttributes<ObsoleteAttribute>());
        Assert.False(obsolete.IsError);
        Assert.Contains("Runtime API v2", obsolete.Message, StringComparison.Ordinal);

        var view = CreateVersionOneView("WorkflowDefinitionVersion", null);
#pragma warning disable CS0618 // The obsolete v1 deconstructor is the source compatibility contract under test.
        var (
            sourceReferenceId,
            artifactId,
            scope,
            sourceType,
            sourceKind,
            sourceId,
            sourceVersion,
            definitionId,
            definitionVersionId,
            artifactVersion,
            activationId,
            slotId,
            createdAt,
            publishedAt,
            expiresAt,
            deletedAt,
            deletedReason,
            live) = view;
#pragma warning restore CS0618

        Assert.Equal("reference-v1", sourceReferenceId);
        Assert.Equal("artifact-v1", artifactId);
        Assert.Equal("Published", scope);
        Assert.Equal("WorkflowDefinitionVersion", sourceType);
        Assert.Equal(sourceKind, sourceType);
        Assert.Equal("definition-version-v1", sourceId);
        Assert.Equal("1.0.0", sourceVersion);
        Assert.Equal("definition-v1", definitionId);
        Assert.Equal("definition-version-v1", definitionVersionId);
        Assert.Equal("1.0.0", artifactVersion);
        Assert.Null(activationId);
        Assert.Null(slotId);
        Assert.Equal(_now, createdAt);
        Assert.Equal(_now, publishedAt);
        Assert.Null(expiresAt);
        Assert.Null(deletedAt);
        Assert.Null(deletedReason);
        Assert.True(live);
    }

    [Theory]
    [InlineData("runtime/workflows/executables")]
    [InlineData("runtime/workflows/executables/{artifactId}")]
    [InlineData("runtime/workflows/executables/{artifactId}/provenance")]
    public void Runtime_owns_each_canonical_executable_read_route(string route)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Authored_input_source_route_requires_publishing_read_independently_of_runtime_evidence()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(
            "runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources");

        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowPublishingRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
        AssertProperties(RuntimeApiEndpointTestFactory.Contract(endpoint).Response,
            "ArtifactId", "SourceReferenceId", "AccessState", "AuthoredInputs", "CompiledInputs");
    }

    [Fact]
    public void Executable_list_exposes_retention_counts_without_definition_reads()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables")).Response;
        var items = response.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(items);
        var row = ElementType(items!.PropertyType);

        AssertProperties(
            row,
            "ArtifactId", "ArtifactVersion", "DefinitionId", "DefinitionVersionId", "RootActivityVersion",
            "ResumeTargetCount", "References", "LiveSourceReferenceCount", "RetainedExecutionCount");
    }

    [Fact]
    public void Executable_detail_exposes_compact_connection_endpoints_on_each_node()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables/{artifactId}")).Response;
        var rootActivity = response.GetProperty("RootActivity", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(rootActivity);
        var connections = rootActivity!.PropertyType.GetProperty("Connections", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(connections);
        var connection = ElementType(connections!.PropertyType);
        AssertProperties(connection, "Source", "Target");
        AssertProperties(connection.GetProperty("Source")!.PropertyType, "NodeId", "Port");
    }

    [Fact]
    public void Provenance_is_read_only_and_reports_collection_protection()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables/{artifactId}/provenance")).Response;

        AssertProperties(response, "ArtifactId", "SourceReferences", "RetainedExecutionCount", "ProtectedFromCollection");
        var references = response.GetProperty("SourceReferences", BindingFlags.Public | BindingFlags.Instance);
        AssertProperties(ElementType(references!.PropertyType), "DefinitionId", "DefinitionVersionId", "ArtifactVersion");
    }

    [Fact]
    public async Task Inspector_reports_live_source_and_retained_execution_roots()
    {
        var executable = await ActivateAsync(new WorkflowExecutableSourceReference(
            "reference-1", "artifact-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
            "definition-1", "version-1", "1.0.0", _now, _now,
            WorkflowExecutableReferenceScope.Published));
        await _executionStore.SaveAsync(new WorkflowExecutionState(
            "execution-1", executable.Identity, WorkflowExecutionStatus.Completed, null, _now, _now, _now, _now,
            null, null, null, new Dictionary<string, string>()));

        var summary = Assert.Single((await _inspector.ListAsync()).Items);
        var provenance = await _inspector.GetProvenanceAsync("artifact-1");

        Assert.Equal(1, summary.LiveSourceReferenceCount);
        Assert.Equal(1, summary.RetainedExecutionCount);
        Assert.True(provenance!.ProtectedFromCollection);
        Assert.Equal(1, provenance.RetainedExecutionCount);
        Assert.True(Assert.Single(provenance.SourceReferences).Live);
        var source = Assert.Single(provenance.SourceReferences);
        Assert.Equal("definition-1", source.DefinitionId);
        Assert.Equal("version-1", source.DefinitionVersionId);
        Assert.Equal("1.0.0", source.ArtifactVersion);
        Assert.Equal("WorkflowDefinitionVersion", source.SourceKind);
        Assert.Equal("reference-1", Assert.Single(summary.References).SourceReferenceId);
    }

    [Fact]
    public async Task Detail_honors_requested_reference_and_defaults_to_live_published_reference()
    {
        await ActivateAsync(
            Reference("published-old", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-20), "1.0.0"),
            Reference("published-new", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-10), "2.0.0"),
            Reference("test-newest", WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1), "draft", _now.AddHours(1)));

        var defaultDetail = await _inspector.GetAsync("artifact-1");
        var requestedDetail = await _inspector.GetAsync("artifact-1", "published-old");

        Assert.Equal("published-new", defaultDetail!.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest-live", defaultDetail.ChosenReference.Selection);
        Assert.Equal("published-old", requestedDetail!.ChosenReference!.SourceReferenceId);
        Assert.Equal("requested", requestedDetail.ChosenReference.Selection);
        Assert.Equal(
            ["test-newest", "published-new", "published-old"],
            defaultDetail.References.Select(reference => reference.SourceReferenceId));

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _inspector.GetAsync("artifact-1", "missing-reference").AsTask());
        Assert.Contains("missing-reference", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_projects_frozen_activity_presentation_from_the_chosen_reference()
    {
        await ActivateAsync(Reference(
            "published-presentation",
            WorkflowExecutableReferenceScope.Published,
            _now,
            "1.0.0") with
        {
            ActivityPresentation =
            [
                new WorkflowExecutableActivityPresentationRecord(
                    "root",
                    "Notify buyer",
                    "Send the confirmation after payment.")
            ]
        });

        var detail = await _inspector.GetAsync("artifact-1");
        var presentation = Assert.Single(detail!.ChosenReference!.ActivityPresentation);

        Assert.Equal("root", presentation.ExecutableNodeId);
        Assert.Equal("Notify buyer", presentation.DisplayName);
        Assert.Equal("Send the confirmation after payment.", presentation.Description);
    }

    [Fact]
    public async Task Detail_rejects_explicit_retired_reference_without_substituting_live_layout()
    {
        await ActivateAsync(
            Reference("published-live", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-10), "2.0.0"),
            Reference("published-retired", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-20), "1.0.0")
                .Retire(_now.AddMinutes(-5), "superseded"));

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _inspector.GetAsync("artifact-1", "published-retired").AsTask());
        var defaultDetail = await _inspector.GetAsync("artifact-1");

        Assert.Contains("published-retired", exception.Message, StringComparison.Ordinal);
        Assert.Equal("published-live", defaultDetail!.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest-live", defaultDetail.ChosenReference.Selection);
    }

    [Fact]
    public async Task List_includes_reference_less_artifact_only_when_retained_history_is_requested()
    {
        var executable = await ActivateAsync();

        Assert.Empty((await _inspector.ListAsync(includeRetired: true)).Items);
        await _executionStore.SaveAsync(new WorkflowExecutionState(
            "execution-1", executable.Identity, WorkflowExecutionStatus.Completed, null, _now, _now, _now, _now,
            null, null, null, new Dictionary<string, string>()));

        Assert.Empty((await _inspector.ListAsync()).Items);
        var retained = Assert.Single((await _inspector.ListAsync(includeRetired: true)).Items);
        Assert.Equal(1, retained.RetainedExecutionCount);
        Assert.Empty(retained.References);
    }

    [Fact]
    public async Task List_and_detail_fall_back_to_newest_retired_reference_when_retired_history_is_requested()
    {
        await ActivateAsync(Reference(
            "retired-reference", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-1), "2.0.0")
            .Retire(_now, "superseded"));

        var summary = Assert.Single((await _inspector.ListAsync(includeRetired: true)).Items);
        var detail = await _inspector.GetAsync("artifact-1");

        Assert.Equal("2.0.0", summary.ArtifactVersion);
        Assert.Equal("retired-reference", Assert.Single(summary.References).SourceReferenceId);
        Assert.Equal("retired-reference", detail!.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest", detail.ChosenReference.Selection);
    }

    [Fact]
    public async Task Detail_uses_live_test_run_when_no_live_published_reference_exists()
    {
        await ActivateAsync(Reference(
            "test-reference", WorkflowExecutableReferenceScope.TestRun, _now, "draft", _now.AddHours(1)));

        var detail = await _inspector.GetAsync("artifact-1");

        Assert.Equal("test-reference", detail!.ChosenReference!.SourceReferenceId);
        Assert.Equal("newest-live", detail.ChosenReference.Selection);
    }

    [Fact]
    public async Task Detail_projects_nodes_without_descriptor_payloads()
    {
        await ActivateAsync(descriptor: JsonSerializer.SerializeToElement(new { secret = "must-not-leak" }));

        var detail = await _inspector.GetAsync("artifact-1");
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Null(detail!.ChosenReference);
        Assert.Empty(detail.References);
        Assert.DoesNotContain("descriptorPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-leak", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailScrubsSourcePayloadWhilePublishingProjectionReturnsStructuredBindingWithoutSummaryParsing()
    {
        var binding = new RuntimeInputBinding(
            "text-input-key",
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                "return variables.orderId;",
                new RuntimeValueTypeDescriptor("clr", "System.String, System.Private.CoreLib", null)),
            metadata: new Dictionary<string, string> { ["typeName"] = "System.String, System.Private.CoreLib" });
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding>
        {
            ["text-input-key"] = binding
        }));
        await _referenceStore.SaveAsync(Reference("source-1", WorkflowExecutableReferenceScope.Published, _now, "1.0.0") with
        {
            AuthoredInputs =
            [
                new WorkflowExecutableAuthoredInputRecord(
                    "root",
                    "text-input-key",
                    "JavaScript",
                    JsonSerializer.SerializeToElement("return variables.orderId;"))
            ]
        });

        var detail = await _inspector.GetAsync("artifact-1");

        var projected = Assert.Single(detail!.RootActivity.InputBindings);
        Assert.Equal("text-input-key", projected.InputName);
        Assert.Equal("text-input-key", projected.InputKey);
        Assert.False(projected.IsSensitive);
        Assert.Equal("Expression", projected.Source);
        Assert.Null(projected.Summary);
        Assert.Null(projected.Expression);
        Assert.Null(projected.LiteralValue);
        Assert.Null(projected.Metadata);

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-1");
        var authored = Assert.Single(sources!.AuthoredInputs);
        Assert.Equal("allowed", authored.AccessState);
        Assert.Equal("return variables.orderId;", authored.Value!.Value.GetString());
        var compiled = Assert.Single(sources.CompiledInputs).Binding;
        Assert.Equal("JavaScript", compiled.Expression!.Language);
        Assert.Equal("return variables.orderId;", compiled.Expression.Expression);
    }

    [Fact]
    public async Task InputSourcesRedactSensitiveAuthoredAndCompiledPayloads()
    {
        var binding = new RuntimeInputBinding(
            "secret-key",
            new ValueTypeDescriptor("String"),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true),
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("must-not-leak"),
                new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true)));
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding> { ["secret-key"] = binding }));
        await _referenceStore.SaveAsync(Reference("source-sensitive", WorkflowExecutableReferenceScope.Published, _now, "1.0.0") with
        {
            AuthoredInputs = [new WorkflowExecutableAuthoredInputRecord("executable-root", "secret-key", "Literal", JsonSerializer.SerializeToElement("must-not-leak"), true)]
        });

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-sensitive");
        var json = JsonSerializer.Serialize(sources, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("redacted", Assert.Single(sources!.AuthoredInputs).AccessState);
        Assert.Null(Assert.Single(sources.AuthoredInputs).Value);
        Assert.Equal("redacted", Assert.Single(sources.CompiledInputs).AccessState);
        Assert.Null(Assert.Single(sources.CompiledInputs).Binding.LiteralValue);
        Assert.DoesNotContain("must-not-leak", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A secret reference is not a value (spec 188, T067), so the input-sources view shows a secret read's reference as its
    /// summary and in <c>secret</c>, although its policy keeps every other source detail out. The structural detail view
    /// keeps every source detail out, the reference included.
    /// </summary>
    [Fact]
    public async Task InputSourcesShowASecretReadByItsReferenceAndNeverAValue()
    {
        var reference = new RuntimeSecretReference("payments.api-key", "text", "billing");
        var binding = new RuntimeInputBinding(
            "api-key",
            StringType,
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead,
            conversionPlan: ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue),
            secret: reference);
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding> { ["api-key"] = binding }));
        await _referenceStore.SaveAsync(Reference("source-secret", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"));

        var compiled = Assert.Single((await _inspector.GetInputSourcesAsync("artifact-1", "source-secret"))!.CompiledInputs);
        var structural = Assert.Single((await _inspector.GetAsync("artifact-1"))!.RootActivity.InputBindings);

        Assert.Equal("redacted", compiled.AccessState);
        Assert.Equal(nameof(RuntimeInputBindingSource.SecretRead), compiled.Binding.Source);
        Assert.Equal("payments.api-key", compiled.Binding.Summary);
        Assert.Equal(reference, compiled.Binding.Secret);
        Assert.True(compiled.Binding.IsSensitive);
        Assert.Null(compiled.Binding.LiteralValue);
        Assert.Null(compiled.Binding.ConversionPlan);
        Assert.Null(structural.Summary);
        Assert.Null(structural.Secret);
    }

    /// <summary>
    /// Studio prints a compiled binding's summary verbatim, so a binding whose policy marks its value sensitive or as
    /// requiring encryption carries no value in it, nor anywhere else on the wire (spec 188, T070).
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task InputSourcesKeepTheValueOutOfABindingWhosePolicyIsSensitiveOrRequiresEncryption(bool isSensitive, bool requiresEncryption)
    {
        var sentinel = $"plain{Guid.NewGuid():N}";
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive, requiresEncryption);
        var binding = new RuntimeInputBinding(
            "token",
            StringType,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(sentinel), policy));
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding> { ["token"] = binding }));
        await _referenceStore.SaveAsync(Reference("source-protected", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"));

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-protected");
        var compiled = Assert.Single(sources!.CompiledInputs);

        Assert.Equal("redacted", compiled.AccessState);
        Assert.True(compiled.Binding.IsSensitive);
        Assert.Null(compiled.Binding.Summary);
        Assert.Null(compiled.Binding.LiteralValue);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(sources, WebJson), StringComparison.Ordinal);
    }

    /// <summary>
    /// The authored record carries only the author's own sensitivity flag; an activity's declaration reaches the compiled
    /// binding and the pinned contract (spec 188, found in slice 5). The authored-inputs view redacts by those too, and
    /// redacts an input the executable holds neither for.
    /// </summary>
    [Theory]
    [InlineData("declared on the compiled binding")]
    [InlineData("declared on the pinned contract only")]
    [InlineData("no compiled input")]
    public async Task InputSourcesRedactAnAuthoredValueByTheEffectivePolicyNotOnlyItsAuthoredFlag(string row)
    {
        var sentinel = $"plain{Guid.NewGuid():N}";
        await _executableStore.SaveAsync(Executable(_now, AuthoredRedactionRoots[row]()));
        await _referenceStore.SaveAsync(Reference("source-authored", WorkflowExecutableReferenceScope.Published, _now, "1.0.0") with
        {
            AuthoredInputs = [new WorkflowExecutableAuthoredInputRecord("root", "token", "Literal", JsonSerializer.SerializeToElement(sentinel))]
        });

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-authored");
        var authored = Assert.Single(sources!.AuthoredInputs);

        Assert.Equal("redacted", authored.AccessState);
        Assert.True(authored.IsSensitive);
        Assert.Null(authored.Value);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(sources, WebJson), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputSourcesShowAnAuthoredValueOfAReusableActivityPlacedAsTheRootByItsAuthoredNodeId()
    {
        // A reusable activity placed as the workflow root keeps a generated executable id; its authored id still matches.
        var root = Node("placed-1", "root", new Dictionary<string, RuntimeInputBinding> { ["token"] = Literal("token", "visible", ValueProtectionPolicy.InstanceInline) });
        await _executableStore.SaveAsync(Executable(_now, root));
        await _referenceStore.SaveAsync(Reference("source-placed", WorkflowExecutableReferenceScope.Published, _now, "1.0.0") with
        {
            AuthoredInputs = [new WorkflowExecutableAuthoredInputRecord("root", "token", "Literal", JsonSerializer.SerializeToElement("visible"))]
        });

        var authored = Assert.Single((await _inspector.GetInputSourcesAsync("artifact-1", "source-placed"))!.AuthoredInputs);

        Assert.Equal("allowed", authored.AccessState);
        Assert.Equal("visible", authored.Value!.Value.GetString());
    }

    [Fact]
    public async Task InputSourcesExposeResolvedConversionPlansForNonSensitiveCompiledBindings()
    {
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"),
            new ValueTypeDescriptor("Elsa.Any"),
            ValueConversionMode.Json,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            ValueConversionLimits.Default,
            options: null);
        var binding = new RuntimeInputBinding(
            "payload",
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: plan);
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding> { ["payload"] = binding }));
        await _referenceStore.SaveAsync(Reference("source-conversion", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"));

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-conversion");

        var conversion = Assert.Single(sources!.CompiledInputs).Binding.ConversionPlan!;
        Assert.Equal(ValueConversionMode.Json, conversion.Mode);
        Assert.Equal(ValueRepresentation.FormattedContent, conversion.SourceRepresentation);
        Assert.Equal("String", conversion.SourceType.Alias);
        Assert.Equal("Elsa.Any", conversion.TargetType.Alias);
        Assert.Equal("elsa.json", conversion.Profile!.Id);
        Assert.Equal("1", conversion.Profile.Version);
        Assert.Equal(plan.Fingerprint, conversion.Fingerprint);
    }

    [Fact]
    public async Task InputSourcesRedactConversionPlansForSensitiveCompiledBindings()
    {
        var plan = ValueConversionPlan.Identity(new ValueTypeDescriptor("String"), ValueRepresentation.TextValue);
        var binding = new RuntimeInputBinding(
            "secret-key",
            new ValueTypeDescriptor("String"),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true),
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("secret"),
                new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true)),
            conversionPlan: plan);
        await _executableStore.SaveAsync(Executable(_now, inputBindings: new Dictionary<string, RuntimeInputBinding> { ["secret-key"] = binding }));
        await _referenceStore.SaveAsync(Reference("source-sensitive-conversion", WorkflowExecutableReferenceScope.Published, _now, "1.0.0"));

        var sources = await _inspector.GetInputSourcesAsync("artifact-1", "source-sensitive-conversion");

        Assert.Null(Assert.Single(sources!.CompiledInputs).Binding.ConversionPlan);
    }

    [Fact]
    public async Task DetailExposesOutputCaptureConversionPlansForPublishedExecutableInspection()
    {
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"),
            new ValueTypeDescriptor("Acme.Customer"),
            ValueConversionMode.Xml,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.xml", "1"),
            ValueConversionLimits.Default,
            options: null);
        var capture = new RuntimeOutputCapture(
            "body",
            "customer",
            new RuntimeValueTypeDescriptor("alias", "Acme.Customer", null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            captureOnSuccessfulCompletion: true,
            conversionPlan: plan);
        await _executableStore.SaveAsync(Executable(
            _now,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["body"] = capture }));

        var detail = await _inspector.GetAsync("artifact-1");

        var projected = Assert.Single(detail!.RootActivity.OutputCaptures!);
        Assert.Equal("body", projected.OutputName);
        Assert.Equal("customer", projected.ValueId);
        Assert.Equal("Acme.Customer", projected.Type.Id);
        Assert.Equal("Instance", projected.Lifecycle);
        Assert.Equal("Inline", projected.Storage);
        Assert.Equal(ValueConversionMode.Xml, projected.ConversionPlan!.Mode);
        Assert.Equal("elsa.xml", projected.ConversionPlan.Profile!.Id);
        Assert.Equal(plan.Fingerprint, projected.ConversionPlan.Fingerprint);
    }

    [Fact]
    public async Task Detail_projects_immutable_flowchart_connections_with_ports()
    {
        var structure = new ExecutableActivityStructure(
            "elsa.flowchart.structure",
            "1.0.0",
            JsonSerializer.SerializeToElement(new
            {
                connections = new[]
                {
                    new
                    {
                        source = new { nodeId = "approve-order", port = "Approved" },
                        target = new { nodeId = "send-email", port = "In" }
                    }
                },
                startNodeId = "approve-order"
            }));
        await _executableStore.SaveAsync(Executable(_now, structure: structure));

        var detail = await _inspector.GetAsync("artifact-1");

        var connection = Assert.Single(detail!.RootActivity.Connections);
        Assert.Equal("approve-order", connection.Source.NodeId);
        Assert.Equal("Approved", connection.Source.Port);
        Assert.Equal("send-email", connection.Target.NodeId);
        Assert.Equal("In", connection.Target.Port);
        Assert.Null(detail.RootActivity.BpmnStructure);
        var json = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("rootActivity").GetProperty("bpmnStructure").ValueKind);
    }

    [Fact]
    public async Task Detail_projects_only_compact_bpmn_elements_and_sequence_flows()
    {
        var structure = new ExecutableActivityStructure(
            "elsa.bpmn.structure",
            "1.0.0",
            JsonSerializer.SerializeToElement(new
            {
                elements = new object[]
                {
                    new { elementId = "start", elementType = "startEvent", name = "Begin", secret = "element-secret" },
                    new { elementId = "task", elementType = "task", childNodeId = "writer", name = "Write order", properties = new { expression = "element-payload-secret" } },
                    new { elementId = "end", elementType = "endEvent", eventDefinitions = new[] { "event-secret" } }
                },
                sequenceFlows = new object[]
                {
                    new { flowId = "start-to-task", sourceRef = "start", targetRef = "task", name = "continue", conditionOutcome = "Continue", isDefault = false, conditionExpression = "flow-secret" },
                    new { flowId = "task-to-end", sourceRef = "task", targetRef = "end", isDefault = true }
                },
                variables = new[] { new { name = "secret-variable" } },
                diagram = new { bounds = "diagram-secret" },
                rawXml = "raw-xml-secret"
            }));
        var writer = new ExecutableNode(
            "writer",
            "authored-writer",
            "Elsa.Activities.Primitives.Activities.WriteLine",
            "1.0.0",
            new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        await _executableStore.SaveAsync(Executable(
            _now,
            structure: structure,
            childSlots: [new ExecutableChildSlot("Bpmn.Activities", [writer])],
            activityType: "Elsa.Activities.Bpmn.Activities.BpmnProcess"));

        var detail = await _inspector.GetAsync("artifact-1");
        var json = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsType<Models.WorkflowExecutableBpmnStructureView>(detail!.RootActivity.BpmnStructure);
        var bpmnJson = json.GetProperty("rootActivity").GetProperty("bpmnStructure");
        var elements = bpmnJson.GetProperty("elements").EnumerateArray().ToArray();
        var sequenceFlows = bpmnJson.GetProperty("sequenceFlows").EnumerateArray().ToArray();

        Assert.Equal(3, elements.Length);
        Assert.Equal("start", elements[0].GetProperty("elementId").GetString());
        Assert.Equal("startEvent", elements[0].GetProperty("elementType").GetString());
        Assert.Equal("Begin", elements[0].GetProperty("name").GetString());
        Assert.False(elements[0].TryGetProperty("childNodeId", out _));
        Assert.Equal("writer", elements[1].GetProperty("childNodeId").GetString());
        Assert.Equal("Write order", elements[1].GetProperty("name").GetString());
        Assert.Equal(4, elements[1].EnumerateObject().Count());
        Assert.False(elements[2].TryGetProperty("eventDefinitions", out _));
        Assert.Equal(2, sequenceFlows.Length);
        Assert.Equal("start-to-task", sequenceFlows[0].GetProperty("flowId").GetString());
        Assert.Equal("start", sequenceFlows[0].GetProperty("sourceRef").GetString());
        Assert.Equal("task", sequenceFlows[0].GetProperty("targetRef").GetString());
        Assert.Equal("continue", sequenceFlows[0].GetProperty("name").GetString());
        Assert.Equal("Continue", sequenceFlows[0].GetProperty("conditionOutcome").GetString());
        Assert.False(sequenceFlows[0].GetProperty("isDefault").GetBoolean());
        Assert.True(sequenceFlows[1].GetProperty("isDefault").GetBoolean());
        Assert.Equal(6, sequenceFlows[0].EnumerateObject().Count());
        Assert.DoesNotContain("element-secret", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("element-payload-secret", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("event-secret", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("flow-secret", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-variable", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("diagram-secret", json.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-xml-secret", json.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_preserves_bpmn_events_without_activity_child_slots_and_skips_malformed_entries()
    {
        await _executableStore.SaveAsync(Executable(
            _now,
            structure: new ExecutableActivityStructure(
                "elsa.bpmn.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    elements = new object[]
                    {
                        new { elementId = "start", elementType = "startEvent" },
                        new { elementId = "task", elementType = "task", childNodeId = 42, name = true },
                        new { elementId = "end", elementType = "endEvent" },
                        new { elementId = "malformed-element" },
                        "not-an-element"
                    },
                    sequenceFlows = new object[]
                    {
                        new { flowId = "start-to-end", sourceRef = "start", targetRef = "end", name = 42, isDefault = "false" },
                        new { flowId = "malformed-flow", sourceRef = "start" },
                        7
                    }
                })),
            activityType: "Elsa.Activities.Bpmn.Activities.BpmnProcess"));

        var detail = await _inspector.GetAsync("artifact-1");

        Assert.Empty(detail!.RootActivity.ChildSlots);
        var bpmn = Assert.IsType<Models.WorkflowExecutableBpmnStructureView>(detail.RootActivity.BpmnStructure);
        var bpmnJson = JsonSerializer.SerializeToElement(bpmn, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var elements = bpmnJson.GetProperty("elements").EnumerateArray().ToArray();
        Assert.Equal(["start", "task", "end"], elements
            .Select(element => element.GetProperty("elementId").GetString()));
        Assert.False(elements[1].TryGetProperty("childNodeId", out _));
        Assert.False(elements[1].TryGetProperty("name", out _));
        var flows = bpmnJson.GetProperty("sequenceFlows").EnumerateArray().ToArray();
        Assert.Equal(["start-to-end"], flows
            .Select(flow => flow.GetProperty("flowId").GetString()));
        Assert.False(flows[0].TryGetProperty("name", out _));
        Assert.False(flows[0].TryGetProperty("isDefault", out _));
    }

    [Fact]
    public async Task Detail_omits_bpmn_projection_for_unsupported_schema()
    {
        await _executableStore.SaveAsync(Executable(
            _now,
            structure: new ExecutableActivityStructure(
                "elsa.bpmn.structure",
                "2.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    elements = new[] { new { elementId = "task", elementType = "task" } },
                    sequenceFlows = Array.Empty<object>()
                })),
            activityType: "Elsa.Activities.Bpmn.Activities.BpmnProcess"));

        var detail = await _inspector.GetAsync("artifact-1");

        Assert.Null(detail!.RootActivity.BpmnStructure);
    }

    [Fact]
    public async Task Detail_exposes_the_versioned_declared_input_contract_in_canonical_order()
    {
        var contract = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [
                new WorkflowDeclaredInput("zeta", new TypeReference("String", CollectionKind.List), false),
                new WorkflowDeclaredInput(
                    "alpha",
                    new TypeReference("Int32"),
                    true,
                    JsonSerializer.SerializeToElement(42))
            ]);
        await _executableStore.SaveAsync(Executable(_now, inputContract: contract));

        var detail = await _inspector.GetAsync("artifact-1");

        Assert.NotNull(detail!.InputContract);
        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, detail.InputContract.Version);
        Assert.Equal(["alpha", "zeta"], detail.InputContract.Inputs.Select(input => input.Name));
        var alpha = detail.InputContract.Inputs.First();
        Assert.Equal(new TypeReference("Int32"), alpha.Type);
        Assert.True(alpha.IsRequired);
        Assert.Equal(42, alpha.DefaultValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Detail_exposes_canonical_direct_dependencies_without_source_facts()
    {
        await _executableStore.SaveAsync(Executable(
            _now,
            dependencies:
            [
                new WorkflowExecutableDependency("child-b", "sha256:b", ["root"]),
                new WorkflowExecutableDependency("child-a", "sha256:a", ["root"])
            ]));

        var detail = await _inspector.GetAsync("artifact-1");

        Assert.Equal(["child-a", "child-b"], detail!.Dependencies.Select(dependency => dependency.ArtifactId));
        Assert.DoesNotContain(
            detail.Dependencies.SelectMany(dependency => dependency.GetType().GetProperties()),
            property => property.Name.Contains("Source", StringComparison.Ordinal) ||
                        property.Name.Contains("Publication", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_api_registers_the_inspector()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableInspector));
    }

    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly ValueProtectionPolicy SensitivePolicy = new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);

    private static readonly IReadOnlyDictionary<string, Func<ExecutableNode>> AuthoredRedactionRoots = new Dictionary<string, Func<ExecutableNode>>
    {
        ["declared on the compiled binding"] = () => Node("root", "root", new Dictionary<string, RuntimeInputBinding>
        {
            ["token"] = Literal("token", "compiled", SensitivePolicy)
        }),
        ["declared on the pinned contract only"] = () => Node(
            "root",
            "root",
            new Dictionary<string, RuntimeInputBinding>(),
            new ActivityInputContract("token", "Token", StringType, false, true, false, null, ActivityValuePolicy.Default with { IsSensitive = true })),
        ["no compiled input"] = () => Node("other", "other", new Dictionary<string, RuntimeInputBinding>())
    };

    private static RuntimeInputBinding Literal(string inputKey, string value, ValueProtectionPolicy policy) =>
        new(inputKey, StringType, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), policy));

    private static ExecutableNode Node(
        string executableNodeId,
        string authoredActivityId,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        ActivityInputContract? input = null)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { });
        return new ExecutableNode(
            executableNodeId,
            authoredActivityId,
            "Test.Root",
            "1.0.0",
            new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, descriptor),
            inputBindings,
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: input is null
                ? null
                : new ActivityContract(
                    "Test.Root",
                    "1.0.0",
                    "test",
                    descriptor,
                    [input],
                    new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
                    ["Done"],
                    new ActivityActivationRequirement("test", "Test.Root")));
    }

    private static WorkflowExecutable Executable(DateTimeOffset now, ExecutableNode root) =>
        new(
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            now,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutable Executable(
        DateTimeOffset now,
        JsonElement? descriptor = null,
        ExecutableActivityStructure? structure = null,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        string activityType = "Test.Root",
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null,
        WorkflowExecutableInputContract? inputContract = null,
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null) =>
        new(
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new ExecutableNode(
                "root", "root", activityType, "1.0.0",
                new RuntimeActivityDescriptor(
                    "Test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    descriptor ?? JsonSerializer.SerializeToElement(new { })),
                inputBindings ?? new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>(),
                childSlots,
                structure: structure),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            now,
            new Dictionary<string, string>(),
            inputContract,
            dependencies,
            IncidentStrategyBuiltIns.FaultReference);

    private Models.ExecutableSourceReferenceView CreateVersionOneView(string? sourceType, string? sourceKind)
    {
#pragma warning disable CS0618 // The obsolete v1 constructor is the compatibility contract under test.
        return new Models.ExecutableSourceReferenceView(
            SourceReferenceId: "reference-v1",
            ArtifactId: "artifact-v1",
            Scope: "Published",
            SourceType: sourceType,
            SourceKind: sourceKind,
            SourceId: "definition-version-v1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-v1",
            DefinitionVersionId: "definition-version-v1",
            ArtifactVersion: "1.0.0",
            PublicationId: null,
            SlotId: null,
            CreatedAt: _now,
            PublishedAt: _now,
            ExpiresAt: null,
            DeletedAt: null,
            DeletedReason: null,
            Live: true);
#pragma warning restore CS0618
    }

    private Task<WorkflowExecutable> ActivateAsync(params WorkflowExecutableSourceReference[] references) =>
        ActivateAsync(null, references);

    private async Task<WorkflowExecutable> ActivateAsync(
        JsonElement? descriptor,
        params WorkflowExecutableSourceReference[] references)
    {
        var executable = Executable(_now, descriptor);
        await _executableStore.SaveAsync(executable);
        foreach (var reference in references)
            await _referenceStore.SaveAsync(reference);
        return executable;
    }

    private static WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset createdAt,
        string artifactVersion,
        DateTimeOffset? expiresAt = null) =>
        new(
            sourceReferenceId,
            "artifact-1",
            "WorkflowDefinitionVersion",
            $"version-{artifactVersion}",
            artifactVersion,
            "definition-1",
            $"version-{artifactVersion}",
            artifactVersion,
            createdAt,
            scope == WorkflowExecutableReferenceScope.Published ? createdAt : null,
            scope,
            expiresAt);

    private static Type ElementType(Type collectionType) => collectionType.GetInterfaces().Append(collectionType)
        .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));
}
