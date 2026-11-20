using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Decisions.Core;

namespace Elsa.Decisions.Workflows.Runtime;

/// <summary>Exact publication content embedded in the native executable, with no Design-store dependency.</summary>
public sealed record DecisionWorkflowPin(string Selection, string TenantId, string AssetId, string VersionId, DecisionArtifact Artifact)
{
    public const string MetadataKey = "decisions.artifact.v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static DecisionWorkflowPin Parse(string serialized)
    {
        try
        {
            if (serialized.Length > 2 * 1024 * 1024) throw new JsonException();
            var pin = JsonSerializer.Deserialize<DecisionWorkflowPin>(serialized, Options);
            if (pin is null || string.IsNullOrWhiteSpace(pin.Selection) || string.IsNullOrWhiteSpace(pin.TenantId) ||
                string.IsNullOrWhiteSpace(pin.AssetId) || string.IsNullOrWhiteSpace(pin.VersionId) || pin.Artifact is null)
                throw new JsonException();
            return pin;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw new DecisionInvocationException("invalid-pin", "The frozen decision reference is unavailable or invalid.", exception);
        }
    }
}

public sealed class DecisionInvocationException(string code, string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
