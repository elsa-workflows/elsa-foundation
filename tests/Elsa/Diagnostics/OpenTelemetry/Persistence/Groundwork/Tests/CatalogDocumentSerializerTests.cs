using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Groundwork.Documents.Store;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class CatalogDocumentSerializerTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 14, 13, 14, 15, TimeSpan.FromHours(3));

    private readonly CatalogDocumentSerializer _serializer = new();

    [Fact]
    public void Resource_document_is_canonical_keyed_and_restart_stable()
    {
        var first = Resource(Attributes(("z", "last"), ("a", "first")));
        var equivalent = Resource(Attributes(("a", "first"), ("z", "last")));

        var request = _serializer.ToSaveRequest(first, expectedRevision: 4);
        var equivalentRequest = _serializer.ToSaveRequest(equivalent, expectedRevision: 4);
        var restored = _serializer.ToResource(Envelope(request, revision: 5));
        var afterRestart = _serializer.ToSaveRequest(restored.Resource, expectedRevision: restored.Revision);

        Assert.Equal(CatalogDocuments.ResourceKind, request.DocumentKind);
        Assert.Equal(first.Id, request.Id);
        Assert.Equal(CatalogDocuments.SchemaVersion, request.SchemaVersion);
        Assert.Equal(request.ContentJson, equivalentRequest.ContentJson);
        Assert.Equal(request.ContentJson, afterRestart.ContentJson);
        Assert.Equal(5, restored.Revision);
        Assert.Equal(first with { Attributes = restored.Resource.Attributes }, restored.Resource);
        AssertAttributes(first.Attributes, restored.Resource.Attributes);
    }

    [Fact]
    public void Instrument_document_preserves_explicit_retry_time_tie_breaker_and_revision()
    {
        var instrument = Instrument(Attributes(("z", null), ("a", "first")));
        var request = _serializer.ToSaveRequest(instrument, Timestamp, expectedRevision: 2);
        var restored = _serializer.ToInstrument(Envelope(request, revision: 3));
        var retried = _serializer.ToSaveRequest(restored.Instrument, restored.LastSeen, expectedRevision: 2);

        Assert.Equal(CatalogDocuments.InstrumentKind, request.DocumentKind);
        Assert.Equal(request.ContentJson, retried.ContentJson);
        Assert.Equal(Timestamp, restored.LastSeen);
        Assert.Equal(instrument.Id, restored.RetentionTieBreaker);
        Assert.Equal(3, restored.Revision);
        Assert.Equal(instrument with { Attributes = restored.Instrument.Attributes }, restored.Instrument);
        AssertAttributes(instrument.Attributes, restored.Instrument.Attributes);
    }

    [Fact]
    public void Catalog_payload_exposes_top_level_fields_required_for_bounded_retention_and_queries()
    {
        var resource = _serializer.ToSaveRequest(Resource(Attributes()));
        var instrument = _serializer.ToSaveRequest(Instrument(Attributes()), Timestamp);

        using var resourceJson = System.Text.Json.JsonDocument.Parse(resource.ContentJson);
        using var instrumentJson = System.Text.Json.JsonDocument.Parse(instrument.ContentJson);

        Assert.Equal("api", resourceJson.RootElement.GetProperty("serviceName").GetString());
        Assert.Equal(Resource(Attributes()).LastSeen.UtcTicks, resourceJson.RootElement.GetProperty("lastSeenUtcTicks").GetInt64());
        Assert.Equal("request.duration", instrumentJson.RootElement.GetProperty("name").GetString());
        Assert.Equal("instrument-1", instrumentJson.RootElement.GetProperty("retentionTieBreaker").GetString());
    }

    [Fact]
    public void Wrong_kind_schema_identity_and_malformed_json_fail_without_fallback()
    {
        var request = _serializer.ToSaveRequest(Resource(Attributes()));
        var envelope = Envelope(request, revision: 1);

        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(envelope with { DocumentKind = "foreign" }));
        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(envelope with { SchemaVersion = "2" }));
        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(envelope with { Id = "foreign" }));
        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(envelope with { Version = -1 }));
        var exception = Assert.Throws<CatalogPayloadException>(() =>
            _serializer.ToResource(envelope with { ContentJson = "{" }));
        Assert.NotNull(exception.InnerException);

        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(envelope with
        {
            ContentJson = "{\"id\":\"resource-1\",\"serviceName\":\"api\"}"
        }));
    }

    [Fact]
    public void Negative_expected_revision_is_rejected_before_a_document_request_is_created()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _serializer.ToSaveRequest(Resource(Attributes()), expectedRevision: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _serializer.ToSaveRequest(Instrument(Attributes()), Timestamp, expectedRevision: -1));
    }

    [Fact]
    public void Missing_required_domain_values_and_null_attributes_fail_before_save()
    {
        Assert.Throws<CatalogPayloadException>(() =>
            _serializer.ToSaveRequest(Resource(Attributes()) with { ServiceName = " " }));
        Assert.Throws<CatalogPayloadException>(() =>
            _serializer.ToSaveRequest(Instrument(Attributes()) with { Attributes = null! }, Timestamp));
    }

    [Fact]
    public void Null_and_duplicate_persisted_attribute_keys_are_visible_payload_failures()
    {
        var request = _serializer.ToSaveRequest(Resource(Attributes(("key", "value"))));
        var duplicate = WithMutatedAttributes(request, attributes => attributes.Add(attributes[0]!.DeepClone()));
        var nullKey = WithMutatedAttributes(request, attributes => attributes[0]!["key"] = null);

        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(duplicate));
        Assert.Throws<CatalogPayloadException>(() => _serializer.ToResource(nullKey));
    }

    private static DocumentEnvelope Envelope(SaveDocumentRequest request, long revision) => new(
        request.DocumentKind,
        request.Id,
        request.SchemaVersion,
        revision,
        request.ContentJson,
        Timestamp,
        Timestamp);

    private static DocumentEnvelope WithMutatedAttributes(
        SaveDocumentRequest request,
        Action<JsonArray> mutate)
    {
        var content = JsonNode.Parse(request.ContentJson)!;
        var attributes = content["attributes"]!.AsArray();
        mutate(attributes);
        return Envelope(request, revision: 1) with { ContentJson = content.ToJsonString() };
    }

    private static TelemetryResource Resource(IDictionary<string, string?> attributes) => new(
        "resource-1",
        "api",
        "api-1",
        "dotnet",
        attributes,
        Timestamp,
        TelemetryResourceStatus.Active);

    private static MetricInstrument Instrument(IDictionary<string, string?> attributes) => new(
        "instrument-1",
        "resource-1",
        "request.duration",
        "ms",
        "request duration",
        MetricKind.Gauge,
        attributes);

    private static Dictionary<string, string?> Attributes(params (string Key, string? Value)[] values)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            result.Add(key, value);
        return result;
    }

    private static void AssertAttributes(
        IDictionary<string, string?> expected,
        IDictionary<string, string?> actual) =>
        Assert.Equal(
            expected.OrderBy(x => x.Key, StringComparer.Ordinal),
            actual.OrderBy(x => x.Key, StringComparer.Ordinal));
}
