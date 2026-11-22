using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Canonical, immutable identities for the exact module preparation reviewed by the user.</summary>
internal static class WeaverInvocationIntegrity
{
    public static JsonElement Freeze(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new WeaverContributionException("A prepared invocation contains undefined JSON.");
        return value.Clone();
    }

    public static WeaverPayload Freeze(WeaverPayload value)
    {
        if (string.IsNullOrWhiteSpace(value.TypeId) || string.IsNullOrWhiteSpace(value.SchemaVersion))
            throw new WeaverContributionException("A module payload must declare its type and schema version.");
        return value with { Data = Freeze(value.Data) };
    }

    public static string Digest<T>(T value) => Elsa.Serialization.Core.JsonHashInput.Hash(value);

    /// <summary>The canonical JSON text retained with the digest; carried values remain readable through the shared baseline.</summary>
    public static string Canonical<T>(T value) => Encoding.UTF8.GetString(CanonicalBytes(value));

    /// <summary>The digest of recorded canonical JSON text, exactly as <see cref="Digest{T}"/> computes it for the value it was written from.</summary>
    public static string DigestCanonical(string canonical) => Elsa.Serialization.Core.JsonHashInput.HashJson(canonical);

    private static byte[] CanonicalBytes<T>(T value)
    {
        return Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(Elsa.Serialization.Core.JsonHashInput.SerializeToElement(value));
    }

    public static string PreparedDigest(WeaverPreparedToolInvocation value) => Digest(new
    {
        value.SessionId, value.TurnId, value.ToolCallId, value.ToolName, value.ToolContractVersion,
        value.ModuleId, value.ActorId, value.TenantId, value.Target, value.Arguments, value.Risk,
        value.Preview, value.ExecutionLocation, value.RequiresApproval, value.Client, value.DisclosureSourceId
    });

    public static bool SameTarget(WeaverResourceTarget? left, WeaverResourceTarget? right) => left is null
        ? right is null
        : right is not null && left.ModuleId == right.ModuleId && left.ResourceType == right.ResourceType
          && left.ResourceId == right.ResourceId && left.Revision == right.Revision;

    public static bool ValidReceipt(WeaverPreparedToolInvocation invocation, WeaverToolResult result, WeaverEffectScope scope)
        => result.Succeeded && result.ToolCallId == invocation.ToolCallId && result.ToolName == invocation.ToolName
           && result.Payload is not null && result.Receipt is { } receipt
           && receipt.InvocationId == invocation.InvocationId && receipt.Scope == scope
           && SameTarget(invocation.Target, receipt.Before)
           && receipt.After.ModuleId == invocation.ModuleId && !string.IsNullOrWhiteSpace(receipt.After.ResourceType)
           && receipt.Evidence.Data.ValueKind != JsonValueKind.Undefined;

}
