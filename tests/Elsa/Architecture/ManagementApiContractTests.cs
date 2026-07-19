using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

public class ManagementApiContractTests
{
    private static readonly string ContractPath =
        Path.Combine(AppContext.BaseDirectory, "Contracts", "management-api.openapi.yaml");

    private static readonly string[] ExpectedPaths =
    [
        "/capabilities",
        "/design/activities/availability/diagnostics",
        "/design/activities/availability/settings",
        "/design/activities/catalog",
        "/design/workflows/activities/{activityVersionId}/inputs/{inputName}/options",
        "/design/workflows/definitions",
        "/design/workflows/definitions/page",
        "/design/workflows/definitions/{definitionId}",
        "/design/workflows/definitions/{definitionId}/permanent",
        "/design/workflows/definitions/{definitionId}/restore",
        "/design/workflows/drafts/{draftId}",
        "/design/workflows/drafts/{draftId}/promote",
        "/design/workflows/scoped-variables/analyze",
        "/design/workflows/versions/{versionId}",
        "/expressions/descriptors",
        "/expressions/variable-types",
        "/publishing/workflows/drafts/test-runs",
        "/publishing/workflows/{definitionId}/policy",
        "/publishing/workflows/{definitionId}/slots",
        "/publishing/workflows/{definitionId}/slots/{slotName}",
        "/publishing/workflows/{definitionId}/slots/{slotName}/restore",
        "/publishing/workflows/{versionId}/preflight",
        "/publishing/workflows/{versionId}/publish",
        "/publishing/workflows/{versionId}/test-runs",
        "/runtime/workflows/diagnostics/settings",
        "/runtime/workflows/executables",
        "/runtime/workflows/executables/{artifactId}",
        "/runtime/workflows/executables/{artifactId}/execute",
        "/runtime/workflows/executables/{artifactId}/provenance",
        "/runtime/workflows/instances",
        "/runtime/workflows/instances/{workflowExecutionId}",
        "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}",
        "/runtime/workflows/instances/{workflowExecutionId}/incidents"
    ];

    private static readonly string[] ExpectedSchemas =
    [
        "ActivityAuthoringDescriptor",
        "ActivityAvailabilityDiagnostic",
        "ActivityAvailabilitySettings",
        "ActivityInputDescriptor",
        "ActivityInputOptionsRequest",
        "ActivityOutputDescriptor",
        "ActivityPortDescriptor",
        "ApiCapability",
        "AuthoredActivity",
        "CapabilityDocument",
        "CapabilityLink",
        "CreateWorkflowDefinitionRequest",
        "DefinitionListState",
        "DesignMetadata",
        "ExecutableProvenance",
        "ExecutableSourceReference",
        "ExpressionDescriptor",
        "Problem",
        "Publication",
        "PublicationIntent",
        "PublicationPolicy",
        "PublicationPreflight",
        "PublicationSlot",
        "PublicationStatus",
        "ReplaceWorkflowDraftRequest",
        "RuntimeDiagnosticsSettings",
        "ScopedVariableAnalysis",
        "ScopedVariableAnalysisRequest",
        "SetPublicationPolicyRequest",
        "SourceReferenceScope",
        "TestRun",
        "TriggerChange",
        "TriggerConflict",
        "UpdateWorkflowDefinitionMetadataRequest",
        "VariableTypeDescriptor",
        "WorkflowDefinitionDetails",
        "WorkflowDefinitionPage",
        "WorkflowDefinitionState",
        "WorkflowDefinitionSummary",
        "WorkflowDefinitionVersion",
        "WorkflowDefinitionVersionSummary",
        "WorkflowDraft",
        "WorkflowExecutableDetails",
        "WorkflowExecutableSummary",
        "WorkflowExecutionDispatch",
        "WorkflowInstanceDetails",
        "WorkflowInstanceSummary"
    ];

    [Fact]
    public async Task Contract_contains_the_exact_canonical_path_inventory()
    {
        var document = await LoadContractAsync();

        Assert.Equal(ExpectedPaths, document.Paths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Contract_contains_the_exact_canonical_schema_inventory()
    {
        var document = await LoadContractAsync();

        Assert.Equal(ExpectedSchemas, document.Schemas.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Contract_excludes_the_legacy_server_management_surface()
    {
        var document = await LoadContractAsync();

        Assert.DoesNotContain(
            document.Paths,
            path => path.StartsWith(string.Join('/', "", "_elsa", "workflow-management"), StringComparison.Ordinal));
    }

    private static async Task<ContractInventory> LoadContractAsync()
    {
        Assert.True(
            File.Exists(ContractPath),
            $"Expected the management API contract fixture at '{ContractPath}'.");

        await using var stream = File.OpenRead(ContractPath);
        using var reader = new StreamReader(stream);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var document = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        Assert.Equal("3.1.0", GetScalar(document, "openapi"));

        var paths = GetMapping(document, "paths").Children.Keys
            .Cast<YamlScalarNode>()
            .Select(x => Assert.IsType<string>(x.Value))
            .ToArray();
        var components = GetMapping(document, "components");
        var schemas = GetMapping(components, "schemas").Children.Keys
            .Cast<YamlScalarNode>()
            .Select(x => Assert.IsType<string>(x.Value))
            .ToArray();

        return new ContractInventory(paths, schemas);
    }

    private static YamlMappingNode GetMapping(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlMappingNode>(parent.Children[new YamlScalarNode(key)]);

    private static string? GetScalar(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlScalarNode>(parent.Children[new YamlScalarNode(key)]).Value;

    private sealed record ContractInventory(IReadOnlyCollection<string> Paths, IReadOnlyCollection<string> Schemas);
}
