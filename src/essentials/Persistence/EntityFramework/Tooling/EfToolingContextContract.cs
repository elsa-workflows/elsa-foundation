using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>The closed host-operation protocol used only with a host-owned configuration context.</summary>
internal static class EfToolingContextContract
{
    public const int Version = 2;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Version 2 does not accept caller-supplied host, shell, or capability assertions.</summary>
internal sealed class EfToolingContextRequest
{
    public int? Version { get; init; }
    public string? Command { get; init; }
    public EfToolingSelection? Selection { get; init; }
    public string? Provider { get; init; }
    public string? Schema { get; init; }
    public string? Output { get; init; }
    public EfToolingEngineFacts? Engine { get; init; }
    public IReadOnlyList<EfToolingPackageFacts>? Packages { get; init; }
    public string? Connection { get; init; }
    public string? Resource { get; init; }
}

internal sealed class EfToolingContextResponse
{
    public int Version { get; init; } = EfToolingContextContract.Version;
    public string Status { get; init; } = "ok";
    public int ExitCode { get; init; }
    public string? Command { get; init; }
    public EfToolingInspectContextPayload? InspectContext { get; init; }
    public EfToolingListPayload? List { get; init; }
    public EfToolingPlanPayload? Plan { get; init; }
    public EfToolingScriptPayload? Script { get; init; }
    public EfToolingApplyPayload? Apply { get; init; }
    public EfToolingValidatePayload? Validate { get; init; }
    public EfToolingPostMigratePayload? PostMigrate { get; init; }
    public EfToolingConfigurationContextFacts? ConfigurationContext { get; init; }
    public EfToolingErrorPayload? Error { get; init; }
}

internal sealed class EfToolingInspectContextPayload
{
    public required string Outcome { get; init; }
}

internal sealed class EfToolingConfigurationContextFacts
{
    public required string Source { get; init; }
    public required string Environment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Shell { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Resource { get; init; }
    public required string Resolution { get; init; }
    public string TargetVerification { get; init; } = "not-performed";
    public string RuntimeParity { get; init; } = "unobserved";
    public IReadOnlyList<EfToolingContextParticipant> Participants { get; init; } = [];
    public IReadOnlyList<string> Unresolved { get; init; } = [];
}

internal sealed class EfToolingContextParticipant
{
    public required string Feature { get; init; }
    public required string Module { get; init; }
    public string? Resource { get; init; }
    public string? Provider { get; init; }
    public string? ConnectionReference { get; init; }
    public required string Selection { get; init; }
}
