using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Canonical, immutable identities for the exact module preparation reviewed by the user.</summary>
internal static class AgentInvocationIntegrity
{
    public static JsonElement Freeze(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new AgentContributionException("A prepared invocation contains undefined JSON.");
        return value.Clone();
    }

    public static AgentPayload Freeze(AgentPayload value)
    {
        if (string.IsNullOrWhiteSpace(value.TypeId) || string.IsNullOrWhiteSpace(value.SchemaVersion))
            throw new AgentContributionException("A module payload must declare its type and schema version.");
        return value with { Data = Freeze(value.Data) };
    }

    public static string Digest<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(value));
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static string PreparedDigest(AgentPreparedToolInvocation value) => Digest(new
    {
        value.SessionId, value.TurnId, value.ToolCallId, value.ToolName, value.ToolContractVersion,
        value.ModuleId, value.ActorId, value.TenantId, value.Target, value.Arguments, value.Risk,
        value.Preview, value.ExecutionLocation, value.RequiresApproval, value.Client, value.DisclosureSourceId
    });

    public static bool SameTarget(AgentResourceTarget? left, AgentResourceTarget? right) => left is null
        ? right is null
        : right is not null && left.ModuleId == right.ModuleId && left.ResourceType == right.ResourceType
          && left.ResourceId == right.ResourceId && left.Revision == right.Revision;

    public static bool ValidReceipt(AgentPreparedToolInvocation invocation, AgentToolResult result, AgentEffectScope scope)
        => result.Succeeded && result.ToolCallId == invocation.ToolCallId && result.ToolName == invocation.ToolName
           && result.Payload is not null && result.Receipt is { } receipt
           && receipt.InvocationId == invocation.InvocationId && receipt.Scope == scope
           && SameTarget(invocation.Target, receipt.Before)
           && receipt.After.ModuleId == invocation.ModuleId && !string.IsNullOrWhiteSpace(receipt.After.ResourceType)
           && receipt.Evidence.Data.ValueKind != JsonValueKind.Undefined;

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Undefined:
                throw new AgentContributionException("Undefined JSON cannot identify a prepared invocation.");
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
