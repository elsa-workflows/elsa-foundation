using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Endpoints;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Exact task bindings through BPMN export and import (T063). Export carries a task's exact activity version and literal
/// inputs only when the visible catalog declares every bound input a portable literal, and reports every binding it
/// leaves out. Import proposes what the document retained and binds it only for elements the author confirms; the
/// importing host's catalog then decides whether the version and inputs are available.
/// </summary>
public sealed class BpmnTaskBindingInterchangeTests
{
    private const string VersionId = "acme.summarize@1.0.0";
    private const string Brief = "moss gardens";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_portable_binding_travels_is_proposed_and_binds_only_when_the_author_confirms_it()
    {
        var catalog = new Catalog(Declared("brief"));

        var exported = await new BpmnDocumentExporter(catalog).ExportAsync(Process(Literal("brief", Brief)));

        Assert.Empty(exported.Omissions);
        Assert.Contains("elsa:taskBinding", exported.Xml, StringComparison.Ordinal);
        Assert.Contains(VersionId, exported.Xml, StringComparison.Ordinal);
        var importer = new BpmnDocumentImporter(new BpmnWorkBinder(), catalog);

        var proposed = await importer.ImportAsync(exported.Xml);

        Assert.False(proposed.Analysis.CanExecute);
        var proposal = Assert.Single(proposed.Analysis.Capabilities, x => x.ElementId == "summarize");
        Assert.Equal(BpmnImportCapabilityAvailability.RequiresBinding, proposal.Availability);
        Assert.Equal(VersionId, proposal.RetainedBinding!.ActivityVersionId);
        Assert.Equal(["brief"], proposal.RetainedBinding.InputKeys);
        Assert.Empty(Structure(proposed.ProcessNode).Activities);

        var confirmed = await importer.ImportAsync(exported.Xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.True(confirmed.Analysis.CanExecute);
        var structure = Structure(confirmed.ProcessNode);
        var child = Assert.Single(structure.Activities);
        Assert.Equal(child.NodeId, Assert.Single(structure.Elements, x => x.ElementId == "summarize").ChildNodeId);
        Assert.Equal(VersionId, child.ActivityVersionId);
        var input = Assert.Single(child.Inputs);
        Assert.Equal("brief", input.ReferenceKey);
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Equal(Brief, ((JsonElement)input.Value.Value!).GetString());
        Assert.Equal(BpmnImportCapabilityAvailability.Available, Assert.Single(confirmed.Analysis.Capabilities, x => x.ElementId == "summarize").Availability);
    }

    [Theory]
    [InlineData("unresolvable-version")]
    [InlineData("unresolvable-input")]
    [InlineData("sensitive-declaration")]
    [InlineData("credential-declaration")]
    [InlineData("connection-declaration")]
    [InlineData("password-hint")]
    [InlineData("sensitive-binding")]
    [InlineData("non-literal")]
    [InlineData("storage-setting")]
    [InlineData("output-capture")]
    [InlineData("structured-child")]
    public async Task A_binding_that_cannot_travel_is_omitted_and_reported_for_its_element(string reason)
    {
        var declaration = reason switch
        {
            "sensitive-declaration" => Declared("brief") with { IsSensitive = true },
            "credential-declaration" => Declared("brief") with { IsCredential = true },
            "connection-declaration" => Declared("brief") with { ConnectionType = "acme.connection" },
            "password-hint" => Declared("brief") with { UiHint = "password" },
            _ => Declared(reason == "unresolvable-input" ? "other" : "brief")
        };
        var input = reason switch
        {
            "sensitive-binding" => Literal("brief", Brief) with { IsSensitive = true },
            "non-literal" => new ArgumentState("brief", new ArgumentValue("briefVariable", "Variable"), null, null, null, null),
            "storage-setting" => Literal("brief", Brief) with { StorageDriverType = "acme.blob" },
            _ => Literal("brief", Brief)
        };
        IReadOnlyList<ArgumentState> outputs = reason == "output-capture"
            ? [new ArgumentState("Result", new ArgumentValue("summary", "Variable"), null, null, null, null)]
            : [];
        var structure = reason == "structured-child"
            ? new ActivityNodeStructure("acme.flow", "1", JsonSerializer.SerializeToElement(new { }))
            : null;
        var catalog = new Catalog(declaration, versionVisible: reason != "unresolvable-version");

        var exported = await new BpmnDocumentExporter(catalog).ExportAsync(Process(input, outputs, structure));

        var omission = Assert.Single(exported.Omissions);
        Assert.Equal("summarize", omission.ElementId);
        Assert.Equal(reason switch
        {
            "sensitive-declaration" or "credential-declaration" or "connection-declaration" or "password-hint" or "sensitive-binding" => "sensitive-input",
            "non-literal" => "non-literal-input",
            "storage-setting" => "unsupported-input-setting",
            "output-capture" => "unsupported-output",
            _ => reason
        }, omission.Code);
        Assert.DoesNotContain("elsa:taskBinding", exported.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain(VersionId, exported.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain(Brief, exported.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_catalog_no_binding_travels_and_each_is_reported()
    {
        var node = Process(Literal("brief", Brief));

        var exported = await new BpmnDocumentExporter().ExportAsync(node);
        var synchronous = new BpmnDocumentExporter(new Catalog(Declared("brief"))).Export(node);

        Assert.Equal("catalog-unavailable", Assert.Single(exported.Omissions).Code);
        Assert.DoesNotContain(VersionId, exported.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain(VersionId, synchronous, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("undeclared")]
    [InlineData("credential")]
    [InlineData("missing-version")]
    public async Task A_confirmed_binding_the_importing_host_cannot_honor_is_refused(string host)
    {
        var xml = (await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(Process(Literal("brief", Brief)))).Xml;
        var importingCatalog = host switch
        {
            "undeclared" => new Catalog(Declared("other")),
            "credential" => new Catalog(Declared("brief") with { IsCredential = true }),
            _ => new Catalog(Declared("brief"), versionVisible: false)
        };

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), importingCatalog)
            .ImportAsync(xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.Contains(result.Analysis.Capabilities, x => x.Availability == BpmnImportCapabilityAvailability.Unsupported);
    }

    [Fact]
    public async Task A_confirmed_binding_without_a_verified_catalog_is_not_executable()
    {
        var xml = (await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(Process(Literal("brief", Brief)))).Xml;

        var result = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.Equal(BpmnImportCapabilityAvailability.Unknown, Assert.Single(result.Analysis.Capabilities, x => x.ElementId == "summarize").Availability);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"activityVersionId\":\"x\",\"inputs\":[]}")]
    [InlineData("not json")]
    public async Task An_unreadable_retained_binding_is_reported_and_never_proposed(string value)
    {
        var xml = (await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(Process(Literal("brief", Brief)))).Xml;
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var attribute = document.Descendants().SelectMany(x => x.Attributes()).Single(x => x.Name.LocalName == "taskBinding");
        attribute.Value = value;

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief")))
            .ImportAsync(document.ToString(), new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.Null(Assert.Single(result.Analysis.Capabilities, x => x.ElementId == "summarize").RetainedBinding);
        Assert.Contains(result.Analysis.Issues, x => x.ElementId == "summarize" && x.Severity == BpmnImportIssueSeverity.Degraded);
        Assert.Empty(Structure(result.ProcessNode).Activities);
    }

    [Fact]
    public async Task Confirming_an_element_without_a_retained_binding_is_reported_and_not_executable()
    {
        var xml = new BpmnDocumentExporter().Export(Process(Literal("brief", Brief)));

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief")))
            .ImportAsync(xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.Contains(result.Analysis.Issues, x => x.ElementId == "summarize" && x.Severity == BpmnImportIssueSeverity.Degraded);
    }

    [Fact]
    public async Task The_endpoints_report_omissions_and_honor_confirmations()
    {
        var catalog = new Catalog(Declared("brief"));
        var exporter = new BpmnDocumentExporter(catalog);
        var importer = new BpmnDocumentImporter(new BpmnWorkBinder(), catalog);

        var exported = await new Endpoints.Documents.Export.Endpoint(exporter)
            .HandleAsync(new ExportBpmnDocumentRequest(Process(Literal("brief", Brief) with { IsSensitive = true })), CancellationToken.None);
        var carried = await new Endpoints.Documents.Export.Endpoint(exporter)
            .HandleAsync(new ExportBpmnDocumentRequest(Process(Literal("brief", Brief))), CancellationToken.None);
        var analysis = await new Endpoints.Documents.Analyze.Endpoint(importer)
            .HandleAsync(new AnalyzeBpmnDocumentRequest(carried.Xml, ConfirmedBindings: ["summarize"]), CancellationToken.None);
        var imported = await new Endpoints.Documents.Import.Endpoint(importer)
            .HandleAsync(new ImportBpmnDocumentRequest(carried.Xml, ConfirmedBindings: ["summarize"]), CancellationToken.None);

        Assert.Equal("sensitive-input", Assert.Single(exported.Omissions!).Code);
        Assert.Empty(carried.Omissions!);
        Assert.True(analysis.CanExecute);
        Assert.True(imported.Analysis.CanExecute);
    }

    private static InputDefinition Declared(string referenceKey) =>
        new(referenceKey, referenceKey, new TypeReference("String"), "Json", referenceKey, null, IsNullable: true);

    private static ArgumentState Literal(string key, string value) =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);

    /// <summary>start → summarize (a service task bound to <see cref="VersionId"/>) → end.</summary>
    private static ActivityNode Process(ArgumentState input, IReadOnlyList<ArgumentState>? outputs = null, ActivityNodeStructure? childStructure = null)
    {
        var child = new ActivityNode("child-summarize", VersionId, [input], outputs ?? [], childStructure);
        var structure = new BpmnAuthoredStructure(
            [child],
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("summarize", BpmnElementTypes.ServiceTask, "Summarize", child.NodeId),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            [new BpmnSequenceFlow("to-summarize", "start", "summarize"), new BpmnSequenceFlow("to-end", "summarize", "end")]);
        return new ActivityNode("process", BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId, [], [],
            new ActivityNodeStructure(Activities.BpmnProcess.StructureKind, Activities.BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));
    }

    private static BpmnAuthoredStructure Structure(ActivityNode node) => node.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;

    /// <summary>A visible catalog with the BPMN process and, when visible, <see cref="VersionId"/> declaring one input.</summary>
    private sealed class Catalog(InputDefinition declaration, bool versionVisible = true) : IActivityDefinitionLookup
    {
        private static readonly Definition ProcessDefinition = new("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess");
        private static readonly Definition SummarizeDefinition = new("catalog-summarize", "Acme.Summarize");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>(versionVisible ? [ProcessDefinition, SummarizeDefinition] : [ProcessDefinition]);

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Exact bindings never consult version ordering.");

        public Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionSummary?>(new(definitionId + "-v1", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action));

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(versionId == VersionId ? new CatalogVersion(versionId, SummarizeDefinition, [declaration]) : null);
    }

    private sealed record Definition(string Id, string ActivityTypeKey) : IActivityDefinition
    {
        public string Category => "Test";
        public string? DisplayName => ActivityTypeKey;
        public string? Description => null;
    }

    private sealed record CatalogVersion(string Id, IActivityDefinition Definition, IEnumerable<InputDefinition> Inputs) : IActivityDefinitionVersion
    {
        public string Version => "1.0.0";
        public string DefinitionId => Definition.Id;
        public string ProviderKey => "test";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test";
        public string ConsumerSchemaVersion => "1";
        public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
        public string SourceKind => "Test";
        public string SourceId => Id;
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;
    }
}
