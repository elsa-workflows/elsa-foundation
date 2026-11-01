using System.Text.Json;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>Implementation settings only; the generic activity contract owns public input/result members.</summary>
public sealed record SpecialistManifest(
    string Instructions,
    SpecialistModelReference Model,
    JsonElement Settings,
    IReadOnlyDictionary<string, JsonElement> InputConstraints,
    IReadOnlyDictionary<string, JsonElement> ResultConstraints,
    SpecialistTurnLimits Limits,
    bool AllowContinuity = false);

/// <summary>A logical operational connection name, resolved and authorized in the owning execution scope.</summary>
public sealed record SpecialistModelReference(string ProviderKey, string ConnectionName, string ModelId);

/// <summary>Published upper bounds. Invocation controls may only restrict them further.</summary>
public sealed record SpecialistTurnLimits(int MaximumModelCalls = 8, int TimeoutMilliseconds = 300_000, int? MaximumOutputTokens = null);

/// <summary>Stable provider/consumer identities, independent of the specialist's display name.</summary>
public static class SpecialistRuntimeIds
{
    public const string ProviderKey = "elsa.specialist";
    public const string ManifestSchema = "1";
    public const string ConsumerKey = "elsa.specialist";
    public const string DescriptorSchema = "1";
    public const string SchemaProfile = "elsa.specialist.json/1";
    public const string FacadeCapability = "Elsa.Agent.Specialists.Activities.SpecialistActivity";
    public const string TurnCapability = "Elsa.Agent.Specialists.Activities.SpecialistModelTurn";
}
