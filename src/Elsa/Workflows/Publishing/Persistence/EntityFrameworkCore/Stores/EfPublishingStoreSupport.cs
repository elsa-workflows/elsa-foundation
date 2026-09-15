using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

internal static class EfPublishingStoreSupport
{
    public const int IdentityMaximumLength = PublishingPolicyProjectionEfModule.IdentityMaximumLength;

    public static string? TenantValue(IPersistenceAccessContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        var current = accessor.Current ?? throw new InvalidOperationException("Publishing persistence access context is missing.");
        if (current.AcrossScopes)
            throw new InvalidOperationException("Publishing policy and projection-intent stores require an explicit global or scoped persistence access context.");
        var tenantId = current.Scope?.Value;
        if (tenantId is not null)
            EnsureIdentity(tenantId, nameof(tenantId));
        return tenantId;
    }

    public static string TenantHash(string? tenantId) => EfRelationalIdentity.Hash(tenantId ?? "");

    public static string Hash(string value) => EfRelationalIdentity.Hash(value);

    public static string Encode(string value) => EfRelationalIdentity.Encode(value);

    public static string? EncodeNullable(string? value) => value is null ? null : Encode(value);

    public static string DecodeIdentity(string encoded, string field) =>
        DecodeValue(encoded, IdentityMaximumLength, field);

    public static string? DecodeNullableIdentity(string? encoded, string field) =>
        encoded is null ? null : DecodeIdentity(encoded, field);

    public static string DecodeValue(string encoded, int maximumLength, string field)
    {
        EnsurePersistedValue(encoded, EncodedMaximumLength(maximumLength), field);
        string value;
        try
        {
            value = EfRelationalIdentity.Decode(encoded);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Malformed persisted publication state: {field} is not a valid encoded value.", exception);
        }

        EnsureValue(value, maximumLength, field);
        return value;
    }

    public static int EncodedMaximumLength(int maximumLength) =>
        checked(((maximumLength * sizeof(char) + 2) / 3) * 4);

    public static byte[] OrderKey(string value)
    {
        EnsureIdentity(value, nameof(value));
        return EfRelationalIdentity.CreateOrderKey(value, IdentityMaximumLength);
    }

    public static string PolicyKey(string? workflowDefinitionId)
    {
        if (workflowDefinitionId is null)
            return "host";

        EnsureIdentity(workflowDefinitionId, nameof(workflowDefinitionId));
        // The framing is deliberate: a definition called "host" cannot collide with the host sentinel,
        // and embedded separators cannot forge another definition identity.
        return $"workflow:{workflowDefinitionId.Length}:{workflowDefinitionId}";
    }

    public static string PhysicalId(string? tenantId, string logicalId) =>
        EfRelationalIdentity.HashLengthFramed(tenantId ?? "", logicalId);

    public static void EnsureIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > IdentityMaximumLength)
            throw new ArgumentException($"The identity cannot exceed {IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    public static void EnsureValue(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} UTF-16 code units.", parameterName);
    }

    public static void EnsurePersistedValue(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new InvalidOperationException($"Malformed persisted publication state: {field} is missing or exceeds its provider-safe bound.");
    }

    public static DateTimeOffset DateTimeOffset(long utcTicks, int offsetMinutes)
    {
        try
        {
            return new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException("The row contains an invalid DateTimeOffset projection.", exception);
        }
    }

    public static (long UtcTicks, int OffsetMinutes) DateTimeOffsetParts(DateTimeOffset value) =>
        (value.UtcTicks, checked((int)value.Offset.TotalMinutes));

    public static void EnsureProjection(string value, string hash, byte[] orderKey, string field)
    {
        EnsureIdentity(value, field);
        if (orderKey is null || !StringComparer.Ordinal.Equals(hash, Hash(value)) || !orderKey.AsSpan().SequenceEqual(OrderKey(value)))
            throw new InvalidOperationException($"The persisted publication {field} identity projection is corrupt.");
    }

    public static void EnsureHash(string value, string hash, string field)
    {
        EnsureIdentity(value, field);
        if (!StringComparer.Ordinal.Equals(hash, Hash(value)))
            throw new InvalidOperationException($"The persisted publication {field} hash projection is corrupt.");
    }
}
