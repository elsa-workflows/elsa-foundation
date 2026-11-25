using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Canonical, immutable identities for the exact module preparation reviewed by the user.</summary>
internal static class WeaverInvocationIntegrity
{
    // Retained snapshots are data, not canonical hash bytes. This owner-only wire deviation
    // carries the same enum labels as the hash projection and preserves raw JSON leaves.
    internal static JsonSerializerOptions SnapshotOptions { get; } = CreateSnapshotOptions();

    private static JsonSerializerOptions CreateSnapshotOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
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

    /// <summary>The retained snapshot JSON; carried values remain readable through the shared baseline.</summary>
    public static string Canonical<T>(T value) => JsonSerializer.Serialize(value, SnapshotOptions);

    /// <summary>The digest of recorded snapshot JSON, exactly as <see cref="Digest{T}"/> computes it for the value it was written from.</summary>
    public static string DigestCanonical(string canonical) => Elsa.Serialization.Core.JsonHashInput.HashJson(canonical);

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
