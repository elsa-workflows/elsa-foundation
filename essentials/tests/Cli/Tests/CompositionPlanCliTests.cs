using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionPlanCliTests
{
    private const string SecretSentinel = "Server=db;Password=composition-sentinel";
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Plan_emits_deterministic_redacted_json_from_the_shared_selection_planner()
    {
        using var files = new PlanFiles();
        var compositionBefore = File.ReadAllBytes(files.CompositionPath);
        var first = Run(files, "json");
        var second = Run(files, "json");

        Assert.True(first.ExitCode == ToolExitCode.Success, first.Error);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(string.Empty, first.Error);
        Assert.DoesNotContain(SecretSentinel, first.Text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(first.Output);
        var root = json.RootElement;
        Assert.Equal("composition-plan", root.GetProperty("kind").GetString());
        Assert.Equal("supplied-files", root.GetProperty("evidenceScope").GetString());
        Assert.Equal(new[] { "A", "C", "D" }, Strings(root.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(new[] { "A", "B", "C" }, Strings(root.GetProperty("accepted").GetProperty("featureIds")));
        Assert.Equal("unchecked", root.GetProperty("persistence").GetProperty("status").GetString());
        Assert.Contains(root.GetProperty("reasons").EnumerateArray(), reason =>
            reason.GetProperty("featureId").GetString() == "B" && reason.GetProperty("action").GetString() == "removed");
        Assert.DoesNotContain("rationale", first.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings", first.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resources", first.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(compositionBefore, File.ReadAllBytes(files.CompositionPath));
    }

    [Fact]
    public void Plan_reports_supplied_dependency_and_resource_evidence_without_claiming_readiness()
    {
        using var files = new PlanFiles(includeEvidence: true);
        var run = Run(files, "json");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.DoesNotContain(SecretSentinel, run.Text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(run.Output);
        var root = json.RootElement;
        var dependency = Assert.Single(root.GetProperty("dependencyEvidence").EnumerateArray());
        Assert.Equal("A", dependency.GetProperty("featureId").GetString());
        Assert.Equal("B", dependency.GetProperty("dependencyId").GetString());
        Assert.Equal("required", dependency.GetProperty("mode").GetString());
        Assert.Equal("runtime-descriptor", dependency.GetProperty("evidenceKind").GetString());
        Assert.Equal("target-export", dependency.GetProperty("evidenceSource").GetString());
        Assert.False(dependency.GetProperty("targetSelected").GetBoolean());
        Assert.Equal("primary", Assert.Single(Strings(root.GetProperty("persistence").GetProperty("resourceReferences"))));
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "required-dependency-missing");
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "feature-unknown" && finding.GetProperty("featureId").GetString() == "D");
        Assert.DoesNotContain("runtimeReady", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_labels_optional_manifest_evidence_without_adding_a_feature()
    {
        using var files = new PlanFiles(includeEvidence: true);
        File.WriteAllText(files.InventoryPath!,
            "{\"schemaVersion\":\"1\",\"inventoryId\":\"snapshot-42\",\"targetId\":\"staging\",\"observedAt\":\"2026-09-25T09:00:00Z\",\"source\":\"supplied-snapshot\",\"features\":[{" +
            "\"featureId\":\"A\",\"availability\":\"installed\",\"runtimeDependencies\":null,\"manifestDependencies\":[{\"id\":\"B\",\"optional\":true}],\"manifestReadStatus\":\"read\",\"package\":null,\"hostBundled\":true,\"compatibility\":\"unknown\",\"evidenceSource\":\"installed-manifest\"}]} ");

        var run = Run(files, "json");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        using var json = JsonDocument.Parse(run.Output);
        var root = json.RootElement;
        Assert.Equal(new[] { "A", "C", "D" }, Strings(root.GetProperty("candidate").GetProperty("featureIds")));
        var dependency = Assert.Single(root.GetProperty("dependencyEvidence").EnumerateArray());
        Assert.Equal("optional", dependency.GetProperty("mode").GetString());
        Assert.Equal("package-manifest", dependency.GetProperty("evidenceKind").GetString());
        Assert.Equal("installed-manifest", dependency.GetProperty("evidenceSource").GetString());
        Assert.False(dependency.GetProperty("targetSelected").GetBoolean());
    }

    [Fact]
    public void Plan_keeps_parse_refusals_off_stdout_and_does_not_echo_untrusted_paths()
    {
        using var files = new PlanFiles();
        var invalidCatalog = Path.Join(files.Directory, "invalid.json");
        File.WriteAllText(invalidCatalog, files.CatalogJson.Replace("\"schemaVersion\":\"1\"", "\"schemaVersion\":\"2\"", StringComparison.Ordinal));

        var refused = DotnetElsa.Run("composition", "plan", "--catalog", invalidCatalog,
            "--composition", files.CompositionPath, "--format", "json");
        Assert.Equal(ToolExitCode.Refusal, refused.ExitCode);
        Assert.Equal(string.Empty, refused.Output);
        Assert.Contains("schema-unsupported", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, refused.Text, StringComparison.Ordinal);

        var missingPath = Path.Join(files.Directory, SecretSentinel);
        var unreadable = DotnetElsa.Run("composition", "plan", "--catalog", missingPath,
            "--composition", files.CompositionPath, "--format", "json");
        Assert.Equal(ToolExitCode.ResolutionFailure, unreadable.ExitCode);
        Assert.Equal(string.Empty, unreadable.Output);
        Assert.Contains("composition-input-unreadable", unreadable.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, unreadable.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_defaults_to_text_and_rejects_unknown_output_format()
    {
        using var files = new PlanFiles();
        var text = DotnetElsa.Run("composition", "plan", "--catalog", files.CatalogPath, "--composition", files.CompositionPath);
        Assert.Equal(ToolExitCode.Success, text.ExitCode);
        Assert.StartsWith("Candidate: A, C, D (3 exact IDs); accepted: A, B, C (3 IDs)", text.Output, StringComparison.Ordinal);
        Assert.Contains("Live host readiness: not assessed", text.Output, StringComparison.Ordinal);

        var invalid = DotnetElsa.Run("composition", "plan", "--catalog", files.CatalogPath,
            "--composition", files.CompositionPath, "--format", "xml");
        Assert.Equal(ToolExitCode.Refusal, invalid.ExitCode);
        Assert.Equal(string.Empty, invalid.Output);
        Assert.Contains("composition-format-invalid", invalid.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_parser_refusals_do_not_echo_untrusted_arguments()
    {
        using var files = new PlanFiles();
        var run = DotnetElsa.Run("composition", "plan", "--catalog", files.CatalogPath,
            "--composition", files.CompositionPath, "--unknown-option", SecretSentinel);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains("composition-usage", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_identity_resource_label_and_malformed_json_are_refused_without_echo()
    {
        using var unsafeIdentity = new PlanFiles(profileId: SecretSentinel);
        var identityResult = Run(unsafeIdentity, "json");
        Assert.Equal(ToolExitCode.Refusal, identityResult.ExitCode);
        Assert.Equal(string.Empty, identityResult.Output);
        Assert.DoesNotContain(SecretSentinel, identityResult.Text, StringComparison.Ordinal);

        using var unsafeResource = new PlanFiles(includeEvidence: true);
        File.WriteAllText(unsafeResource.PersistencePath!,
            "{\"schemaVersion\":\"1\",\"source\":\"developer-file\",\"resourceReferences\":[\"" + SecretSentinel + "\"]}");
        var resourceResult = Run(unsafeResource, "json");
        Assert.Equal(ToolExitCode.Refusal, resourceResult.ExitCode);
        Assert.Equal(string.Empty, resourceResult.Output);
        Assert.DoesNotContain(SecretSentinel, resourceResult.Text, StringComparison.Ordinal);

        using var malformed = new PlanFiles();
        File.WriteAllText(malformed.CatalogPath, "{\"secret\":\"" + SecretSentinel + "\",");
        var malformedResult = Run(malformed, "json");
        Assert.Equal(ToolExitCode.Refusal, malformedResult.ExitCode);
        Assert.Equal(string.Empty, malformedResult.Output);
        Assert.DoesNotContain(SecretSentinel, malformedResult.Text, StringComparison.Ordinal);
    }

    private static CliRun Run(PlanFiles files, string format)
    {
        var args = new List<string>
        {
            "composition", "plan", "--catalog", files.CatalogPath,
            "--composition", files.CompositionPath
        };
        if (files.InventoryPath is { } inventoryPath)
            args.AddRange(["--inventory", inventoryPath]);
        if (files.PersistencePath is { } persistencePath)
            args.AddRange(["--persistence-evidence", persistencePath]);
        args.AddRange(["--format", format]);
        return DotnetElsa.Run([.. args]);
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private sealed class PlanFiles : IDisposable
    {
        private readonly TempDirectory temp = new("elsa-composition-plan-");

        public PlanFiles(bool includeEvidence = false, string profileId = "starter")
        {
            Directory = temp.Path;
            var profile = Definition("profile", profileId, ["A", "B"]);
            var group = Definition("group", "runtime", ["B", "C"]);
            var catalogDraft = new SelectionCatalog("1", "foundation-fixtures", "1", "elsa-foundation", new string('0', 64), [profile], [group]);
            var catalog = catalogDraft with { Digest = SelectionDigest.ComputeCatalogDigest(catalogDraft) };
            var authored = new AuthoredComposition(
                "1",
                new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
                Reference(profile),
                [Reference(group)],
                ["D"],
                ["B"],
                new AcceptedSelection(catalog.Digest, ["A", "B", "C"], []),
                JsonDocument.Parse("{\"connection\":\"" + SecretSentinel + "\"}").RootElement.Clone(),
                JsonDocument.Parse("{\"future\":{\"secret\":\"" + SecretSentinel + "\"}}").RootElement.Clone());

            CatalogJson = JsonSerializer.Serialize(catalog, s_json);
            CatalogPath = temp.File("catalog.json");
            CompositionPath = temp.File("composition.json");
            File.WriteAllText(CatalogPath, CatalogJson);
            File.WriteAllText(CompositionPath, JsonSerializer.Serialize(authored, s_json));

            if (includeEvidence)
            {
                InventoryPath = temp.File("inventory.json");
                PersistencePath = temp.File("resource-hints.json");
                File.WriteAllText(InventoryPath,
                    "{\"schemaVersion\":\"1\",\"inventoryId\":\"snapshot-42\",\"targetId\":\"staging\",\"observedAt\":\"2026-09-25T09:00:00Z\",\"source\":\"supplied-snapshot\",\"features\":[{" +
                    "\"featureId\":\"A\",\"availability\":\"loaded\",\"runtimeDependencies\":[\"B\"],\"manifestDependencies\":null,\"manifestReadStatus\":\"absent\",\"package\":null,\"hostBundled\":true,\"compatibility\":\"unknown\",\"evidenceSource\":\"target-export\"}]} ");
                File.WriteAllText(PersistencePath,
                    "{\"schemaVersion\":\"1\",\"source\":\"developer-file\",\"resourceReferences\":[\"primary\"]}");
            }
        }

        public string Directory { get; }
        public string CatalogJson { get; }
        public string CatalogPath { get; }
        public string CompositionPath { get; }
        public string? InventoryPath { get; }
        public string? PersistencePath { get; }

        public void Dispose() => temp.Dispose();

        private static SelectionDefinition Definition(string kind, string id, ImmutableArray<string> members)
        {
            var draft = new SelectionDefinition(kind, id, "1", new string('0', 64), members,
                SecretSentinel, id, "fixture description", []);
            return draft with { Digest = SelectionDigest.ComputeDefinitionDigest(draft) };
        }

        private static DefinitionReference Reference(SelectionDefinition definition) =>
            new("foundation", definition.Kind, definition.Id, definition.Version, definition.Digest);
    }
}
