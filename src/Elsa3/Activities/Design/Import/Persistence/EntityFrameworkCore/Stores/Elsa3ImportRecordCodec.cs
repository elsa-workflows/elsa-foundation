using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;
using Elsa3.Activities.Design.Import.Services;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Maps import ledger rows to domain values and back. Every read proves the row against its lossless
/// material: the canonical JSON must hash to the stored content hash, and every lookup projection must agree
/// with the decoded residual and with the JSON. Any disagreement is corruption and fails closed.
/// </summary>
internal static class Elsa3ImportRecordCodec
{
    private const string GlobalTenantKey = "g:";
    private const string TenantKeyPrefix = "t:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A provider-safe, domain-separated tenant partition: global scope can never equal a tenant.</summary>
    public static string TenantKey(string? tenantId) =>
        tenantId is null ? GlobalTenantKey : TenantKeyPrefix + EfRelationalIdentity.Hash(tenantId);

    public static string Hash(string value) => EfRelationalIdentity.Hash(value);

    public static Elsa3ImportCollectionRecord ToRecord(ReusableActivityImportCollectionHandle collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        EnsureIdentity(collection.Handle, "collection handle");
        EnsureScope(collection.AccessScope);
        var json = JsonSerializer.Serialize(new CollectionDocument(collection), Json);
        return new Elsa3ImportCollectionRecord
        {
            TenantKey = TenantKey(collection.AccessScope.TenantId),
            UserIdHash = Hash(collection.AccessScope.UserId),
            HandleHash = Hash(collection.Handle),
            Handle = EfRelationalIdentity.Encode(collection.Handle),
            TenantId = EncodeNullable(collection.AccessScope.TenantId),
            UserId = EfRelationalIdentity.Encode(collection.AccessScope.UserId),
            SchemaVersion = Elsa3ImportEfModule.SchemaVersion,
            CreatedAtUtcTicks = collection.CreatedAt.UtcTicks,
            ExpiresAtUtcTicks = collection.ExpiresAt.UtcTicks,
            ContentLength = collection.ContentLength,
            ContentJson = json,
            ContentHash = Hash(json)
        };
    }

    public static ReusableActivityImportCollectionHandle ReadCollection(
        Elsa3ImportCollectionRecord record,
        string handle,
        ReusableActivityImportAccessScope accessScope)
    {
        EnsureEnvelope(record.SchemaVersion, record.ContentJson, record.ContentHash);
        EnsureResidual(record.Handle, handle, "collection handle");
        EnsureScopeResiduals(record.TenantId, record.UserId, accessScope);
        var collection = Deserialize<CollectionDocument>(record.ContentJson).Collection
                         ?? throw new InvalidDataException("The Elsa 3 import collection row has no collection.");
        if (!StringComparer.Ordinal.Equals(collection.Handle, handle) ||
            !SameScope(collection.AccessScope, accessScope) ||
            collection.CreatedAt.UtcTicks != record.CreatedAtUtcTicks ||
            collection.ExpiresAt.UtcTicks != record.ExpiresAtUtcTicks ||
            collection.ContentLength != record.ContentLength)
            throw new InvalidDataException("The Elsa 3 import collection row does not match its canonical content.");
        return collection;
    }

