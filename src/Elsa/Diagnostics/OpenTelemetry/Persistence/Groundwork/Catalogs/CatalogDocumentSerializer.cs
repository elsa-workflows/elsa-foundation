using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;

/// <summary>Maps mutable resource and instrument catalogs to canonical keyed Groundwork documents.</summary>
public sealed class CatalogDocumentSerializer
{
    private static readonly OpenTelemetryGroundworkDocumentCodec Codec = new();

    /// <summary>Creates a canonical keyed document request for a resource catalog value.</summary>
    /// <exception cref="CatalogPayloadException">The resource cannot be represented canonically.</exception>
    public SaveDocumentRequest ToSaveRequest(TelemetryResource resource, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ValidateExpectedRevision(expectedRevision);

        return Map(() =>
        {
            var id = RequiredText(resource.Id, nameof(resource.Id));
            var serviceName = RequiredText(resource.ServiceName, nameof(resource.ServiceName));
            var idSearchKey = SearchKey(id);
            var serviceNameProjection = DiagnosticStringComparisonKey.Project(
                serviceName,
                DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase);
            EnumValue<TelemetryResourceStatus>((int)resource.Status, nameof(resource.Status));

            return CreateRequest(
                CatalogDocuments.ResourceKind,
                id,
                new ResourceDocument(
                    id,
                    serviceName,
                    idSearchKey,
                    serviceNameProjection.ComparisonKey,
                    serviceNameProjection.ComparisonKeyHash,
                    serviceNameProjection.SearchKey,
                    resource.ServiceInstanceId,
                    resource.TelemetrySdkLanguage,
                    Canonicalize(resource.Attributes),
                    resource.LastSeen.UtcTicks,
                    (int)resource.Status,
                    RetentionTieBreaker(id)),
                expectedRevision);
        });
    }

    /// <summary>Creates a canonical keyed document request for an instrument catalog value.</summary>
    /// <remarks>The caller supplies the observation time once and reuses it across retries.</remarks>
    /// <exception cref="CatalogPayloadException">The instrument cannot be represented canonically.</exception>
    public SaveDocumentRequest ToSaveRequest(
        MetricInstrument instrument,
        DateTimeOffset lastSeen,
        long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ValidateExpectedRevision(expectedRevision);

        return Map(() =>
        {
            var id = RequiredText(instrument.Id, nameof(instrument.Id));
            var resourceId = RequiredText(instrument.ResourceId, nameof(instrument.ResourceId));
            var name = RequiredText(instrument.Name, nameof(instrument.Name));
            EnumValue<MetricKind>((int)instrument.Kind, nameof(instrument.Kind));

            return CreateRequest(
                CatalogDocuments.InstrumentKind,
                id,
                new InstrumentDocument(
                    id,
                    resourceId,
                    name,
                    instrument.Unit,
                    instrument.Description,
                    (int)instrument.Kind,
                    Canonicalize(instrument.Attributes),
                    lastSeen.UtcTicks,
                    RetentionTieBreaker(id)),
                expectedRevision);
        });
    }

    /// <summary>Restores a resource catalog value and its revision from a Groundwork document.</summary>
    /// <exception cref="CatalogPayloadException">The document kind, schema, identity, or payload is invalid.</exception>
    public ResourceCatalogEntry ToResource(DocumentEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Deserialize(envelope, CatalogDocuments.ResourceKind, (ResourceDocument document) =>
        {
            ValidateIdentity(envelope, document.Id, document.RetentionTieBreaker);
            ValidateSearchKey(document.Id, document.IdSearchKey, nameof(document.IdSearchKey));
            ValidateServiceProjection(document);
            return new ResourceCatalogEntry(
                new(
                    RequiredText(document.Id, nameof(document.Id)),
                    RequiredText(document.ServiceName, nameof(document.ServiceName)),
                    document.ServiceInstanceId,
                    document.TelemetrySdkLanguage,
                    Restore(document.Attributes),
                    Utc(document.LastSeenUtcTicks),
                    EnumValue<TelemetryResourceStatus>(document.Status, nameof(document.Status))),
                envelope.Version);
        });
    }

    /// <summary>Restores an instrument catalog value, retention metadata, and revision from a Groundwork document.</summary>
    /// <exception cref="CatalogPayloadException">The document kind, schema, identity, or payload is invalid.</exception>
    public InstrumentCatalogEntry ToInstrument(DocumentEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Deserialize(envelope, CatalogDocuments.InstrumentKind, (InstrumentDocument document) =>
        {
            ValidateIdentity(envelope, document.Id, document.RetentionTieBreaker);
            return new InstrumentCatalogEntry(
                new(
                    RequiredText(document.Id, nameof(document.Id)),
                    RequiredText(document.ResourceId, nameof(document.ResourceId)),
                    RequiredText(document.Name, nameof(document.Name)),
                    document.Unit,
                    document.Description,
                    EnumValue<MetricKind>(document.Kind, nameof(document.Kind)),
                    Restore(document.Attributes)),
                Utc(document.LastSeenUtcTicks),
                RequiredText(document.RetentionTieBreaker, nameof(document.RetentionTieBreaker)),
                envelope.Version);
        });
    }

