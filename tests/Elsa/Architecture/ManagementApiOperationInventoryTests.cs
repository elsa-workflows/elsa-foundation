using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

public sealed partial class ManagementApiOperationInventoryTests
{
    private static readonly FormerFacadeOperation[] Inventory =
    [
        Canonical("GET", "/capabilities", "API Capabilities", "getApiCapabilities"),
        Canonical("GET", "/definitions", "Workflow Design", "listWorkflowDefinitions"),
        Canonical("GET", "/definitions/{definitionId}", "Workflow Design", "getWorkflowDefinition"),
        Canonical("POST", "/definitions", "Workflow Design", "createWorkflowDefinition"),
        Canonical("PATCH", "/definitions/{definitionId}", "Workflow Design", "updateWorkflowDefinitionMetadata"),
        Canonical("DELETE", "/definitions/{definitionId}", "Workflow Design", "softDeleteWorkflowDefinition"),
        Canonical("POST", "/definitions/{definitionId}/restore", "Workflow Design", "restoreWorkflowDefinition"),
        Canonical("DELETE", "/definitions/{definitionId}/permanent", "Workflow Design", "permanentlyDeleteWorkflowDefinition"),
        Canonical("PUT", "/drafts/{draftId}", "Workflow Design", "replaceWorkflowDraft"),
        Canonical("POST", "/drafts/{draftId}/promote", "Workflow Design", "promoteWorkflowDraft"),
        Canonical("DELETE", "/drafts/{draftId}", "Workflow Design", "discardWorkflowDraft"),
        Canonical("GET", "/versions/{versionId}", "Workflow Design", "getWorkflowDefinitionVersion"),
        Canonical("POST", "/versions/{versionId}/publish", "Publishing", "publishWorkflowVersion"),
        Canonical("GET", "/executables", "Runtime", "listWorkflowExecutables"),
        Canonical("GET", "/executables/{artifactId}", "Runtime", "getWorkflowExecutable"),
        Removed("DELETE", "/executables/{artifactId}", "Artifact-level retirement is replaced by explicit Publishing slot lifecycle operations."),
        Removed("POST", "/executables/{artifactId}/restore", "Artifact-level restoration is replaced by restoring an explicit Publishing slot."),
        Removed("DELETE", "/executables/{artifactId}/permanent", "Management clients cannot physically delete immutable Runtime artifacts; retention-driven collection owns removal."),
        Canonical("POST", "/executables/{artifactId}/run", "Runtime", "executeWorkflowExecutable"),
        Canonical("GET", "/activities", "Activity Design", "getActivityAuthoringCatalog"),
        Canonical("GET", "/activities/availability/settings", "Activity Design", "getActivityAvailabilitySettings"),
        Canonical("PUT", "/activities/availability/settings", "Activity Design", "saveActivityAvailabilitySettings"),
        Canonical("GET", "/activities/availability/diagnostics", "Activity Design", "listActivityAvailabilityDiagnostics"),
        Canonical("GET", "/descriptors/activities", "Activity Design", "getActivityAuthoringCatalog"),
        Canonical("POST", "/descriptors/activities/{activityVersionId}/inputs/{inputName}/options", "Workflow Design", "resolveActivityInputOptions"),
        Canonical("GET", "/descriptors/expression-descriptors", "Expressions", "listExpressionDescriptors"),
        Canonical("GET", "/descriptors/variables", "Expressions", "listVariableTypeDescriptors"),
        Canonical("POST", "/design/scoped-variables/analyze", "Workflow Design", "analyzeScopedVariables")
    ];

    [Fact]
    public void Frozen_inventory_matches_every_operation_in_the_former_facade_source_when_present()
    {
        var facadeFileName = string.Concat("ElsaWorkflow", "ManagementApi.cs");
        var facadePath = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", facadeFileName);
        if (!File.Exists(facadePath))
            return;

        var sourceOperations = FacadeRouteRegistration()
            .Matches(File.ReadAllText(facadePath))
            .Select(match => $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(sourceOperations, Inventory.Select(x => x.LegacyKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_former_facade_operation_has_exactly_one_canonical_owner_or_removal_rationale()
    {
        Assert.Equal(Inventory.Length, Inventory.Select(x => x.LegacyKey).Distinct(StringComparer.Ordinal).Count());

        var contractOperationIds = LoadCanonicalOperationIds();
        foreach (var operation in Inventory)
        {
            var hasOwner = operation.Owner is not null && operation.CanonicalOperationId is not null;
            var hasRemovalRationale = operation.RemovalRationale is not null;
            Assert.True(hasOwner ^ hasRemovalRationale, $"{operation.LegacyKey} must have one owner or one removal rationale.");

            if (hasOwner)
                Assert.Contains(operation.CanonicalOperationId!, contractOperationIds);
            else
                Assert.False(string.IsNullOrWhiteSpace(operation.RemovalRationale), $"{operation.LegacyKey} has an empty removal rationale.");
        }
    }

    private static HashSet<string> LoadCanonicalOperationIds()
    {
        var contractPath = Path.Combine(RepoRoot, "specs", "092-domain-owned-apis", "contracts", "management-api.openapi.yaml");
        using var reader = File.OpenText(contractPath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var paths = Assert.IsType<YamlMappingNode>(root.Children[new YamlScalarNode("paths")]);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths.Children.Values.Cast<YamlMappingNode>())
        foreach (var operation in path.Children.Values.OfType<YamlMappingNode>())
        {
            if (operation.Children.TryGetValue(new YamlScalarNode("operationId"), out var operationId))
                Assert.True(operationIds.Add(Assert.IsType<YamlScalarNode>(operationId).Value!));
        }

        return operationIds;
    }

    private static FormerFacadeOperation Canonical(string verb, string path, string owner, string operationId) =>
        new(verb, path, owner, operationId, null);

    private static FormerFacadeOperation Removed(string verb, string path, string rationale) =>
        new(verb, path, null, null, rationale);

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    [GeneratedRegex("group\\.Map(Get|Post|Put|Patch|Delete)\\(\"([^\"]+)\"")]
    private static partial Regex FacadeRouteRegistration();

    private sealed record FormerFacadeOperation(
        string Verb,
        string Path,
        string? Owner,
        string? CanonicalOperationId,
        string? RemovalRationale)
    {
        public string LegacyKey => $"{Verb} {Path}";
    }
}
