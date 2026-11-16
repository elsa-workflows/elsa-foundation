using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Secrets.Core.Contracts;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// Protects every payload column with the selected Secrets protector. A payload is bound to its purpose and to the
/// identity of the row that holds it, so ciphertext moved to another row, session or column does not authenticate.
/// Any failure to authenticate or decode is an <see cref="InvalidDataException"/> carrying the original cause.
/// </summary>
internal sealed class WeaverRecordCodec(ISecretValueProtector protector)
{
    // Default options keep the canonical shape the owner's integrity digests read, exactly as the default store does.
    private static readonly JsonSerializerOptions ValueOptions = new();
    private static readonly JsonSerializerOptions EnvelopeOptions = new() { MaxDepth = 8 };

    public const string SessionPurpose = "elsa.weaver.session/1";
    public const string PausedTurnPurpose = "elsa.weaver.paused-turn/1";
    public const string MessagePurpose = "elsa.weaver.message/1";
    public const string SnapshotPurpose = "elsa.weaver.snapshot/1";
    public const string ContextPurpose = "elsa.weaver.context/1";
    public const string ProposalPurpose = "elsa.weaver.proposal/1";
    public const string AuditPurpose = "elsa.weaver.audit/1";
    public const string PendingAuditsPurpose = "elsa.weaver.pending-audits/1";

    /// <summary>Serializes <paramref name="value"/> and protects it for one row. A value that cannot be serialized throws before anything is written.</summary>
    public string Protect<T>(string purpose, string owner, string id, T value) => ProtectText(purpose, owner, id, JsonSerializer.Serialize(value, ValueOptions));

    /// <summary>Protects <paramref name="text"/> verbatim for one row; unprotecting returns the identical string.</summary>
    public string ProtectText(string purpose, string owner, string id, string text)
        => protector.Protect(JsonSerializer.Serialize(new Envelope(purpose, owner, id, text), EnvelopeOptions));

    public T Unprotect<T>(string purpose, string owner, string id, string protectedValue)
    {
        var text = UnprotectText(purpose, owner, id, protectedValue);
        try
        {
            return JsonSerializer.Deserialize<T>(text, ValueOptions) ?? throw new InvalidDataException($"A recorded {typeof(T).Name} is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"A recorded {typeof(T).Name} cannot be read.", exception);
        }
    }

    public string UnprotectText(string purpose, string owner, string id, string protectedValue)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(protector.Unprotect(protectedValue), EnvelopeOptions);
        }
        // A protector may be any Secrets implementation; whatever it throws for unauthenticated input is unreadable state.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("A protected Weaver record cannot be authenticated with the configured keys.", exception);
        }
        if (envelope is null || envelope.Purpose != purpose || envelope.Owner != owner || envelope.Id != id || envelope.Value is null)
            throw new InvalidDataException("A protected Weaver record does not belong to the row that holds it.");
        return envelope.Value;
    }

    /// <summary>The lowercase hexadecimal SHA-256 of an identity used as a bounded key column.</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Envelope(string Purpose, string Owner, string Id, string Value);
}
