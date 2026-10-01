using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Catalog;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionInspectionCaptureTests
{
    [Fact]
    public void Explicit_environment_document_accepts_blank_whitespace_and_ordinary_connection_values()
    {
        var parsed = ParseEnvironment(CandidateInspectionFixture.EnvironmentDocument(
            ("ConnectionStrings__Primary", "Data Source=:memory:"),
            ("Blank", ""),
            ("Whitespace", "\n\t"),
            (" Key ", "  value  "),
            ("café", "composed"),
            ("café", "decomposed")));

        Assert.Equal("Data Source=:memory:", parsed["ConnectionStrings:Primary"]);
        Assert.Equal("", parsed["Blank"]);
        Assert.Equal("\n\t", parsed["Whitespace"]);
        Assert.Equal("  value  ", parsed[" Key "]);
        Assert.Equal("composed", parsed["café"]);
        Assert.Equal("decomposed", parsed["café"]);
        Assert.Equal(6, parsed.Count);

        var mutable = Assert.IsAssignableFrom<IDictionary<string, string>>(parsed);
        Assert.Throws<NotSupportedException>(() => mutable["Blank"] = "changed");
    }

    [Fact]
    public void Explicit_environment_document_accepts_one_leading_bom_but_not_a_second_one()
    {
        var document = CandidateInspectionFixture.EnvironmentDocument(("Key", "value"));
        var withBom = new byte[document.Length + 3];
        "\uFEFF"u8.CopyTo(withBom);
        document.CopyTo(withBom, 3);

        Assert.Equal("value", ParseEnvironment(withBom)["Key"]);

        var withTwoBoms = new byte[withBom.Length + 3];
        "\uFEFF"u8.CopyTo(withTwoBoms);
        withBom.CopyTo(withTwoBoms, 3);
        AssertInvalid(withTwoBoms);
    }

    [Fact]
    public void Explicit_environment_document_refuses_invalid_utf8_without_echoing_bytes()
    {
        AssertInvalid([0x7B, 0x22, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, 0x22, 0x3A, 0x31, 0x2C,
            0x22, 0x65, 0x6E, 0x74, 0x72, 0x69, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x5D, 0x7D, 0xC3, 0x28]);
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[],\"extra\":true}")]
    [InlineData("{\"version\":1.0,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":null}")]
    [InlineData("{\"version\":1,\"entries\":[null]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"x\",\"value\":\"y\",\"remove\":true}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":null,\"value\":\"y\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"x\",\"value\":null}]}")]
    [InlineData("{\"version\":1,// comment\n\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[],}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"x\",\"value\":{}}]}")]
    public void Explicit_environment_document_refuses_closed_shape_and_json_grammar_violations(string json)
    {
        AssertInvalid(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void Explicit_environment_document_enforces_the_json_depth_limit()
    {
        var nested = "0";
        for (var index = 0; index < 65; index++)
            nested = $"[{nested}]";

        AssertInvalid(Encoding.UTF8.GetBytes($"{{\"version\":1,\"entries\":[],\"unknown\":{nested}}}"));
    }

    [Theory]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"\",\"value\":\"ok\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"bad=key\",\"value\":\"ok\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"\\u0001\",\"value\":\"ok\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"\\uD800\",\"value\":\"ok\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"ok\",\"value\":\"\\u0000\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"ok\",\"value\":\"\\uDFFF\"}]}")]
    public void Explicit_environment_document_refuses_invalid_key_value_scalars(string json)
    {
        AssertInvalid(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void Explicit_environment_document_preserves_valid_surrogate_pairs()
    {
        var parsed = ParseEnvironment(Encoding.UTF8.GetBytes(
            "{\"version\":1,\"entries\":[{\"key\":\"emoji\",\"value\":\"\\uD83D\\uDE00\"}]}"));

        Assert.Equal("😀", parsed["emoji"]);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("case")]
    [InlineData("alias")]
    public void Explicit_environment_document_refuses_raw_case_and_normalized_key_collisions(string kind)
    {
        var entries = kind switch
        {
            "raw" => new[] { ("Key", "one"), ("Key", "two") },
            "case" => new[] { ("Key", "one"), ("key", "two") },
            _ => new[] { ("A__B", "one"), ("A:B", "two") }
        };

        var refusal = Assert.Throws<CliRefusal>(() => ParseEnvironment(CandidateInspectionFixture.EnvironmentDocument(entries)));
        Assert.Equal("candidate-environment-key-collision", refusal.Code);
        Assert.Empty(refusal.Details);
    }

    [Theory]
    [InlineData("MYSQLCONNSTR_")]
    [InlineData("SQLAZURECONNSTR_")]
    [InlineData("SQLCONNSTR_")]
    [InlineData("CUSTOMCONNSTR_")]
    [InlineData("POSTGRESQLCONNSTR_")]
    [InlineData("APIHUBCONNSTR_")]
    [InlineData("DOCDBCONNSTR_")]
    [InlineData("EVENTHUBCONNSTR_")]
    [InlineData("NOTIFICATIONHUBCONNSTR_")]
    [InlineData("REDISCACHECONNSTR_")]
    [InlineData("SERVICEBUSCONNSTR_")]
    public void Explicit_environment_document_refuses_each_service_prefix_before_normalization(string prefix)
    {
        var key = prefix.ToLowerInvariant() + "Name";
        var refusal = Assert.Throws<CliRefusal>(() =>
            ParseEnvironment(CandidateInspectionFixture.EnvironmentDocument((key, "private"))));

        Assert.Equal("candidate-environment-prefix-unsupported", refusal.Code);
        Assert.Empty(refusal.Details);
    }

    [Theory]
    [InlineData(1_048_576, false)]
    [InlineData(1_048_577, true)]
    public void Explicit_environment_document_enforces_the_raw_byte_bound(int bytes, bool oversized)
    {
        if (oversized)
        {
            AssertTooLarge(CandidateInspectionFixture.EnvironmentDocumentOfSize(bytes));
            return;
        }

        Assert.Empty(ParseEnvironment(CandidateInspectionFixture.EnvironmentDocumentOfSize(bytes)));
    }

    [Theory]
    [InlineData(1_024, false)]
    [InlineData(1_025, true)]
    public void Explicit_environment_document_enforces_the_entry_count_bound(int count, bool oversized)
    {
        var entries = Enumerable.Range(0, count).Select(index => ($"Key{index}", "value")).ToArray();
        if (oversized)
        {
            AssertTooLarge(CandidateInspectionFixture.EnvironmentDocument(entries));
            return;
        }

        Assert.Equal(count, ParseEnvironment(CandidateInspectionFixture.EnvironmentDocument(entries)).Count);
    }

    [Theory]
    [InlineData(1_024, false)]
    [InlineData(1_025, true)]
    public void Explicit_environment_document_enforces_the_key_byte_bound(int length, bool oversized)
    {
        var document = CandidateInspectionFixture.EnvironmentDocument((new string('k', length), "value"));
        if (oversized)
        {
            AssertTooLarge(document);
            return;
        }

        Assert.Equal("value", ParseEnvironment(document)[new string('k', length)]);
    }

    [Theory]
    [InlineData(65_536, false)]
    [InlineData(65_537, true)]
    public void Explicit_environment_document_enforces_the_value_byte_bound(int length, bool oversized)
    {
        var document = CandidateInspectionFixture.EnvironmentDocument(("Key", new string('v', length)));
        if (oversized)
        {
            AssertTooLarge(document);
            return;
        }

        Assert.Equal(length, ParseEnvironment(document)["Key"].Length);
    }

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

    private static IReadOnlyDictionary<string, string> ParseEnvironment(ReadOnlyMemory<byte> document)
    {
        var parserType = typeof(CompositionInputSnapshot).Assembly.GetType("Elsa.Cli.ExplicitEnvironmentInput", throwOnError: true)!;
        var parse = parserType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            return (IReadOnlyDictionary<string, string>)parse.Invoke(null, [document])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironment(byte[] document) =>
        ParseEnvironment((ReadOnlyMemory<byte>)document);

    private static void AssertInvalid(byte[] document)
    {
        var refusal = Assert.Throws<CliRefusal>(() => ParseEnvironment(document));
        Assert.Equal("candidate-environment-input-invalid", refusal.Code);
        Assert.Empty(refusal.Details);
    }

    private static void AssertTooLarge(byte[] document)
    {
        var refusal = Assert.Throws<CliRefusal>(() => ParseEnvironment(document));
        Assert.Equal("candidate-environment-input-too-large", refusal.Code);
        Assert.Empty(refusal.Details);
    }
}
