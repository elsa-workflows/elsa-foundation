using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

/// <summary>
/// A provider-independent public observation produced by an Identity scenario.
/// </summary>
public sealed record AspNetCoreIdentityScenarioObservation
{
    private AspNetCoreIdentityScenarioObservation(string name, string value)
    {
        Name = ScenarioResultCatalog.RequireSlug(name, nameof(name));
        Value = ScenarioResultCatalog.RequireSafeValue(value, nameof(value));
    }

    public string Name { get; }
    public string Value { get; }

    public static AspNetCoreIdentityScenarioObservation Text(string name, string value) => new(name, value);

    public static AspNetCoreIdentityScenarioObservation Count(string name, int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "An observation count cannot be negative.");

        return new(name, value.ToString(CultureInfo.InvariantCulture));
    }

    public static AspNetCoreIdentityScenarioObservation Flag(string name, bool value) =>
        new(name, value ? "true" : "false");

    public static AspNetCoreIdentityScenarioObservation Timestamp(string name, DateTimeOffset? value) =>
        new(name, value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-");

    public static AspNetCoreIdentityScenarioObservation OrderedValues(string name, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ordered = values
            .Select(value => ScenarioResultCatalog.RequireSafeValue(value, nameof(values)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new(name, string.Concat(ordered.Select(ScenarioResultCatalog.LengthPrefix)));
    }

    public override string ToString() => $"{Name}={Value}";
}

/// <summary>
/// The sanitized, provider-independent outcome of a complete Identity scenario.
/// </summary>
public sealed record AspNetCoreIdentityScenarioResult
{
    private AspNetCoreIdentityScenarioResult(
        string scenarioId,
        IReadOnlyList<AspNetCoreIdentityScenarioObservation> observations,
        string canonicalFingerprint)
    {
        ScenarioId = scenarioId;
        Observations = observations;
        CanonicalFingerprint = canonicalFingerprint;
    }

    public string ScenarioId { get; }
    public IReadOnlyList<AspNetCoreIdentityScenarioObservation> Observations { get; }
    public string CanonicalFingerprint { get; }

    public static AspNetCoreIdentityScenarioResult Create(
        string scenarioId,
        IEnumerable<AspNetCoreIdentityScenarioObservation> observations)
    {
        ScenarioResultCatalog.RequireSlug(scenarioId, nameof(scenarioId));
        ArgumentNullException.ThrowIfNull(observations);

        var ordered = observations
            .Select(observation => observation ?? throw new ArgumentException(
                "Scenario observations cannot contain null entries.",
                nameof(observations)))
            .OrderBy(observation => observation.Name, StringComparer.Ordinal)
            .ThenBy(observation => observation.Value, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 0)
            throw new ArgumentException("At least one scenario observation is required.", nameof(observations));
        if (ordered.Select(observation => observation.Name).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Observation names must be unique within a scenario result.", nameof(observations));

        var canonical = ScenarioResultCatalog.LengthPrefix(scenarioId) + string.Concat(
            ordered.Select(observation =>
                ScenarioResultCatalog.LengthPrefix(observation.Name) +
                ScenarioResultCatalog.LengthPrefix(observation.Value)));

        return new(
            scenarioId,
            Array.AsReadOnly(ordered),
            FingerprintText(canonical));
    }

    public bool TryGetObservation(string name, out AspNetCoreIdentityScenarioObservation? observation)
    {
        ScenarioResultCatalog.RequireSlug(name, nameof(name));
        observation = Observations.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return observation is not null;
    }

    public AspNetCoreIdentityScenarioObservation GetObservation(string name) =>
        TryGetObservation(name, out var observation)
            ? observation!
            : throw new KeyNotFoundException($"Scenario '{ScenarioId}' has no '{name}' observation.");

    public override string ToString() => $"{ScenarioId}:{CanonicalFingerprint}";

    internal static string FingerprintText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

internal static partial class ScenarioResultCatalog
{
    private static readonly ConcurrentDictionary<string, byte> SensitiveValues = new(StringComparer.Ordinal);

    public static string RequireSlug(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !SlugRegex().IsMatch(value))
            throw new ArgumentException("A lowercase kebab-case catalog value is required.", parameterName);

        return value;
    }

    public static string RequireSafeValue(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!IsWellFormedUnicode(value) || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("An observation must be single-line, well-formed Unicode.", parameterName);
        if (SensitiveValues.Keys.Any(secret => value.Contains(secret, StringComparison.Ordinal)))
            throw new ArgumentException("An observation contains a registered fixture secret.", parameterName);
        if (SensitiveAssignmentRegex().IsMatch(value) || ConnectionUriRegex().IsMatch(value))
            throw new ArgumentException("An observation contains a credential, secret, or connection value.", parameterName);

        return value;
    }

    public static string LengthPrefix(string value) =>
        $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}";

    public static void RegisterSensitiveValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        SensitiveValues.TryAdd(value, 0);
    }

    private static bool IsWellFormedUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index]))
                return false;
            if (!char.IsHighSurrogate(value[index]))
                continue;
            if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                return false;
        }

        return true;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(
        @"\b(password|pwd|secret|credential|token|connection(?:\s+string)?)\s*[:=]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(
        @"\b(?:mongodb(?:\+srv)?|postgres(?:ql)?|sqlserver)://",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionUriRegex();
}
