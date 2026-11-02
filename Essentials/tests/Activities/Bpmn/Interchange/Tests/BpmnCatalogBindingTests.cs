using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Endpoints;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>Catalog-port controls; actual persisted publication/execution is a separate integration witness.</summary>
public sealed class BpmnCatalogBindingTests
{
    [Fact]
    public async Task Registered_binder_can_return_nested_Studio_intrinsic_strings()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityDefinitionLookup>(_ => new Catalog("available"));
        services.AddScoped<IBpmnWorkBinder, IntrinsicBinder>();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>().ImportAsync(Document);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var outer = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(options)!;
        var nested = Assert.Single(outer.Activities).Structure!.Payload.Deserialize<BpmnAuthoredStructure>(options)!;
        var child = Assert.Single(nested.Activities);
        Assert.Equal(AuthoredWorkflowIntrinsicKind.Set, child.Intrinsic!.Kind);
        Assert.Equal(AuthoredValueConversionMode.Json, Assert.Single(child.Inputs).Conversion!.Mode);
    }

    [Fact]
    public async Task Malformed_registered_binder_structure_raises_contextual_domain_exception()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityDefinitionLookup>(_ => new Catalog("available"));
        services.AddScoped<IBpmnWorkBinder, MalformedBinder>();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>();
        var failure = await Assert.ThrowsAsync<BpmnInterchangeException>(() => importer.ImportAsync(Document));
        Assert.Contains("node-wait", failure.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    private const string Document = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:test">
          <process id="p" isExecutable="true"><startEvent id="start"/>
            <intermediateCatchEvent id="wait"><timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition></intermediateCatchEvent>
            <endEvent id="end"/><sequenceFlow id="first" sourceRef="start" targetRef="wait"/><sequenceFlow id="last" sourceRef="wait" targetRef="end"/>
          </process>
        </definitions>
        """;

    [Theory]
    [InlineData("available")]
    [InlineData("missing-delay")]
    [InlineData("newer-version-only")]
    [InlineData("build-metadata")]
    [InlineData("prerelease")]
    [InlineData("no-recommendation")]
    public async Task Registered_endpoint_resolves_exact_profile_references_from_visible_catalog(string inventory)
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityDefinitionLookup>(_ => new Catalog(inventory));
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>();
        var endpoint = new Endpoints.Documents.Import.Endpoint(importer);
        var result = await endpoint.HandleAsync(new ImportBpmnDocumentRequest(Document), CancellationToken.None);
        var analysis = await new Endpoints.Documents.Analyze.Endpoint(importer).HandleAsync(new AnalyzeBpmnDocumentRequest(Document), CancellationToken.None);
        Assert.Equal(result.Analysis.CanExecute, analysis.CanExecute);
        Assert.True(result.Analysis.HostInventoryVerified);
        Assert.Equal("catalog-process-v1", result.ProcessNode.ActivityVersionId);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var wait = Assert.Single(structure.Activities);
        if (inventory is "available" or "build-metadata")
        {
            Assert.Equal("catalog-delay-v1", wait.ActivityVersionId);
            Assert.True(result.Analysis.CanExecute);
            Assert.All(result.Analysis.Capabilities, capability => Assert.Equal(BpmnImportCapabilityAvailability.Available, capability.Availability));
        }
        else
        {
            Assert.Equal("Elsa.Delay", wait.ActivityVersionId); // Retain authoring rather than invent a catalog identity.
            Assert.False(result.Analysis.CanExecute);
            Assert.Contains(result.Analysis.Capabilities, capability => capability.Availability == BpmnImportCapabilityAvailability.Unsupported);
        }
    }

    private sealed record Definition(string Id, string ActivityTypeKey) : IActivityDefinition
    {
        public string Category => "Test";
        public string? DisplayName => ActivityTypeKey;
        public string? Description => null;
    }

    private sealed class MalformedBinder : IBpmnWorkBinder
    {
        public BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request) => new(new ActivityNode(request.BindingRef, "Elsa.BpmnProcess", [], [],
            new ActivityNodeStructure(Activities.BpmnProcess.StructureKind, Activities.BpmnProcess.StructureSchemaVersion,
                JsonDocument.Parse("{\"activities\":42}").RootElement.Clone())), BpmnImportCapabilityAvailability.Available, "Malformed provider result");
    }

    private sealed class IntrinsicBinder : IBpmnWorkBinder
    {
        public BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request)
        {
            using var document = JsonDocument.Parse("""
                { "activities": [{
                  "nodeId": "set-variable", "activityVersionId": "elsa.intrinsic.set@1", "outputs": [],
                  "inputs": [{ "referenceKey": "value", "value": { "value": 42, "expressionType": "Literal" }, "conversion": { "mode": "json" } }],
                  "intrinsic": { "kind": "set", "valueType": { "alias": "Int32" }, "variable": { "referenceKey": "counter" } }
                }] }
                """);
            return new(new ActivityNode(request.BindingRef, "Elsa.BpmnProcess", [], [],
                new(Activities.BpmnProcess.StructureKind, Activities.BpmnProcess.StructureSchemaVersion, document.RootElement.Clone())),
                BpmnImportCapabilityAvailability.Available, "Nested authored process");
        }
    }

    private sealed class Catalog(string inventory) : IActivityDefinitionLookup
    {
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IActivityDefinition[] definitions = inventory == "missing-delay"
                ? [new Definition("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess")]
                : [new Definition("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess"),
                    new Definition("catalog-delay", "Elsa.Activities.Scheduling.Activities.Delay")];
            return Task.FromResult<IEnumerable<IActivityDefinition>>(definitions);
        }
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("New bindings must follow the recommendation, never historical version ordering.");
        }
        public Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default)
        {
            if (definitionId == "catalog-delay" && inventory == "no-recommendation")
                return Task.FromResult<ActivityDefinitionVersionSummary?>(null);
            var version = definitionId != "catalog-delay" ? "1.0.0+current-source" : inventory switch
            {
                "newer-version-only" => "2.0.0",
                "prerelease" => "1.0.0-preview.1",
                "build-metadata" => "1.0.0+current-source",
                _ => "1.0.0"
            };
            return Task.FromResult<ActivityDefinitionVersionSummary?>(new(definitionId + "-v1", version, DateTimeOffset.UnixEpoch, ActivityExecutionType.Action));
        }
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            versionId == "elsa.intrinsic.set@1"
                ? Task.FromResult<IActivityDefinitionVersion?>(null)
                : throw new NotSupportedException();
    }
}