    private static SaveDocumentRequest CreateRequest<TDocument>(
        string documentKind,
        string id,
        TDocument document,
        long? expectedRevision)
    {
        try
        {
            return Codec.CreateSaveRequest(documentKind, id, document, expectedRevision);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        {
            throw new CatalogPayloadException("The OpenTelemetry catalog value cannot be serialized.", exception);
        }
    }

    private static SaveDocumentRequest Map(Func<SaveDocumentRequest> map)
    {
        try
        {
            return map();
        }
        catch (CatalogPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        {
            throw new CatalogPayloadException(
                "The OpenTelemetry catalog value cannot be represented as a canonical payload.",
                exception);
        }
    }

    private static void ValidateExpectedRevision(long? expectedRevision) =>
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision ?? 0, 0);

    private static TResult Deserialize<TDocument, TResult>(
        DocumentEnvelope envelope,
        string expectedKind,
        Func<TDocument, TResult> map)
        where TDocument : class
    {
        try
        {
            if (!StringComparer.Ordinal.Equals(envelope.DocumentKind, expectedKind))
                throw new CatalogPayloadException(
                    $"OpenTelemetry catalog document kind '{envelope.DocumentKind}' is not '{expectedKind}'.");
            if (envelope.Version < 0)
                throw new CatalogPayloadException("The OpenTelemetry catalog revision cannot be negative.");

            var document = Codec.Deserialize<TDocument>(envelope);
            return map(document);
        }
        catch (CatalogPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException or OverflowException or DocumentSchemaVersionException)
        {
            throw new CatalogPayloadException("The OpenTelemetry catalog payload is malformed.", exception);
        }
    }

    private static void ValidateIdentity(DocumentEnvelope envelope, string? id, string? retentionTieBreaker)
    {
        if (!EquivalentIdentity(envelope.Id, id))
            throw new CatalogPayloadException("The OpenTelemetry catalog payload identity does not match its envelope.");
        if (!StringComparer.Ordinal.Equals(RetentionTieBreaker(id!), retentionTieBreaker))
            throw new CatalogPayloadException("The OpenTelemetry catalog retention tie-breaker does not match its identity.");
    }

    private static bool EquivalentIdentity(string first, string? second) =>
        second is not null &&
        StringComparer.Ordinal.Equals(
            DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(first),
            DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(second));

    private static string RetentionTieBreaker(string value)
    {
        var key = DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal static string ComparisonKey(string value) =>
        DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(RequiredText(value, nameof(value)));

    internal static string SearchKey(string value) =>
        DiagnosticStringComparisonKey.CreateSearchKey(
            RequiredText(value, nameof(value)),
            DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase);

    private static void ValidateSearchKey(string? value, string? searchKey, string field)
    {
        if (!StringComparer.Ordinal.Equals(SearchKey(RequiredText(value, field)), searchKey))
            throw new CatalogPayloadException($"The OpenTelemetry catalog field '{field}' is not canonical.");
    }

    private static void ValidateServiceProjection(ResourceDocument document)
    {
        var projection = DiagnosticStringComparisonKey.Project(
            RequiredText(document.ServiceName, nameof(document.ServiceName)),
            DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase);
        if (!StringComparer.Ordinal.Equals(projection.ComparisonKey, document.ServiceNameComparisonKey) ||
            !StringComparer.Ordinal.Equals(projection.ComparisonKeyHash, document.ServiceNameHash) ||
            !StringComparer.Ordinal.Equals(projection.SearchKey, document.ServiceNameSearchKey))
        {
            throw new CatalogPayloadException("The OpenTelemetry resource service-name projection is not canonical.");
        }
    }

    private static AttributeDocument[] Canonicalize(IDictionary<string, string?> attributes) =>
        Required(attributes, nameof(attributes))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new AttributeDocument(RequiredText(x.Key, "attribute key"), x.Value))
            .ToArray();

    private static Dictionary<string, string?> Restore(AttributeDocument[]? attributes)
    {
        if (attributes is null)
            throw new CatalogPayloadException("The OpenTelemetry catalog attributes are missing.");

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            var value = Required(attribute, "attribute");
            if (value.Key is null || !result.TryAdd(value.Key, value.Value))
                throw new CatalogPayloadException("The OpenTelemetry catalog contains an invalid or duplicate attribute key.");
        }
        return result;
    }

    private static T Required<T>(T? value, string field) where T : class =>
        value ?? throw new CatalogPayloadException($"The OpenTelemetry catalog field '{field}' is required.");

    private static string RequiredText(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CatalogPayloadException($"The OpenTelemetry catalog field '{field}' requires a non-empty value.");

    private static TEnum EnumValue<TEnum>(int value, string field) where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value)
            : throw new CatalogPayloadException($"The OpenTelemetry catalog field '{field}' has an unsupported value.");

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);

    private sealed record AttributeDocument(string Key, string? Value);

    private sealed record ResourceDocument(
        string Id,
        string ServiceName,
        string IdSearchKey,
        string ServiceNameComparisonKey,
        string ServiceNameHash,
        string ServiceNameSearchKey,
        string? ServiceInstanceId,
        string? TelemetrySdkLanguage,
        AttributeDocument[] Attributes,
        long LastSeenUtcTicks,
        int Status,
        string RetentionTieBreaker);

    private sealed record InstrumentDocument(
        string Id,
        string ResourceId,
        string Name,
        string? Unit,
        string? Description,
        int Kind,
        AttributeDocument[] Attributes,
        long LastSeenUtcTicks,
        string RetentionTieBreaker);
}
