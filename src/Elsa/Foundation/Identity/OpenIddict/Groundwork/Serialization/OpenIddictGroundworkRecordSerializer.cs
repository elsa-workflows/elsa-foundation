using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;

/// <summary>Owns the exact record-kind/type binding used by all four stores.</summary>
public static class OpenIddictGroundworkRecordSerializer
{
    private static readonly VersionedJsonDocumentCodec Codec = OpenIddictGroundworkJson.CreateCodec();

    public static SaveDocumentRequest CreateSaveRequest<TRecord>(
        TRecord record,
        long? expectedVersion = null)
        where TRecord : OpenIddictGroundworkRecord
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateIdentity(record.Id);
        ValidateConcurrencyToken(record.ConcurrencyToken);

        try
        {
            return Codec.CreateSaveRequest(DocumentKind<TRecord>(), record.Id, record, expectedVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new OpenIddictGroundworkSerializationException(
                $"The OpenIddict Groundwork record '{record.Id}' could not be serialized.",
                exception);
        }
    }

    public static TRecord Deserialize<TRecord>(DocumentEnvelope envelope)
        where TRecord : OpenIddictGroundworkRecord
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var expectedKind = DocumentKind<TRecord>();
        if (!string.Equals(envelope.DocumentKind, expectedKind, StringComparison.Ordinal))
        {
            throw new OpenIddictGroundworkSerializationException(
                $"Document kind '{envelope.DocumentKind}' cannot be materialized as '{typeof(TRecord).Name}'.");
        }

        try
        {
            var record = Codec.Deserialize<TRecord>(envelope);
            if (!string.Equals(record.Id, envelope.Id, StringComparison.Ordinal))
            {
                throw new OpenIddictGroundworkSerializationException(
                    $"OpenIddict record identity '{record.Id}' does not match envelope identity '{envelope.Id}'.");
            }
            ValidateConcurrencyToken(record.ConcurrencyToken);

            record.PersistenceVersion = envelope.Version;
            return record;
        }
        catch (OpenIddictGroundworkSerializationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new OpenIddictGroundworkSerializationException(
                $"OpenIddict Groundwork document '{envelope.Id}' could not be materialized.",
                exception);
        }
    }

    public static string DocumentKind<TRecord>() where TRecord : OpenIddictGroundworkRecord => typeof(TRecord) switch
    {
        var type when type == typeof(OpenIddictGroundworkApplication) => OpenIddictGroundworkJson.ApplicationDocumentKind,
        var type when type == typeof(OpenIddictGroundworkAuthorization) => OpenIddictGroundworkJson.AuthorizationDocumentKind,
        var type when type == typeof(OpenIddictGroundworkScope) => OpenIddictGroundworkJson.ScopeDocumentKind,
        var type when type == typeof(OpenIddictGroundworkToken) => OpenIddictGroundworkJson.TokenDocumentKind,
        _ => throw new OpenIddictGroundworkSerializationException(
            $"OpenIddict Groundwork record type '{typeof(TRecord).FullName}' is not registered.")
    };

    private static void ValidateIdentity(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new OpenIddictGroundworkSerializationException("An OpenIddict Groundwork record identity is required.");
    }

    private static void ValidateConcurrencyToken(string concurrencyToken)
    {
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new OpenIddictGroundworkSerializationException(
                "An OpenIddict Groundwork record concurrency token is required.");
    }
}
