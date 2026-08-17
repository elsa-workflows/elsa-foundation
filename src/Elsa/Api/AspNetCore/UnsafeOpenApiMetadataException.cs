namespace Elsa.Api.AspNetCore;

/// <summary>Identifies the exact unsafe reference found in completed endpoint metadata.</summary>
public sealed record UnsafeOpenApiMetadataViolation
{
    public UnsafeOpenApiMetadataViolation(
        string owner,
        string? shell,
        int? generation,
        string endpoint,
        OpenApiLifetimeViolationCategory category,
        string artifactIdentity,
        string loadContextIdentity)
    {
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A violation owner is required.", nameof(owner));
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("A violation endpoint is required.", nameof(endpoint));
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "A violation category must be defined.");
        if (artifactIdentity is null)
            throw new ArgumentNullException(nameof(artifactIdentity));
        if (string.IsNullOrWhiteSpace(artifactIdentity))
            throw new ArgumentException("A violation artifact identity is required.", nameof(artifactIdentity));
        if (loadContextIdentity is null)
            throw new ArgumentNullException(nameof(loadContextIdentity));
        if (string.IsNullOrWhiteSpace(loadContextIdentity))
            throw new ArgumentException("A violation load-context identity is required.", nameof(loadContextIdentity));
        if (generation is < 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "A generation must be non-negative when supplied.");

        Owner = owner.Trim();
        Shell = Normalize(shell);
        Generation = generation;
        Endpoint = endpoint.Trim();
        Category = category;
        ArtifactIdentity = artifactIdentity.Trim();
        LoadContextIdentity = loadContextIdentity.Trim();
    }

    public string Owner { get; }
    public string? Shell { get; }
    public int? Generation { get; }
    public string Endpoint { get; }
    public OpenApiLifetimeViolationCategory Category { get; }
    public string ArtifactIdentity { get; }
    public string LoadContextIdentity { get; }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Categories used in deterministic unload-safe OpenAPI diagnostics.</summary>
public enum OpenApiLifetimeViolationCategory
{
    MissingOwnership,
    DuplicateOwnership,
    RequestType,
    ResponseType,
    MetadataObject,
    MemberOrMethod,
    DelegateOrTransformer,
    SerializerMetadata,
    UnknownMetadataShape
}

/// <summary>
/// Thrown before endpoint publication when API Explorer-facing metadata crosses into a collectible
/// implementation generation.
/// </summary>
public sealed class UnsafeOpenApiMetadataException : InvalidOperationException
{
    public UnsafeOpenApiMetadataException(UnsafeOpenApiMetadataViolation violation)
        : this([violation])
    {
    }

    public UnsafeOpenApiMetadataException(IEnumerable<UnsafeOpenApiMetadataViolation> violations)
        : base(BuildMessage(violations, out var ordered))
    {
        Violations = ordered;
    }

    public IReadOnlyList<UnsafeOpenApiMetadataViolation> Violations { get; }

    public UnsafeOpenApiMetadataViolation Violation => Violations.Count == 1
        ? Violations[0]
        : throw new InvalidOperationException("The exception contains more than one violation.");

    private static string BuildMessage(
        IEnumerable<UnsafeOpenApiMetadataViolation> violations,
        out IReadOnlyList<UnsafeOpenApiMetadataViolation> ordered)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var materialized = violations.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one OpenAPI lifetime violation is required.", nameof(violations));

        ordered = materialized
            .OrderBy(violation => violation.Category)
            .ThenBy(violation => violation.Owner, StringComparer.Ordinal)
            .ThenBy(violation => violation.Shell, StringComparer.Ordinal)
            .ThenBy(violation => violation.Generation)
            .ThenBy(violation => violation.Endpoint, StringComparer.Ordinal)
            .ThenBy(violation => violation.ArtifactIdentity, StringComparer.Ordinal)
            .ThenBy(violation => violation.LoadContextIdentity, StringComparer.Ordinal)
            .ToArray();

        var lines = ordered.Select(FormatViolation);
        return "Unsafe OpenAPI metadata validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatViolation(UnsafeOpenApiMetadataViolation violation)
    {
        var shell = violation.Shell is null ? "" : $"; shell='{violation.Shell}'";
        var generation = violation.Generation is null ? "" : $"; generation={violation.Generation.Value}";
        return $"- owner='{violation.Owner}'{shell}{generation}; endpoint='{violation.Endpoint}'; category={violation.Category}; artifact='{violation.ArtifactIdentity}'; loadContext='{violation.LoadContextIdentity}'";
    }
}
