using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Tests;

public sealed class PortableCompositionTests
{
    private const string InputId = "b6f2b17f-180a-4ee3-9d5d-4b42d81fd747";
    private const string InputRevision = "739e9c76-fd64-467e-bbc2-e48163d50fba";
    private const string CandidateId = "a6766759-2040-4b2f-9efb-d6d27c3d5110";
    private const string CatalogDigest = "6563d77f116b7aefb2a67425f28b72cab736297d4e46df964bf9fb507cf91c3c";

    [Fact]
    public void Envelope_codec_preserves_the_authored_json_value_and_writes_only_the_public_wrapper()
    {
        const string authored = "{\"schemaVersion\":\"1\",\"catalog\":{\"id\":\"elsa-foundation\",\"version\":\"3\",\"digest\":\"" + CatalogDigest + "\"},\"profile\":null,\"groups\":[],\"add\":[],\"remove\":[],\"accepted\":{\"catalogDigest\":\"" + CatalogDigest + "\",\"featureIds\":[],\"locks\":[]},\"settings\":{\"Http\":{\"Retries\":1.00,\"Enabled\":false}},\"resources\":null}";
        var envelope = Envelope(authored);

        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var parsed = PortableCompositionJson.ParseComposition(bytes);

        Assert.Equal(authored, parsed.Composition.GetRawText());
        Assert.Equal(JsonValueKind.Number, parsed.Composition.GetProperty("settings").GetProperty("Http").GetProperty("Retries").ValueKind);
        using var publicDocument = JsonDocument.Parse(bytes);
        Assert.Equal("origin", publicDocument.RootElement.GetProperty("inputDisposition").GetString());
        Assert.DoesNotContain("publicEnvelopeSha256", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("production", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("null-input")]
    [InlineData("wrong-version")]
    [InlineData("wrong-kind")]
    [InlineData("bad-guid")]
    [InlineData("bad-disposition")]
    [InlineData("invalid-inner")]
    [InlineData("duplicate-inner")]
    public void Envelope_codec_refuses_malformed_or_unsupported_wire_shapes(string mutation)
    {
        var json = EnvelopeJson();
        json = mutation switch
        {
            "duplicate" => json.Replace("\"kind\":\"portable-composition\"", "\"kind\":\"portable-composition\",\"kind\":\"portable-composition\"", StringComparison.Ordinal),
            "unknown" => json.Replace("\"inputDisposition\":\"origin\"", "\"inputDisposition\":\"origin\",\"extra\":true", StringComparison.Ordinal),
            "null-input" => json.Replace("\"requiredInput\":{\"kind\":\"workbench-json-bundle\",\"id\":\"" + InputId + "\",\"revision\":\"" + InputRevision + "\"}", "\"requiredInput\":null", StringComparison.Ordinal),
            "wrong-version" => json.Replace("\"schemaVersion\":\"1\"", "\"schemaVersion\":\"2\"", StringComparison.Ordinal),
            "wrong-kind" => json.Replace("portable-composition", "other", StringComparison.Ordinal),
            "bad-guid" => json.Replace(InputId, "b6f2b17f-180a-1ee3-9d5d-4b42d81fd747", StringComparison.Ordinal),
            "bad-disposition" => json.Replace("\"inputDisposition\":\"origin\"", "\"inputDisposition\":null", StringComparison.Ordinal),
            "invalid-inner" => json.Replace(AuthoredJson(), "{}", StringComparison.Ordinal),
            "duplicate-inner" => json.Replace("\"settings\":null", "\"settings\":{\"Http\":{\"Retries\":1,\"Retries\":2}}", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        AssertCode("portable-input-invalid", () => PortableCompositionJson.ParseComposition(json));
    }

    [Fact]
    public void Receipt_codecs_roundtrip_private_context_and_sorted_hashes()
    {
        var envelope = Envelope();
        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var snapshot = InputSnapshot();
        var inputReceipt = PortableCompositionValidator.CreateInputReceipt(envelope, bytes, snapshot);
        var inputRoundtrip = PortableCompositionJson.ParseInputReceipt(PortableCompositionJson.SerializeInputReceipt(inputReceipt));
        var candidateReceipt = PortableCompositionValidator.CreateCandidateReceipt(envelope, bytes, snapshot, CandidateId);
        var candidateRoundtrip = PortableCompositionJson.ParseCandidateReceipt(PortableCompositionJson.SerializeCandidateReceipt(candidateReceipt));

        Assert.Equal(new PortableInputContext("default", "production"), inputRoundtrip.Context);
        Assert.Equal(snapshot.FileNames.ToArray(), inputRoundtrip.Files.Select(file => file.Name).ToArray());
        Assert.Equal(Hash(bytes), inputRoundtrip.PublicEnvelopeSha256);
        Assert.Equal(envelope.InputDisposition, candidateRoundtrip.InputDisposition);
        Assert.Equal(InputId, candidateRoundtrip.InputId);
        Assert.Equal(InputRevision, candidateRoundtrip.InputRevision);
        AssertCode("portable-input-invalid", () => PortableCompositionValidator.CreateCandidateReceipt(envelope, bytes, snapshot, "not-a-v4-uuid"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-version")]
    [InlineData("wrong-kind")]
    [InlineData("bad-guid")]
    [InlineData("uppercase-hash")]
    [InlineData("unsafe-name")]
    [InlineData("case-collision")]
    [InlineData("unsorted")]
    [InlineData("null-context")]
    [InlineData("empty-files")]
    public void Input_receipt_codec_refuses_invalid_fields_and_inventories(string mutation)
    {
        var json = InputReceiptJson();
        json = mutation switch
        {
            "unknown" => json.Replace("\"kind\":\"portable-input-receipt\"", "\"kind\":\"portable-input-receipt\",\"extra\":true", StringComparison.Ordinal),
            "wrong-version" => json.Replace("\"schemaVersion\":\"1\"", "\"schemaVersion\":\"3\"", StringComparison.Ordinal),
            "wrong-kind" => json.Replace("portable-input-receipt", "future-receipt", StringComparison.Ordinal),
            "bad-guid" => json.Replace(InputRevision, "739e9c76-fd64-167e-bbc2-e48163d50fba", StringComparison.Ordinal),
            "uppercase-hash" => json.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal),
            "unsafe-name" => json.Replace("appsettings.json", "../appsettings.json", StringComparison.Ordinal),
            "case-collision" => json.Replace("{\"name\":\"shells.json\",\"sha256\":\"" + new string('b', 64) + "\"}", "{\"name\":\"shells.PRODUCTION.json\",\"sha256\":\"" + new string('b', 64) + "\"}", StringComparison.Ordinal),
            "unsorted" => ReverseFileRows(json),
            "null-context" => json.Replace("\"context\":{\"shell\":\"default\",\"environment\":\"production\"}", "\"context\":null", StringComparison.Ordinal),
            "empty-files" => json.Replace("\"files\":[{\"name\":\"appsettings.json\",\"sha256\":\"" + new string('b', 64) + "\"},{\"name\":\"shells.production.json\",\"sha256\":\"" + new string('c', 64) + "\"},{\"name\":\"shells.json\",\"sha256\":\"" + new string('d', 64) + "\"}]", "\"files\":[]", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        AssertCode("portable-input-invalid", () => PortableCompositionJson.ParseInputReceipt(json));
    }

    [Fact]
    public void Receipts_preserve_supported_long_sibling_names_without_changing_the_selected_context()
    {
        var envelope = Envelope();
        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var source = InputSnapshot();
        var sibling = $"appsettings.{new string('x', 129)}.json";
        var files = source.FileNames.Select(name => new KeyValuePair<string, byte[]>(name, source.CopyBytes(name)))
            .Append(Pair(sibling, "private sibling bytes"));
        var complete = SourceSnapshot.Freeze(source.Selection, files);

        var receipt = PortableCompositionValidator.CreateInputReceipt(envelope, bytes, complete);
        var roundtrip = PortableCompositionJson.ParseInputReceipt(PortableCompositionJson.SerializeInputReceipt(receipt));

        Assert.Contains(roundtrip.Files, file => file.Name == sibling && file.Sha256 == Hash(complete.CopyBytes(sibling)));
        PortableCompositionValidator.ValidateInput(envelope, bytes, roundtrip, complete);
    }

    [Fact]
    public void Candidate_receipt_codec_refuses_unknown_disposition_and_missing_fields()
    {
        var valid = CandidateReceiptJson();
        AssertCode("portable-input-invalid", () => PortableCompositionJson.ParseCandidateReceipt(valid.Replace("\"inputDisposition\":\"origin\"", "\"inputDisposition\":\"source\"", StringComparison.Ordinal)));
        AssertCode("portable-input-invalid", () => PortableCompositionJson.ParseCandidateReceipt(valid.Replace("\"candidateId\":\"" + CandidateId + "\",", "", StringComparison.Ordinal)));
    }

    [Fact]
    public void Input_validation_binds_exact_public_bytes_context_and_the_complete_frozen_inventory()
    {
        var envelope = Envelope();
        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var snapshot = InputSnapshot();
        var receipt = PortableCompositionValidator.CreateInputReceipt(envelope, bytes, snapshot);
        var review = Review(("Http", "/Retries", "number"));

        PortableCompositionValidator.ValidateInput(envelope, bytes, receipt, snapshot, review);
        PortableCompositionValidator.ValidateInputAssociation(envelope, bytes, receipt);
        PortableCompositionValidator.ValidateInputForRebind(envelope, bytes, receipt, review);

        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInputAssociation(envelope, [.. bytes, (byte)' '], receipt));
        var edited = Envelope(AuthoredJson("{\"Http\":{\"Future\":\"canary\"}}", "null"));
        var editedBytes = PortableCompositionJson.SerializeComposition(edited);
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInput(edited, editedBytes, receipt, snapshot, review));
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInput(envelope, bytes, receipt, InputSnapshot(contentMarker: "changed"), review));
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInput(envelope, bytes, receipt, InputSnapshot(extraFile: true), review));
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInput(envelope, bytes, receipt with { Context = new("worker", "production") }, snapshot, review));
    }

    [Fact]
    public void Acceptance_keeps_the_logical_input_id_and_uses_a_fresh_revision_and_receipt()
    {
        var original = Envelope();
        var accepted = Envelope(revision: "749e9c76-fd64-467e-bbc2-e48163d50fba");
        var originalBytes = PortableCompositionJson.SerializeComposition(original);
        var acceptedBytes = PortableCompositionJson.SerializeComposition(accepted);
        var snapshot = InputSnapshot();
        var oldReceipt = PortableCompositionValidator.CreateInputReceipt(original, originalBytes, snapshot);
        var acceptedReceipt = PortableCompositionValidator.CreateInputReceipt(accepted, acceptedBytes, snapshot);

        PortableCompositionValidator.ValidateInput(accepted, acceptedBytes, acceptedReceipt, snapshot);

        Assert.Equal(original.RequiredInput.Id, accepted.RequiredInput.Id);
        Assert.NotEqual(original.RequiredInput.Revision, accepted.RequiredInput.Revision);
        Assert.Equal(PortableInputDisposition.Origin, accepted.InputDisposition);
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateInputAssociation(accepted, acceptedBytes, oldReceipt));
    }

    [Fact]
    public void Rebind_keeps_context_but_admits_a_complete_replacement_inventory()
    {
        var envelope = Envelope();
        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var previousReceipt = PortableCompositionValidator.CreateInputReceipt(envelope, bytes, InputSnapshot());
        var replacement = InputSnapshot(contentMarker: "replacement", extraFile: true);

        PortableCompositionValidator.ValidateInputForRebind(envelope, bytes, previousReceipt);
        PortableCompositionValidator.ValidateRebindReplacementContext(previousReceipt, replacement);

        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateRebindReplacementContext(
            previousReceipt, Snapshot("other", "production")));
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateRebindReplacementContext(
            previousReceipt, Snapshot("default", "production", missingShellOverlay: true)));
    }

