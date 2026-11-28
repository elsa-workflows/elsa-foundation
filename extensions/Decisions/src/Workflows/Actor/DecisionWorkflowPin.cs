using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Decisions.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Decisions.Workflows.Runtime;

/// <summary>Exact publication content embedded in the native executable, with no Design-store dependency.</summary>
internal sealed record DecisionWorkflowPin(string Selection, string TenantId, string AssetId, string VersionId, DecisionArtifact Artifact)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int InputProtectionVersion { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    internal static bool IsWorkflowInput(RuntimeInputBinding binding) =>
        binding.Source is RuntimeInputBindingSource.WorkflowRequest or RuntimeInputBindingSource.WorkflowRequestMember;

    internal static bool RequiresValueProtection(RuntimeInputBinding binding) =>
        RequiresValueProtection(binding.EffectivePolicy) ||
        binding.Literal is { } literal && RequiresValueProtection(literal.Policy);

    internal bool RequiresInputVerification(ExecutableNode node) =>
        node.InputBindings.Values.Any(RequiresValueProtection) ||
        node.InputBindings.Values.Any(IsWorkflowInput) &&
        !ExecutableDecisionPinMetadata.HasVerifiedWorkflowInputs(node.Metadata, Selection);

    private static bool RequiresValueProtection(ValueProtectionPolicy policy) =>
        policy.HidesValue() || policy.RedactionMode is not null || policy.RetentionPolicy is not null ||
        policy.Storage is DurableValueStorage.External or DurableValueStorage.Custom;

    public static DecisionWorkflowPin Parse(string serialized)
    {
        try
        {
            if (serialized.Length > 2 * 1024 * 1024) throw new JsonException();
            var pin = JsonSerializer.Deserialize<DecisionWorkflowPin>(serialized, Options);
            if (pin is null || string.IsNullOrWhiteSpace(pin.Selection) || string.IsNullOrWhiteSpace(pin.TenantId) ||
                string.IsNullOrWhiteSpace(pin.AssetId) || string.IsNullOrWhiteSpace(pin.VersionId) || pin.Artifact is null)
                throw new JsonException();
            var separator = pin.Selection.LastIndexOf('@');
            if (separator <= 0 || separator == pin.Selection.Length - 1 || pin.Selection[..separator] != pin.AssetId)
                throw new JsonException();
            var version = pin.Selection[(separator + 1)..];
            if (version != "latest" && version != pin.VersionId)
                throw new JsonException();
            return pin;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw new DecisionInvocationException("invalid-pin", "The frozen decision reference is unavailable or invalid.", exception);
        }
    }
}

internal sealed class DecisionInvocationException(string code, string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
