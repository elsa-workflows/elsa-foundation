using System.Text;
using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Tests;

public sealed class CompositionHandoffTests
{
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";

    [Fact]
    public void Projects_the_complete_candidate_as_safe_roles_with_an_opaque_identity()
    {
        var files = Files();
        var source = SourceSnapshot.Freeze(new SourceSelection("default", "Production", "shells.Production.json", "appsettings.Production.json"), files);
        var catalog = new CatalogPin("bridge-test", "1", new string('a', 64));

        var handoff = CompositionHandoff.Create("workbench-a", source, catalog, ["A"], files, ["package-inventory-unchecked"]);
        var second = CompositionHandoff.Create("workbench-a", source, catalog, ["A"], files, []);
        var json = JsonSerializer.Serialize(handoff);

        Assert.True(Guid.TryParseExact(handoff.CandidateId, "N", out _));
        Assert.NotEqual(handoff.CandidateId, second.CandidateId);
        Assert.Equal("workbench-a", handoff.Host);
        Assert.Equal("default", handoff.Shell);
        Assert.Equal("Production", handoff.Environment);
        Assert.Equal(catalog, handoff.Catalog);
        Assert.Equal(new[] { "A" }, handoff.AcceptedFeatureIds.ToArray());
        Assert.Equal(files.Count, handoff.IncludedFiles.Length);
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "shells-base");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "shells-selected-overlay");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "appsettings-base");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "appsettings-selected-overlay");
        Assert.Equal(new[] { 1, 2 }, handoff.IncludedFiles.Where(item => item.Role == "unselected-copy").Select(item => item.Ordinal).ToArray());
        Assert.Contains("connection-target-unchecked", handoff.Unresolved);
        Assert.Equal("external-attestation-required", handoff.DeploymentIntegrity);
        Assert.Equal("unchecked", handoff.Activation);
        Assert.DoesNotContain(ConnectionCanary, json, StringComparison.Ordinal);
        Assert.DoesNotContain("shells.Staging.json", json, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings.Staging.json", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../host")]
    [InlineData("host/one")]
    [InlineData("Server=localhost")]
    public void Refuses_an_unsafe_host_alias(string alias)
    {
        var files = Files();
        var source = SourceSnapshot.Freeze(new SourceSelection("default", "Production", "shells.Production.json", "appsettings.Production.json"), files);

        var refusal = Assert.Throws<CompositionImportException>(() => CompositionHandoff.Create(
            alias, source, new CatalogPin("bridge-test", "1", new string('a', 64)), ["A"], files, []));

        Assert.Equal("candidate-incomplete", refusal.Code);
        Assert.DoesNotContain(alias, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_candidate_missing_any_copied_file()
    {
        var files = Files();
        var source = SourceSnapshot.Freeze(new SourceSelection("default", "Production", "shells.Production.json", "appsettings.Production.json"), files);
        var incomplete = files.Where(item => item.Key != "shells.Staging.json").ToDictionary();

        var refusal = Assert.Throws<CompositionImportException>(() => CompositionHandoff.Create(
            "workbench-a", source, new CatalogPin("bridge-test", "1", new string('a', 64)), ["A"], incomplete, []));

        Assert.Equal("candidate-incomplete", refusal.Code);
    }

    [Fact]
    public void Classifies_supported_case_variants_without_exporting_their_names()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["SHELLS.json"] = Encoding.UTF8.GetBytes("{}"),
            ["Shells.Production.JSON"] = Encoding.UTF8.GetBytes("{}"),
            ["APPSETTINGS.json"] = Encoding.UTF8.GetBytes("{}"),
            ["AppSettings.Staging.Json"] = Encoding.UTF8.GetBytes("{}")
        };
        var source = SourceSnapshot.Freeze(new SourceSelection("default", "Production", "shells.Production.json", null), files);

        var handoff = CompositionHandoff.Create("workbench-a", source,
            new CatalogPin("bridge-test", "1", new string('a', 64)), ["A"], files, []);

        Assert.Contains(handoff.IncludedFiles, item => item.Role == "shells-base");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "shells-selected-overlay");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "appsettings-base");
        Assert.Contains(handoff.IncludedFiles, item => item.Role == "unselected-copy");
        Assert.DoesNotContain("AppSettings.Staging.Json", JsonSerializer.Serialize(handoff), StringComparison.Ordinal);
    }

    private static Dictionary<string, byte[]> Files() => new(StringComparer.Ordinal)
    {
        ["shells.json"] = Encoding.UTF8.GetBytes("{}"),
        ["shells.Production.json"] = Encoding.UTF8.GetBytes("{}"),
        ["shells.Staging.json"] = Encoding.UTF8.GetBytes("{}"),
        ["appsettings.json"] = Encoding.UTF8.GetBytes(ConnectionCanary),
        ["appsettings.Production.json"] = Encoding.UTF8.GetBytes("{}"),
        ["appsettings.Staging.json"] = Encoding.UTF8.GetBytes("{}")
    };
}
