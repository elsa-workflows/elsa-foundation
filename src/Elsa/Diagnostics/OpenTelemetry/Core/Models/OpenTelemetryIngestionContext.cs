using System.Collections.ObjectModel;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Models;

/// <summary>
/// Describes the server-authoritative identity attached to an OpenTelemetry ingestion operation.
/// Telemetry resource attributes are signal data and must never be copied into this context unless an
/// authenticated host implementation has independently validated them.
/// </summary>
public sealed class OpenTelemetryIngestionContext
{
    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    private OpenTelemetryIngestionContext(
        bool isAuthenticated,
        string? sourceIdentity,
        IReadOnlyDictionary<string, string> claims,
        IReadOnlyDictionary<string, string> metadata)
    {
        IsAuthenticated = isAuthenticated;
        SourceIdentity = sourceIdentity;
        Claims = claims;
        Metadata = metadata;
    }

    /// <summary>An explicitly untrusted context used when no request authenticator supplied authority.</summary>
    public static OpenTelemetryIngestionContext Untrusted { get; } = new(false, null, EmptyValues, EmptyValues);

    /// <summary>Whether a host-owned authenticator established the source identity.</summary>
    public bool IsAuthenticated { get; }

    /// <summary>
    /// Opaque, stable identity assigned to the authenticated telemetry source. Consumers must not infer tenant
    /// scope from its format; use authenticated claims for scope mapping.
    /// </summary>
    public string? SourceIdentity { get; }

    /// <summary>Immutable, authenticated claims suitable for workspace/application/environment mapping.</summary>
    public IReadOnlyDictionary<string, string> Claims { get; }

    /// <summary>Immutable, authenticated auxiliary metadata about the ingestion credential or source.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Creates an authenticated context from values established by a host-owned authenticator.</summary>
    public static OpenTelemetryIngestionContext Authenticated(
        string sourceIdentity,
        IEnumerable<KeyValuePair<string, string>>? claims = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
            throw new ArgumentException("An authenticated ingestion source identity is required.", nameof(sourceIdentity));

        return new(true, sourceIdentity, CopyValues(claims, nameof(claims)), CopyValues(metadata, nameof(metadata)));
    }

    private static IReadOnlyDictionary<string, string> CopyValues(
        IEnumerable<KeyValuePair<string, string>>? values,
        string parameterName)
    {
        if (values == null)
            return EmptyValues;

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ingestion context keys cannot be blank.", parameterName);
            if (value == null)
                throw new ArgumentException("Ingestion context values cannot be null.", parameterName);

            copy.Add(key, value);
        }

        return copy.Count == 0 ? EmptyValues : new ReadOnlyDictionary<string, string>(copy);
    }
}
