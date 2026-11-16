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
    public async Task A_task_binding_the_same_input_twice_is_omitted()
    {
        var node = Process(Literal("brief", Brief));
        var structure = Structure(node);
        var child = Assert.Single(structure.Activities);
        var doubled = new BpmnAuthoredStructure([child with { Inputs = [Literal("brief", Brief), Literal("brief", "again")] }], structure.Elements, structure.SequenceFlows);

        var exported = await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(node with
        {
            Structure = new ActivityNodeStructure(node.Structure!.Kind, node.Structure.SchemaVersion, JsonSerializer.SerializeToElement(doubled, Web))
        });

        var omission = Assert.Single(exported.Omissions);
        Assert.Equal("duplicate-input", omission.Code);
        Assert.Equal("brief", omission.ReferenceKey);
    }

    [Fact]
    public async Task A_binding_import_could_not_read_is_omitted_rather_than_written()
    {
        var exported = await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(Process(Literal("brief", new string('x', 20_000))));

        Assert.Equal("oversized-binding", Assert.Single(exported.Omissions).Code);
        Assert.DoesNotContain("elsa:taskBinding", exported.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_whose_element_id_another_scope_shares_carries_no_binding()
    {
        // The outer bound task and an unbound end event inside the subprocess both use the element id "summarize".
        var inner = new BpmnAuthoredStructure([], [new BpmnElement("inner-start", BpmnElementTypes.StartEvent), new BpmnElement("summarize", BpmnElementTypes.EndEvent)],
            [new BpmnSequenceFlow("inner-flow", "inner-start", "summarize")]);
        var nested = new ActivityNode("child-sub", BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId, [], [],
            new ActivityNodeStructure(Activities.BpmnProcess.StructureKind, Activities.BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(inner, Web)));
        var outer = Structure(Process(Literal("brief", "other value")));
        var structure = new BpmnAuthoredStructure(
            outer.Activities.Append(nested).ToArray(),
            outer.Elements.Append(new BpmnElement("sub", BpmnElementTypes.SubProcess, "Sub", nested.NodeId)).ToArray(),
            outer.SequenceFlows);
        var node = new ActivityNode("process", BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId, [], [],
            new ActivityNodeStructure(Activities.BpmnProcess.StructureKind, Activities.BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));

        var exported = await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(node);

        Assert.Equal("ambiguous-element", Assert.Single(exported.Omissions).Code);
        Assert.DoesNotContain("elsa:taskBinding", exported.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_binding_under_both_namespace_aliases_is_refused()
    {
        var xml = await RetainedXml("{\"activityVersionId\":\"acme.summarize@1.0.0\",\"inputs\":{}}");
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var task = document.Descendants().Single(x => x.Attribute("id")?.Value == "summarize");
        task.SetAttributeValue(System.Xml.Linq.XName.Get("taskBinding", global::Bpmn.Interchange.BpmnXmlNames.VendorNamespaceName),
            "{\"activityVersionId\":\"acme.other@1.0.0\"}");

        await Assert.ThrowsAsync<Exceptions.BpmnInterchangeException>(() =>
            new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief"))).ImportAsync(document.ToString()));
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

    [Fact]
    public async Task A_confirmed_binding_naming_a_binding_profile_placeholder_is_refused()
    {
        // The placeholder resolves to a real Delay version through the binding profile, so only the retained-binding check
        // stands between an undeclared carried input and execution.
        var xml = await RetainedXml("{\"activityVersionId\":\"Elsa.Delay\",\"inputs\":{\"brief\":1}}");

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief"), withDelay: true))
            .ImportAsync(xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        var refused = Assert.Single(result.Analysis.Capabilities, x => x.ElementId == "summarize");
        Assert.Equal(BpmnImportCapabilityAvailability.Unsupported, refused.Availability);
        Assert.Contains("binding profile", refused.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"activityVersionId\":\"x\",\"inputs\":[]}")]
    [InlineData("{\"activityVersionId\":\"acme.summarize@1.0.0\",\"inputs\":{\"brief\":\"a\",\"brief\":\"b\"}}")]
    [InlineData("{\"activityVersionId\":\"acme.summarize@1.0.0\",\"inputs\":{\"other\":1},\"inputs\":{\"brief\":\"ok\"}}")]
    [InlineData("not json")]
    public async Task An_unreadable_retained_binding_is_reported_and_never_proposed(string value)
    {
        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief")))
            .ImportAsync(await RetainedXml(value), new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.Null(Assert.Single(result.Analysis.Capabilities, x => x.ElementId == "summarize").RetainedBinding);
        Assert.Contains(result.Analysis.Issues, x => x.ElementId == "summarize" && x.Severity == BpmnImportIssueSeverity.Degraded);
        Assert.Empty(Structure(result.ProcessNode).Activities);
    }

    [Fact]
    public async Task A_retained_binding_on_an_element_without_work_is_reported()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="urn:test">
              <process id="p" isExecutable="true">
                <startEvent id="start"/>
                <exclusiveGateway id="choose" elsa:taskBinding='{"activityVersionId":"acme.summarize@1.0.0","inputs":{}}'/>
                <endEvent id="end"/>
                <sequenceFlow id="a" sourceRef="start" targetRef="choose"/><sequenceFlow id="b" sourceRef="choose" targetRef="end"/>
              </process>
            </definitions>
            """;

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief"))).ImportAsync(xml);

        Assert.False(result.Analysis.CanExecute);
        Assert.Contains(result.Analysis.Issues, x => x.ElementId == "choose" && x.Severity == BpmnImportIssueSeverity.Degraded);
    }

    [Fact]
    public async Task A_retained_binding_on_an_element_id_another_process_shares_is_never_bound()
    {
        const string binding = """{"activityVersionId":"acme.summarize@1.0.0","inputs":{"brief":"moss gardens"}}""";
        var xml = $$"""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="urn:test">
              <collaboration id="c"><participant id="pa" processRef="pa-process"/><participant id="pb" processRef="pb-process"/></collaboration>
              <process id="pa-process" isExecutable="true">
                <startEvent id="a-start"/><serviceTask id="summarize" elsa:taskBinding='{{binding}}'/><endEvent id="a-end"/>
                <sequenceFlow id="a1" sourceRef="a-start" targetRef="summarize"/><sequenceFlow id="a2" sourceRef="summarize" targetRef="a-end"/>
              </process>
              <process id="pb-process" isExecutable="true">
                <startEvent id="b-start"/><exclusiveGateway id="summarize"/><endEvent id="b-end"/>
                <sequenceFlow id="b1" sourceRef="b-start" targetRef="summarize"/><sequenceFlow id="b2" sourceRef="summarize" targetRef="b-end"/>
              </process>
            </definitions>
            """;

        var result = await new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief")))
            .ImportAsync(xml, new BpmnImportOptions { ConfirmedBindings = ["summarize"] });

        Assert.False(result.Analysis.CanExecute);
        Assert.All(result.ProcessNodes, process => Assert.Empty(Structure(process.Node).Activities));
        Assert.All(result.Analysis.Capabilities.Where(x => x.ElementId == "summarize"), x => Assert.Null(x.RetainedBinding));
        Assert.Contains(result.Analysis.Issues, x => x.ElementId == "summarize" && x.Severity == BpmnImportIssueSeverity.Degraded);
    }

    [Fact]
    public async Task Two_processes_binding_work_under_one_element_id_are_refused()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:test">
              <collaboration id="c"><participant id="pa" processRef="pa-process"/><participant id="pb" processRef="pb-process"/></collaboration>
              <process id="pa-process" isExecutable="true"><serviceTask id="work"/></process>
              <process id="pb-process" isExecutable="true"><serviceTask id="work"/></process>
            </definitions>
            """;

        await Assert.ThrowsAsync<Exceptions.BpmnInterchangeException>(() =>
            new BpmnDocumentImporter(new BpmnWorkBinder(), new Catalog(Declared("brief"))).ImportAsync(xml));
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

    /// <summary>An exported document whose task binding attribute is replaced by <paramref name="value"/>.</summary>
    private static async Task<string> RetainedXml(string value)
    {
        var xml = (await new BpmnDocumentExporter(new Catalog(Declared("brief"))).ExportAsync(Process(Literal("brief", Brief)))).Xml;
        var document = System.Xml.Linq.XDocument.Parse(xml);
        document.Descendants().SelectMany(x => x.Attributes()).Single(x => x.Name.LocalName == "taskBinding").Value = value;
        return document.ToString();
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
    private sealed class Catalog(InputDefinition declaration, bool versionVisible = true, bool withDelay = false) : IActivityDefinitionLookup
    {
        private static readonly Definition ProcessDefinition = new("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess");
        private static readonly Definition SummarizeDefinition = new("catalog-summarize", "Acme.Summarize");
        private static readonly Definition DelayDefinition = new("catalog-delay", "Elsa.Activities.Scheduling.Activities.Delay");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>(new[] { ProcessDefinition }
                .Concat(versionVisible ? [SummarizeDefinition] : [])
                .Concat(withDelay ? [DelayDefinition] : []));

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Exact bindings never consult version ordering.");

        public Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionSummary?>(new(definitionId + "-v1", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action));

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // With the delay in the inventory, even the placeholder id reads back as a version declaring the input, so only the
        // binding-profile rule can refuse a retained binding that names it.
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(versionId == VersionId ? new CatalogVersion(versionId, SummarizeDefinition, [declaration])
                : withDelay && versionId is "Elsa.Delay" or "catalog-delay-v1" ? new CatalogVersion(versionId, DelayDefinition, [declaration]) : null);
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
