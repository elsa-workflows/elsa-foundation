using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Persistence.Groundwork.Testing;

public sealed record GroundworkScenarioObservation
{
    public GroundworkScenarioObservation(string name, string value)
    {
        Name = RequireValue(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; }
    public string Value { get; }

    private static string RequireValue(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A nonblank value is required.", parameterName);
}

public sealed record GroundworkCompositionFingerprint
{
    private GroundworkCompositionFingerprint(string value) => Value = value;

    public string Value { get; }

    public static GroundworkCompositionFingerprint Create(string canonicalComposition) =>
        new(StableDigest.FromText(RequireValue(canonicalComposition, nameof(canonicalComposition))));

    public static GroundworkCompositionFingerprint Parse(string value)
    {
        if (!EvidenceCatalog.IsSha256(value))
            throw new ArgumentException("A composition fingerprint must be a lowercase SHA-256 digest.", nameof(value));
        return new GroundworkCompositionFingerprint(value);
    }

    public override string ToString() => Value;

    private static string RequireValue(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A nonblank value is required.", parameterName);
}

public sealed record GroundworkExecutionPath
{
    private GroundworkExecutionPath(string value) => Value = value;

    public string Value { get; }

    public static GroundworkExecutionPath Create(
        string providerKey,
        string scenarioId,
        GroundworkCompositionFingerprint compositionFingerprint)
    {
        EvidenceCatalog.EnsureProviderKey(providerKey);
        EvidenceCatalog.EnsureSlug(scenarioId, nameof(scenarioId));
        ArgumentNullException.ThrowIfNull(compositionFingerprint);
        return new GroundworkExecutionPath(
            $"provider-driver/{providerKey}/{scenarioId}/{compositionFingerprint.Value[..16]}");
    }

    public static GroundworkExecutionPath Parse(string value)
    {
        if (!EvidenceCatalog.ExecutionPathRegex().IsMatch(value))
            throw new ArgumentException("The execution path is not a closed provider-driver catalog value.", nameof(value));
        return new GroundworkExecutionPath(value);
    }

    public override string ToString() => Value;
}

public sealed record GroundworkSanitizedEvidence
{
    private GroundworkSanitizedEvidence(string kind, string content)
    {
        Kind = kind;
        Content = content;
        Sha256 = StableDigest.FromText(content);
    }

    public string Kind { get; }
    public string Content { get; }
    public string Sha256 { get; }

    public static GroundworkSanitizedEvidence Create(string kind, string content)
    {
        EvidenceCatalog.EnsureSlug(kind, nameof(kind));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Evidence content cannot be blank.", nameof(content));
        if (EvidenceCatalog.SecretAssignmentRegex().IsMatch(content) ||
            EvidenceCatalog.ConnectionAssignmentRegex().IsMatch(content) ||
            EvidenceCatalog.ConnectionUriRegex().IsMatch(content))
            throw new ArgumentException("Evidence contains a connection value, credential, or secret assignment.", nameof(content));
        if (EvidenceCatalog.TenantMetricLabelRegex().IsMatch(content))
            throw new ArgumentException("Evidence contains a tenant identifier as a metric label.", nameof(content));
        return new GroundworkSanitizedEvidence(kind, content);
    }
}

public sealed record GroundworkNativePlanEvidence
{
    private GroundworkNativePlanEvidence(GroundworkExecutionPath executionPath, string artifactPath, GroundworkSanitizedEvidence evidence)
    {
        ExecutionPath = executionPath;
        ArtifactPath = artifactPath;
        Evidence = evidence;
    }

    public GroundworkExecutionPath ExecutionPath { get; }
    public string ArtifactPath { get; }
    public GroundworkSanitizedEvidence Evidence { get; }
    public string EvidenceSha256 => Evidence.Sha256;

    public static GroundworkNativePlanEvidence Create(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        GroundworkSanitizedEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(executionPath);
        ArgumentNullException.ThrowIfNull(evidence);
        EvidenceCatalog.EnsureSlug(scenarioId, nameof(scenarioId));
        var pathSegments = executionPath.Value.Split('/');
        if (!string.Equals(pathSegments[2], scenarioId, StringComparison.Ordinal))
            throw new ArgumentException("Native evidence must use the execution-path scenario.", nameof(scenarioId));
        var providerKey = executionPath.Value.Split('/')[1];
        var suffix = executionPath.Value.Split('/')[3];
        return new GroundworkNativePlanEvidence(
            executionPath,
            $"evidence/{providerKey}/{scenarioId}/{suffix}.plan.txt",
            evidence);
    }
}

public enum GroundworkScenarioOutcome
{
    Pass,
    ClassifiedReadinessFailure
}

public sealed record GroundworkScenarioResult
{
    private GroundworkScenarioResult(
        string scenarioId,
        string coverageEntryId,
        GroundworkProviderDescriptor provider,
        GroundworkCompositionFingerprint compositionFingerprint,
        GroundworkExecutionPath executionPath,
        int clients,
        IReadOnlyList<GroundworkScenarioObservation> observations,
        GroundworkScenarioOutcome outcome,
        string? failureWindow,
        GroundworkNativePlanEvidence? nativeEvidence,
        string resultDigest)
    {
        ScenarioId = scenarioId;
        CoverageEntryId = coverageEntryId;
        Provider = provider;
        CompositionFingerprint = compositionFingerprint;
        ExecutionPath = executionPath;
        Clients = clients;
        Observations = observations;
        Outcome = outcome;
        FailureWindow = failureWindow;
        NativeEvidence = nativeEvidence;
        ResultDigest = resultDigest;
    }

    public string ScenarioId { get; }
    public string CoverageEntryId { get; }
    public GroundworkProviderDescriptor Provider { get; }
    public GroundworkCompositionFingerprint CompositionFingerprint { get; }
    public GroundworkExecutionPath ExecutionPath { get; }
    public int Clients { get; }
    public IReadOnlyList<GroundworkScenarioObservation> Observations { get; }
    public GroundworkScenarioOutcome Outcome { get; }
    public string? FailureWindow { get; }
    public GroundworkNativePlanEvidence? NativeEvidence { get; }
    public string ResultDigest { get; }

    public static GroundworkScenarioResult Create(
        string scenarioId,
        string coverageEntryId,
        GroundworkProviderDescriptor provider,
        GroundworkCompositionFingerprint compositionFingerprint,
        string executionPath,
        int clients,
        IEnumerable<GroundworkScenarioObservation> observations,
        GroundworkScenarioOutcome outcome,
        string? failureWindow = null,
        GroundworkNativePlanEvidence? nativeEvidence = null)
    {
        EvidenceCatalog.EnsureSlug(scenarioId, nameof(scenarioId));
        EvidenceCatalog.EnsureSlug(coverageEntryId, nameof(coverageEntryId));
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(compositionFingerprint);
        if (clients < 1)
            throw new ArgumentOutOfRangeException(nameof(clients), "At least one independent client is required.");
        if (failureWindow is not null)
            EvidenceCatalog.EnsureSlug(failureWindow, nameof(failureWindow));

        var parsedPath = GroundworkExecutionPath.Parse(executionPath);
        var expectedPath = GroundworkExecutionPath.Create(provider.ProviderKey, scenarioId, compositionFingerprint);
        if (parsedPath != expectedPath)
            throw new ArgumentException(
                "The execution path must match the result provider, scenario, and composition fingerprint.",
                nameof(executionPath));
        if (nativeEvidence is not null)
        {
            if (nativeEvidence.ExecutionPath != parsedPath)
                throw new ArgumentException("Native evidence must use the result execution path.", nameof(nativeEvidence));
            var expectedArtifact = $"evidence/{provider.ProviderKey}/{scenarioId}/{compositionFingerprint.Value[..16]}.plan.txt";
            if (!string.Equals(nativeEvidence.ArtifactPath, expectedArtifact, StringComparison.Ordinal))
                throw new ArgumentException("Native evidence must match the result provider, scenario, and composition fingerprint.", nameof(nativeEvidence));
        }

        ArgumentNullException.ThrowIfNull(observations);
        var orderedObservations = observations
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedObservations.Length == 0)
            throw new ArgumentException("At least one scenario observation is required.", nameof(observations));
        if (orderedObservations.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != orderedObservations.Length)
            throw new ArgumentException("Observation names must be unique within a scenario result.", nameof(observations));

        var digestInput = string.Join(
            '\n',
            new[] { scenarioId, coverageEntryId, OutcomeCatalogValue(outcome), failureWindow ?? "-" }
                .Concat(orderedObservations.Select(x => $"{x.Name.Length}:{x.Name}{x.Value.Length}:{x.Value}")));

        return new GroundworkScenarioResult(
            scenarioId,
            coverageEntryId,
            provider,
            compositionFingerprint,
            parsedPath,
            clients,
            orderedObservations,
            outcome,
            failureWindow,
            nativeEvidence,
            StableDigest.FromText(digestInput));
    }

    private static string OutcomeCatalogValue(GroundworkScenarioOutcome outcome) => outcome switch
    {
        GroundworkScenarioOutcome.Pass => "pass",
        GroundworkScenarioOutcome.ClassifiedReadinessFailure => "classified-readiness-failure",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}

internal static partial class EvidenceCatalog
{
    private static readonly HashSet<string> ProviderKeys = ["sqlite", "sqlserver", "postgresql", "mongodb"];

    public static void EnsureProviderKey(string value)
    {
        if (!ProviderKeys.Contains(value))
            throw new ArgumentException($"Unknown provider catalog key '{value}'.", nameof(value));
    }

    public static void EnsureProviderIdentity(string value)
    {
        if (value is not ("groundwork-sqlite" or "groundwork-sqlserver" or "groundwork-postgresql" or "groundwork-mongodb"))
            throw new ArgumentException($"Unknown Groundwork provider identity '{value}'.", nameof(value));
    }

    public static void EnsureSlug(string value, string parameterName)
    {
        if (!SlugRegex().IsMatch(value))
            throw new ArgumentException("A lowercase kebab-case catalog value is required.", parameterName);
    }

    public static bool IsSha256(string value) => Sha256Regex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^provider-driver/(sqlite|sqlserver|postgresql|mongodb)/[a-z0-9-]+/[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    public static partial Regex ExecutionPathRegex();

    [GeneratedRegex(@"\b(password|pwd|user\s*id|uid|username|account\s*key|secret|token)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"\b(server|host|data\s*source|initial\s*catalog|database|endpoint)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex ConnectionAssignmentRegex();

    [GeneratedRegex(@"\b(?:mongodb(?:\+srv)?|postgres(?:ql)?|sqlserver)://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex ConnectionUriRegex();

    [GeneratedRegex(@"[{,]\s*tenant(?:[._-]?id)?\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TenantMetricLabelRegex();
}

internal static class StableDigest
{
    public static string FromText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