    public static Elsa3ImportReceiptRecord ToRecord(ReusableActivityImportReceipt receipt, string commitAttemptId)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitAttemptId);
        EnsureIdentity(receipt.ReceiptId, "receipt");
        EnsureIdentity(receipt.IdempotencyKey, "idempotency key");
        EnsureScope(receipt.AccessScope);
        if (!StringComparer.Ordinal.Equals(receipt.ReceiptId, ReusableActivityImportIdentity.Receipt(receipt.IdempotencyKey, receipt.AccessScope)))
            throw new ArgumentException("The Elsa 3 import receipt identity is not derived from its key and scope.", nameof(receipt));
        var json = JsonSerializer.Serialize(new ReceiptDocument(receipt), Json);
        return new Elsa3ImportReceiptRecord
        {
            TenantKey = TenantKey(receipt.AccessScope.TenantId),
            UserIdHash = Hash(receipt.AccessScope.UserId),
            ReceiptIdHash = Hash(receipt.ReceiptId),
            ReceiptId = EfRelationalIdentity.Encode(receipt.ReceiptId),
            IdempotencyKey = EfRelationalIdentity.Encode(receipt.IdempotencyKey),
            TenantId = EncodeNullable(receipt.AccessScope.TenantId),
            UserId = EfRelationalIdentity.Encode(receipt.AccessScope.UserId),
            SchemaVersion = Elsa3ImportEfModule.SchemaVersion,
            CompletedAtUtcTicks = receipt.CompletedAt.UtcTicks,
            CommitAttemptId = commitAttemptId,
            ContentJson = json,
            ContentHash = Hash(json)
        };
    }

    public static ReusableActivityImportReceipt ReadReceipt(
        Elsa3ImportReceiptRecord record,
        string receiptId,
        ReusableActivityImportAccessScope accessScope)
    {
        EnsureEnvelope(record.SchemaVersion, record.ContentJson, record.ContentHash);
        EnsureResidual(record.ReceiptId, receiptId, "receipt");
        EnsureScopeResiduals(record.TenantId, record.UserId, accessScope);
        if (string.IsNullOrWhiteSpace(record.CommitAttemptId))
            throw new InvalidDataException("The Elsa 3 import receipt row has no commit attempt.");
        var receipt = Deserialize<ReceiptDocument>(record.ContentJson).Receipt
                      ?? throw new InvalidDataException("The Elsa 3 import receipt row has no receipt.");
        if (!StringComparer.Ordinal.Equals(receipt.ReceiptId, receiptId) ||
            !StringComparer.Ordinal.Equals(receipt.IdempotencyKey, Decode(record.IdempotencyKey, "idempotency key")) ||
            !StringComparer.Ordinal.Equals(ReusableActivityImportIdentity.Receipt(receipt.IdempotencyKey, receipt.AccessScope), receiptId) ||
            !SameScope(receipt.AccessScope, accessScope) ||
            receipt.CompletedAt.UtcTicks != record.CompletedAtUtcTicks)
            throw new InvalidDataException("The Elsa 3 import receipt row does not match its canonical content.");
        return receipt;
    }

    public static Elsa3ImportDefinitionBindingRecord ToRecord(ReusableActivityImportDefinitionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureIdentity(binding.Id, "definition binding");
        EnsureIdentity(binding.TargetDefinitionId, "binding target");
        EnsureIdentity(binding.SourceDefinitionId, "binding source");
        EnsureKind(binding.TargetDocumentKind);
        EnsureKind(binding.SourceKind);
        if (binding.TenantId is not null)
            EnsureIdentity(binding.TenantId, "tenant");
        return new Elsa3ImportDefinitionBindingRecord
        {
            TenantKey = TenantKey(binding.TenantId),
            BindingIdHash = Hash(binding.Id),
            BindingId = EfRelationalIdentity.Encode(binding.Id),
            TargetDocumentKind = binding.TargetDocumentKind,
            TargetDefinitionIdHash = Hash(binding.TargetDefinitionId),
            TargetDefinitionId = EfRelationalIdentity.Encode(binding.TargetDefinitionId),
            SourceKind = binding.SourceKind,
            SourceDefinitionId = EfRelationalIdentity.Encode(binding.SourceDefinitionId),
            TenantId = EncodeNullable(binding.TenantId),
            SchemaVersion = Elsa3ImportEfModule.SchemaVersion,
            CreatedAtUtcTicks = binding.CreatedAt.UtcTicks,
            CreatedAtOffsetMinutes = checked((int)binding.CreatedAt.Offset.TotalMinutes),
            ContentHash = BindingHash(binding)
        };
    }

    public static ReusableActivityImportDefinitionBinding ReadBinding(
        Elsa3ImportDefinitionBindingRecord record,
        string bindingId,
        string? tenantId)
    {
        if (!StringComparer.Ordinal.Equals(record.SchemaVersion, Elsa3ImportEfModule.SchemaVersion))
            throw new InvalidDataException($"The Elsa 3 import definition binding row has unsupported schema version '{record.SchemaVersion}'.");
        EnsureResidual(record.BindingId, bindingId, "definition binding");
        if (!StringComparer.Ordinal.Equals(DecodeNullable(record.TenantId, "tenant"), tenantId) ||
            !StringComparer.Ordinal.Equals(record.TenantKey, TenantKey(tenantId)))
            throw new InvalidDataException("The Elsa 3 import definition binding row belongs to a different tenant partition.");
        DateTimeOffset createdAt;
        try
        {
            createdAt = new DateTimeOffset(record.CreatedAtUtcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(record.CreatedAtOffsetMinutes));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The Elsa 3 import definition binding row has an invalid timestamp.", exception);
        }

        var binding = new ReusableActivityImportDefinitionBinding(
            bindingId,
            record.TargetDocumentKind,
            Decode(record.TargetDefinitionId, "binding target"),
            record.SourceKind,
            Decode(record.SourceDefinitionId, "binding source"),
            tenantId,
            createdAt);
        if (!StringComparer.Ordinal.Equals(record.TargetDefinitionIdHash, Hash(binding.TargetDefinitionId)) ||
            !StringComparer.Ordinal.Equals(ReusableActivityImportIdentity.DefinitionBinding(binding.TargetDocumentKind, binding.TargetDefinitionId), bindingId) ||
            !StringComparer.Ordinal.Equals(record.ContentHash, BindingHash(binding)))
            throw new InvalidDataException("The Elsa 3 import definition binding row does not match its integrity hash.");
        return binding;
    }

    private static string BindingHash(ReusableActivityImportDefinitionBinding binding) =>
        EfRelationalIdentity.HashLengthFramed(
            binding.Id,
            binding.TargetDocumentKind,
            binding.TargetDefinitionId,
            binding.SourceKind,
            binding.SourceDefinitionId,
            binding.TenantId is null ? GlobalTenantKey : TenantKeyPrefix + binding.TenantId,
            binding.CreatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CreatedAt.Offset.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void EnsureEnvelope(string schemaVersion, string contentJson, string contentHash)
    {
        if (!StringComparer.Ordinal.Equals(schemaVersion, Elsa3ImportEfModule.SchemaVersion))
            throw new InvalidDataException($"The Elsa 3 import row has unsupported schema version '{schemaVersion}'.");
        if (string.IsNullOrEmpty(contentJson) || !StringComparer.Ordinal.Equals(contentHash, Hash(contentJson)))
            throw new InvalidDataException("The Elsa 3 import row content does not match its content hash.");
    }

    private static void EnsureScopeResiduals(string? encodedTenantId, string encodedUserId, ReusableActivityImportAccessScope accessScope)
    {
        if (!StringComparer.Ordinal.Equals(DecodeNullable(encodedTenantId, "tenant"), accessScope.TenantId) ||
            !StringComparer.Ordinal.Equals(Decode(encodedUserId, "user"), accessScope.UserId))
            throw new InvalidDataException("The Elsa 3 import row belongs to a different operation scope than its lookup partition.");
    }

    private static void EnsureResidual(string encoded, string expected, string field)
    {
        if (!StringComparer.Ordinal.Equals(Decode(encoded, field), expected))
            throw new InvalidDataException($"The Elsa 3 import {field} projection does not match its lookup hash.");
    }

    private static void EnsureScope(ReusableActivityImportAccessScope accessScope)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        EnsureIdentity(accessScope.UserId, "user");
        if (accessScope.TenantId is not null)
            EnsureIdentity(accessScope.TenantId, "tenant");
    }

    private static void EnsureIdentity(string value, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, field);
        if (value.Length > Elsa3ImportEfModule.IdentityMaximumLength)
            throw new ArgumentException($"The Elsa 3 import {field} cannot exceed {Elsa3ImportEfModule.IdentityMaximumLength} UTF-16 code units.", field);
    }

    private static void EnsureKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > Elsa3ImportEfModule.KindMaximumLength)
            throw new ArgumentException($"The Elsa 3 import kind cannot exceed {Elsa3ImportEfModule.KindMaximumLength} characters.", nameof(value));
    }

    private static string? EncodeNullable(string? value) => value is null ? null : EfRelationalIdentity.Encode(value);

    private static string Decode(string encoded, string field)
    {
        try
        {
            return EfRelationalIdentity.Decode(encoded);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"The Elsa 3 import {field} projection is not a valid encoded identity.", exception);
        }
    }

    private static string? DecodeNullable(string? encoded, string field) => encoded is null ? null : Decode(encoded, field);

    private static bool SameScope(ReusableActivityImportAccessScope left, ReusableActivityImportAccessScope right) =>
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        StringComparer.Ordinal.Equals(left.UserId, right.UserId);

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("The Elsa 3 import row content is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Elsa 3 import row content is not valid JSON.", exception);
        }
    }

    // The document wrappers keep the canonical JSON shape identical to the Groundwork documents.
    private sealed record CollectionDocument(ReusableActivityImportCollectionHandle Collection);
    private sealed record ReceiptDocument(ReusableActivityImportReceipt Receipt);
}
