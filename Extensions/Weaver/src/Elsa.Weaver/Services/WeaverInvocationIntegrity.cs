using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
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

    public static string Digest<T>(T value) => DigestCanonical(CanonicalBytes(value));

    /// <summary>The canonical JSON text (sorted keys, raw scalars) that <see cref="Digest{T}"/> is taken over.</summary>
    public static string Canonical<T>(T value) => Encoding.UTF8.GetString(CanonicalBytes(value));

    /// <summary>The digest of recorded canonical JSON text, exactly as <see cref="Digest{T}"/> computes it for the value it was written from.</summary>
    public static string DigestCanonical(string canonical) => DigestCanonical(Encoding.UTF8.GetBytes(canonical));

    private static string DigestCanonical(byte[] canonical) => Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();

    private static byte[] CanonicalBytes<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(value));
        return stream.ToArray();
    }

    /// <summary>Normalizes only a newly returned module preparation, never a retained legacy invocation.</summary>
    public static IReadOnlyList<WeaverPrivilegeClass>? PreparePrivilegeClasses(IReadOnlyList<WeaverPrivilegeClass>? classes, WeaverRisk effectiveRisk)
    {
        if (!Enum.IsDefined(effectiveRisk) || classes?.Any(value => !Enum.IsDefined(value)) == true)
            throw new WeaverContributionException("The preparation contains an unsupported privilege classification.");
        var required = classes?.ToHashSet() ?? [];
        if (effectiveRisk == WeaverRisk.Admin) required.Add(WeaverPrivilegeClass.Administrative);
        return required.Count == 0 ? null : Array.AsReadOnly(required.Order().ToArray());
    }

    public static string PreparedDigest(WeaverPreparedToolInvocation value) => value.RequiredPrivilegeClasses is not { Count: > 0 }
        // Exact historical shape: even a null new member would change an already reviewed legacy digest.
        ? Digest(new
        {
            value.SessionId, value.TurnId, value.ToolCallId, value.ToolName, value.ToolContractVersion,
            value.ModuleId, value.ActorId, value.TenantId, value.Target, value.Arguments, value.Risk,
            value.Preview, value.ExecutionLocation, value.RequiresApproval, value.Client, value.DisclosureSourceId
        })
        : Digest(new
        {
            value.SessionId, value.TurnId, value.ToolCallId, value.ToolName, value.ToolContractVersion,
            value.ModuleId, value.ActorId, value.TenantId, value.Target, value.Arguments, value.Risk,
            value.Preview, value.ExecutionLocation, value.RequiresApproval, value.Client, value.DisclosureSourceId,
            value.RequiredPrivilegeClasses
        });

    public static bool SameTarget(WeaverResourceTarget? left, WeaverResourceTarget? right) => left is null
        ? right is null
        : right is not null && left.ModuleId == right.ModuleId && left.ResourceType == right.ResourceType
          && left.ResourceId == right.ResourceId && left.Revision == right.Revision;

    public static bool ValidReceipt(WeaverPreparedToolInvocation invocation, WeaverToolResult result, WeaverEffectScope scope)
        => result.Succeeded && result.Error is null && result.ToolCallId == invocation.ToolCallId && result.ToolName == invocation.ToolName
           && result.Payload is not null && result.Receipt is { } receipt
           && receipt.InvocationId == invocation.InvocationId && receipt.Scope == scope
           && SameTarget(invocation.Target, receipt.Before)
           && receipt.After is { } after && after.ModuleId == invocation.ModuleId && !string.IsNullOrWhiteSpace(after.ResourceType)
           && receipt.Evidence is { } evidence && evidence.Data.ValueKind != JsonValueKind.Undefined;

    /// <summary>Derives an accepted outcome from this dispatch's actual admitted contract, never a later lookup.</summary>
    internal static string? SuccessStatus(WeaverPreparedToolInvocation invocation, WeaverToolResult result, WeaverToolDescriptor descriptor)
    {
        try
        {
            if (!ValidPayload(result.Payload) || !ValidPayload(result.Receipt?.Evidence)
                || descriptor.Name != invocation.ToolName || descriptor.ModuleId != invocation.ModuleId
                || descriptor.ContractVersion != invocation.ToolContractVersion || descriptor.ExecutionLocation != invocation.ExecutionLocation)
                return null;
            if (invocation.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState)
                return ValidReceipt(invocation, result, WeaverEffectScope.ClientWorkingState) ? "executed" : null;
            if (invocation.ExecutionLocation != WeaverExecutionLocation.Server) return null;
            if (ValidReceipt(invocation, result, WeaverEffectScope.ServerPersisted)) return "executed";
            return descriptor.CanPerformWithoutPersistence && ValidReceipt(invocation, result, WeaverEffectScope.NoPersistedEffect)
                && SameTarget(result.Receipt!.Before, result.Receipt.After) ? "performed" : null;
        }
        catch (InvalidOperationException) { return null; } // Disposed/invalid owner JSON is not an admitted receipt.
    }

    internal static bool ValidSuccessOutcome(WeaverActionProposal proposal, WeaverProposalExecutionResult outcome, WeaverToolDescriptor descriptor)
        => proposal.PreparedInvocation is { } prepared && outcome.Executed && outcome.ClientInvocation is null
            && outcome.ProposalId == proposal.Id && outcome.ResourceType == (prepared.Target?.ResourceType ?? proposal.ResourceType ?? "unavailable")
            && outcome.ResourceId == (prepared.Target?.ResourceId ?? proposal.ResourceId ?? string.Empty)
            && outcome.ToolResult is { } result && SuccessStatus(prepared, result, descriptor) is { } status && outcome.Status == status;

    private static bool ValidPayload(WeaverPayload? payload) => payload is not null
        && !string.IsNullOrWhiteSpace(payload.TypeId) && !string.IsNullOrWhiteSpace(payload.SchemaVersion)
        && payload.Data.ValueKind != JsonValueKind.Undefined;

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
                throw new WeaverContributionException("Undefined JSON cannot identify a prepared invocation.");
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