    [Fact]
    public void Candidate_validation_needs_only_the_public_envelope_receipt_and_generated_bytes()
    {
        var envelope = Envelope(disposition: PortableInputDisposition.Replacement);
        var bytes = PortableCompositionJson.SerializeComposition(envelope);
        var generated = InputSnapshot(contentMarker: "candidate");
        var receipt = PortableCompositionValidator.CreateCandidateReceipt(envelope, bytes, generated, CandidateId);

        PortableCompositionValidator.ValidateCandidate(envelope, bytes, receipt, generated);

        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateCandidate(
            envelope, bytes, receipt with { InputDisposition = PortableInputDisposition.Origin }, generated));
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateCandidate(
            envelope, bytes, receipt, InputSnapshot(contentMarker: "edited")));
        var otherEnvelope = Envelope(id: CandidateId);
        var otherBytes = PortableCompositionJson.SerializeComposition(otherEnvelope);
        AssertCode("portable-input-mismatch", () => PortableCompositionValidator.ValidateCandidate(
            otherEnvelope, otherBytes, receipt, generated));
    }

    [Fact]
    public void Public_settings_allow_only_exact_reviewed_leaf_types_and_empty_arrays()
    {
        var composition = Authored(
            "{\"Http\":{\"Retries\":0,\"Enabled\":false,\"Nothing\":null,\"Empty\":{\"Inner\":{}},\"Flags\":[]}}",
            "null");
        var review = Review(
            ("Http", "/Retries", "number"),
            ("Http", "/Enabled", "boolean"),
            ("Http", "/Nothing", "null"),
            ("Http", "/Empty/Inner", "object"),
            ("Http", "/Flags", "array"));

        PortableCompositionValidator.ValidatePublicContent(composition, review);
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("type-mismatch")]
    [InlineData("nonempty-array")]
    [InlineData("portable-parent")]
    [InlineData("forced-private")]
    [InlineData("provider")]
    public void Public_settings_refuse_unreviewed_opaque_arrays_or_private_persistence(string mutation)
    {
        var (settings, review) = mutation switch
        {
            "opaque" => ("{\"Http\":{\"Future\":\"canary\"}}", (SettingReviewDocument?)null),
            "type-mismatch" => ("{\"Http\":{\"Retries\":\"3\"}}", Review(("Http", "/Retries", "number"))),
            "nonempty-array" => ("{\"Http\":{\"Values\":[1]}}", Review(("Http", "/Values", "array"))),
            "portable-parent" => ("{\"Http\":{\"Nested\":{\"Name\":\"safe\"}}}", Review(("Http", "/Nested", "object", true), ("Http", "/Nested/Name", "string", true))),
            "forced-private" => ("{\"Http\":{\"ConnectionStrings\":{\"Primary\":\"secret\"}}}", Review(("Http", "/ConnectionStrings/Primary", "string"))),
            "provider" => ("{\"Http\":{\"Storage\":{\"Provider\":\"Sqlite\"}}}", Review(("Http", "/Storage/Provider", "string"))),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        AssertCode("bridge-portable-unsafe", () => PortableCompositionValidator.ValidatePublicContent(Authored(settings, "null"), review));
    }

    [Fact]
    public void Public_resources_accept_only_logical_persistence_references()
    {
        var safe = Authored("null", "{\"persistence\":{\"defaultResource\":\"primary\",\"bindings\":{\"Http\":\"primary\"}}}");
        PortableCompositionValidator.ValidatePublicContent(safe, null);

        AssertCode("bridge-portable-unsafe", () => PortableCompositionValidator.ValidatePublicContent(
            Authored("null", "{\"persistence\":{\"defaultResource\":\"Server=private;Password=secret\"}}"), null));
        AssertCode("bridge-portable-unsafe", () => PortableCompositionValidator.ValidatePublicContent(
            Authored("null", "{\"persistence\":{\"provider\":\"Sqlite\"}}"), null));
        AssertCode("bridge-portable-unsafe", () => PortableCompositionValidator.ValidatePublicContent(
            Authored("null", "{\"persistence\":{\"bindings\":{}}}"), null));
    }

    [Fact]
    public void Portable_validator_does_not_leak_json_parser_exceptions()
    {
        var malformed = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\"}");

        var exception = Assert.Throws<CompositionImportException>(() => PortableCompositionJson.ParseComposition(malformed));

        Assert.Equal("portable-input-invalid", exception.Code);
        Assert.DoesNotContain("schemaVersion", exception.Message, StringComparison.Ordinal);
    }

    private static PortableComposition Envelope(
        string? authoredJson = null,
        string id = InputId,
        string revision = InputRevision,
        PortableInputDisposition disposition = PortableInputDisposition.Origin)
    {
        using var document = JsonDocument.Parse(authoredJson ?? AuthoredJson());
        return new PortableComposition(
            "1",
            PortableCompositionJson.EnvelopeKind,
            document.RootElement.Clone(),
            new PortableRequiredInput(PortableCompositionJson.RequiredInputKind, id, revision),
            disposition);
    }

    private static AuthoredComposition Authored(string settings, string resources) =>
        SelectionJsonReader.ParseComposition(AuthoredJson(settings, resources));

    private static string AuthoredJson(string? settings = "null", string? resources = "null") =>
        $$"""{"schemaVersion":"1","catalog":{"id":"elsa-foundation","version":"3","digest":"{{CatalogDigest}}"},"profile":null,"groups":[],"add":[],"remove":[],"accepted":{"catalogDigest":"{{CatalogDigest}}","featureIds":[],"locks":[]},"settings":{{settings}},"resources":{{resources}}}""";

    private static string EnvelopeJson() =>
        $$"""{"schemaVersion":"1","kind":"portable-composition","composition":{{AuthoredJson()}},"requiredInput":{"kind":"workbench-json-bundle","id":"{{InputId}}","revision":"{{InputRevision}}"},"inputDisposition":"origin"}""";

    private static string InputReceiptJson() =>
        $$"""{"schemaVersion":"1","kind":"portable-input-receipt","inputId":"{{InputId}}","inputRevision":"{{InputRevision}}","publicEnvelopeSha256":"{{new string('a', 64)}}","context":{"shell":"default","environment":"production"},"files":[{"name":"appsettings.json","sha256":"{{new string('b', 64)}}"},{"name":"shells.production.json","sha256":"{{new string('c', 64)}}"},{"name":"shells.json","sha256":"{{new string('d', 64)}}"}]}""";

    private static string CandidateReceiptJson() =>
        $$"""{"schemaVersion":"1","kind":"portable-candidate-receipt","candidateId":"{{CandidateId}}","inputId":"{{InputId}}","inputRevision":"{{InputRevision}}","publicEnvelopeSha256":"{{new string('a', 64)}}","context":{"shell":"default","environment":"production"},"inputDisposition":"origin","files":[{"name":"appsettings.json","sha256":"{{new string('b', 64)}}"},{"name":"shells.production.json","sha256":"{{new string('c', 64)}}"},{"name":"shells.json","sha256":"{{new string('d', 64)}}"}]}""";

    private static string ReverseFileRows(string json)
    {
        var first = "{\"name\":\"appsettings.json\",\"sha256\":\"" + new string('b', 64) + "\"}";
        var second = "{\"name\":\"shells.production.json\",\"sha256\":\"" + new string('c', 64) + "\"}";
        return json.Replace(first + "," + second, second + "," + first, StringComparison.Ordinal);
    }

    private static SourceSnapshot InputSnapshot(string contentMarker = "source", bool extraFile = false)
    {
        var files = new List<KeyValuePair<string, byte[]>>
        {
            Pair("shells.json", $"shell-base-{contentMarker}"),
            Pair("shells.production.json", $"shell-overlay-{contentMarker}"),
            Pair("appsettings.json", $"appsettings-{contentMarker}")
        };
        if (extraFile)
            files.Add(Pair("appsettings.Staging.json", "extra"));
        return SourceSnapshot.Freeze(
            new SourceSelection("default", "production", "shells.production.json", null),
            files);
    }

    private static SourceSnapshot Snapshot(string shell, string environment, bool missingShellOverlay = false)
    {
        var files = new List<KeyValuePair<string, byte[]>>
        {
            Pair("shells.json", "base"),
            Pair("appsettings.json", "appsettings")
        };
        if (!missingShellOverlay)
            files.Add(Pair($"shells.{environment}.json", "overlay"));
        return SourceSnapshot.Freeze(new SourceSelection(shell, environment, $"shells.{environment}.json", null), files);
    }

    private static SettingReviewDocument Review(params (string Feature, string Pointer, string Type, bool Portable)[] rows)
    {
        var fields = string.Join(",", rows.Select(row =>
            $$"""{"featureId":"{{row.Feature}}","pointer":"{{row.Pointer}}","type":"{{row.Type}}","portable":{{row.Portable.ToString().ToLowerInvariant()}}}"""));
        return SettingReviewReader.Parse($$"""{"schemaVersion":"1","fields":[{{fields}}]}""");
    }

    private static SettingReviewDocument Review(params (string Feature, string Pointer, string Type)[] rows) =>
        Review(rows.Select(row => (row.Feature, row.Pointer, row.Type, true)).ToArray());

    private static KeyValuePair<string, byte[]> Pair(string name, string content) =>
        new(name, Encoding.UTF8.GetBytes(content));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<CompositionImportException>(action).Code);
}
