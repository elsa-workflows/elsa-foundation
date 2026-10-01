using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionInspectionCaptureTests
{
    [Fact]
    public void Capture_refuses_excessive_profile_inputs_before_any_file_read()
    {
        var reads = 0;
        var reader = new CompositionFileReader(_ => reads++, _ => throw new IOException());

        var refusal = Assert.Throws<CliRefusal>(() => CompositionInspectionCapture.Open(
            "unused", "default", "Production", "unused.json", workspaceProfilePaths:
            Enumerable.Range(0, 33).Select(index => $"profile-{index}.json").ToArray(), reader: reader));

        Assert.Equal("candidate-capture-invalid", refusal.Code);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("remove")]
    public void Capture_checks_selection_count_before_building_a_candidate(string field)
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        var ids = new JsonArray(Enumerable.Range(0, 4097)
            .Select(index => (JsonNode?)JsonValue.Create($"Feature{index:D4}")).ToArray());
        if (field == "accepted")
            authored["accepted"]!["featureIds"] = ids;
        else
            authored["remove"] = ids;
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => Open(fixture, [])).Code);
    }

    [Theory]
    [InlineData(false, 128)]
    [InlineData(false, 129)]
    [InlineData(true, 128)]
    [InlineData(true, 129)]
    public void Capture_bounds_selected_and_removed_feature_identities(bool removal, int length)
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var id = new string('x', length);
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["settings"] = null;
        if (removal)
            authored["remove"]!.AsArray().Add(id);
        else
        {
            authored["add"]!.AsArray().Add(id);
            authored["accepted"]!["featureIds"]!.AsArray().Add(id);
        }
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        if (length == 128)
        {
            var capture = Open(fixture, []);
            Assert.Contains(id, removal ? capture.Payload.RemovedFeatureIds! : capture.Payload.AcceptedFeatureIds!);
        }
        else
        {
            var refusal = Assert.Throws<CliRefusal>(() => Open(fixture, []));
            Assert.Equal("candidate-capture-invalid", refusal.Code);
            Assert.DoesNotContain(id, refusal.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_refuses_case_collisions_in_removals_and_across_selection_sets(bool overlap)
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["remove"] = overlap ? new JsonArray("a") : new JsonArray("Other", "other");
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => Open(fixture, [])).Code);
    }

    [Fact]
    public void Capture_transports_three_layers_when_optional_appsettings_overlay_is_absent()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        File.Delete(Path.Join(fixture.HostDirectory, "appsettings.Production.json"));

        var capture = CompositionInspectionCapture.Open(fixture.HostDirectory, "default", "Production",
            fixture.OutputPath, fixture.CatalogPath, fixture.ReviewPath);

        Assert.Equal(3, capture.Payload.Files!.Count);
        Assert.DoesNotContain(capture.Payload.Files, file => file.Name == "appsettings.Production.json");
        capture.VerifyUnchanged();
    }

    [Fact]
    public void One_capture_owns_post_edit_candidate_layers_and_all_supplied_inputs()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 42);
        var unused = fixture.WriteWorkspaceProfile("unused.json", "unused", "1", ["TenantOnly"]);
        var before = Directory.GetFiles(fixture.HostDirectory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var reads = new Dictionary<string, int>(StringComparer.Ordinal);
        var reader = new CompositionFileReader(_ => { }, path =>
        {
            reads[path] = reads.GetValueOrDefault(path) + 1;
            return new MemoryStream(File.ReadAllBytes(path));
        });
        var capture = Open(fixture, [unused.Path], reader);
        var payload = capture.Payload;

        Assert.Equal(4, payload.Files!.Count);
        Assert.All(payload.Files, file => Assert.Equal(payload.CaptureId, file.CaptureId));
        Assert.NotEqual(payload.CaptureId, payload.InvocationId);
        Assert.DoesNotContain(payload.Files, file => file.Name == "shells.Staging.json");
        using var candidateBase = JsonDocument.Parse(Convert.FromBase64String(payload.Files.Single(f => f.Name == "shells.json").Content!));
        var settings = candidateBase.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default")
            .GetProperty("Features").GetProperty("A");
        Assert.Equal(42, settings.GetProperty("Limit").GetInt32());
        Assert.True(settings.GetProperty("Future").TryGetProperty("Canary", out _));
        Assert.All(reads.Values, count => Assert.Equal(1, count));
        Assert.Contains(unused.Path, reads.Keys);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(fixture.HostDirectory).Select(Path.GetFileName).Order());
        Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllBytes(Path.Join(fixture.HostDirectory, file.Key!))));
        capture.VerifyUnchanged();
        Assert.All(reads.Values, count => Assert.Equal(2, count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void One_capture_rechecks_unused_profile_and_sibling_source_before_a_result(bool intent)
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var unused = fixture.WriteWorkspaceProfile("unused.json", "unused", "1", ["TenantOnly"]);
        var capture = Open(fixture, [unused.Path]);
        File.AppendAllText(intent ? unused.Path : Path.Join(fixture.HostDirectory, "shells.Staging.json"), " ");

        Assert.Equal(intent ? "composition-input-changed" : "bridge-source-changed", Assert.Throws<CliRefusal>(() => capture.VerifyUnchanged()).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_refuses_changed_acceptance_or_malformed_unused_input_without_echoing_values(bool malformed)
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var unused = fixture.WriteWorkspaceProfile("unused.json", "unused", "1", ["TenantOnly"]);
        if (malformed)
            File.WriteAllText(unused.Path, "private-input-canary");
        else
        {
            var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
            authored["add"]!.AsArray().Add("C");
            File.WriteAllText(fixture.OutputPath, authored.ToJsonString());
        }

        var refusal = Assert.Throws<CliRefusal>(() => Open(fixture, [unused.Path]));

        Assert.Equal(malformed ? "invalid-field" : "bridge-selection-drift", refusal.Code);
        Assert.DoesNotContain("private-input-canary", refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_uses_the_authored_bundled_pin_when_no_catalog_or_review_file_is_supplied()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition();
        var catalog = FoundationSelectionCatalog.Load();
        var authored = JsonNode.Parse(File.ReadAllText(fixture.OutputPath))!;
        authored["catalog"] = new JsonObject { ["id"] = catalog.Id, ["version"] = catalog.Version, ["digest"] = catalog.Digest };
        authored["accepted"]!["catalogDigest"] = catalog.Digest;
        authored["settings"] = null;
        File.WriteAllText(fixture.OutputPath, authored.ToJsonString());

        var capture = Open(fixture, [], catalog: null, review: null);

        Assert.NotEmpty(capture.Payload.AcceptedFeatureIds!);
        capture.VerifyUnchanged();
    }

    [Fact]
    public void Capture_refuses_post_edit_layers_that_grow_beyond_the_transport_file_bound()
    {
        using var fixture = new CompositionBridgeFixture();
        fixture.WriteAcceptedComposition(limit: 42);
        var path = Path.Join(fixture.HostDirectory, "shells.json");
        var source = JsonNode.Parse(File.ReadAllText(path))!;
        source["Padding"] = "";
        source["Padding"] = new string('x', CompositionFileReader.MaximumFileBytes - Encoding.UTF8.GetByteCount(source.ToJsonString()) - 1);
        File.WriteAllText(path, source.ToJsonString());
        Assert.True(new FileInfo(path).Length <= CompositionFileReader.MaximumFileBytes);

        Assert.Equal("candidate-capture-invalid", Assert.Throws<CliRefusal>(() => Open(fixture, [])).Code);
    }

    private static CompositionInspectionCapture Open(CompositionBridgeFixture fixture, IReadOnlyList<string> profiles,
        CompositionFileReader? reader = null, string? catalog = "fixture", string? review = "fixture") =>
        CompositionInspectionCapture.Open(fixture.HostDirectory, "default", "Production", fixture.OutputPath,
            catalog is null ? null : fixture.CatalogPath, review is null ? null : fixture.ReviewPath, profiles, reader);
}
