using System.Text.Json;

namespace Elsa.Agent.Core.Models;

/// <summary>Implementation settings only; the generic activity contract owns public input/result members.</summary>
public sealed record AgentManifest(
    string Instructions,
    AgentModelReference Model,
    JsonElement Settings,
    IReadOnlyDictionary<string, JsonElement> InputConstraints,
    IReadOnlyDictionary<string, JsonElement> ResultConstraints,
    AgentTurnLimits Limits,
    bool AllowContinuity = false);

/// <summary>A logical operational connection name, resolved and authorized in the owning execution scope.</summary>
public sealed record AgentModelReference(string ProviderKey, string ConnectionName, string ModelId);

/// <summary>Published upper bounds. Invocation controls may only restrict them further.</summary>
public sealed record AgentTurnLimits(int MaximumModelCalls = 8, int TimeoutMilliseconds = 300_000, int? MaximumOutputTokens = null);

/// <summary>Stable provider/consumer identities, independent of the agent's display name.</summary>
public static class AgentRuntimeIds
{
    public const string ProviderKey = "elsa.agent";
    public const string ManifestSchema = "1";
    public const string ContinuityManifestSchema = "2";
    public const string ConsumerKey = "elsa.agent";
    public const string DescriptorSchema = "1";
    public const string ContinuityDescriptorSchema = "2";
    public const string SchemaProfile = "elsa.agent.json/1";
    public const string FacadeCapability = "Elsa.Agent.Activities.AgentActivity";
    public const string TurnCapability = "Elsa.Agent.Activities.AgentModelTurn";
}
