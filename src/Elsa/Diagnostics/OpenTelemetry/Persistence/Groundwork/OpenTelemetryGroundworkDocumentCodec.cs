using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Owns the durable JSON format for OpenTelemetry's mutable Groundwork documents.
/// </summary>
/// <remarks>
/// The catalog and capture-ledger documents are greenfield, so version one is a clean baseline.
/// Any later compatible shape change must add an Elsa-owned <see cref="IDocumentJsonUpcaster"/>,
/// advance the applicable policy, and retain the older readable version explicitly.
/// </remarks>
internal sealed class OpenTelemetryGroundworkDocumentCodec
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly VersionedJsonDocumentCodec _codec = new(
        [
            new DocumentSchemaVersionPolicy(Catalogs.CatalogDocuments.ResourceKind, 1, 1),
            new DocumentSchemaVersionPolicy(Catalogs.CatalogDocuments.InstrumentKind, 1, 1),
            new DocumentSchemaVersionPolicy(OpenTelemetryGroundworkStorageSchema.OperationLedgerKind, 1, 1)
        ],
        [],
        new DocumentSchemaVersionFormat(Parse, (_, version) => version.ToString(CultureInfo.InvariantCulture)),
        Options);

    public SaveDocumentRequest CreateSaveRequest<TDocument>(
        string documentKind,
        string id,
        TDocument document,
        long? expectedVersion = null) =>
        _codec.CreateSaveRequest(documentKind, id, document, expectedVersion);

    public TDocument Deserialize<TDocument>(DocumentEnvelope envelope) => _codec.Deserialize<TDocument>(envelope);

    private static int? Parse(string _, string schemaVersion) =>
        int.TryParse(schemaVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
}
