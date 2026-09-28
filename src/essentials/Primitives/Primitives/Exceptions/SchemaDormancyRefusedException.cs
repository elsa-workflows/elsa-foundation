namespace Elsa.Primitives.Exceptions;

/// <summary>
/// Spec 182's dormancy refusal (FR-013): an operation needs data that only a schema version this host may not use yet
/// can hold, so it is refused whole, before any write or other side effect, and nothing it carried is dropped to make it
/// fit. The shared dormancy check raises it; it carries the feature when known and the reason on top of spec 180's write
/// refusal, whose code, family and versions it keeps.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SchemaWriteRefusedException"/>, so every domain API already answers it with HTTP 409 in its own problem
/// envelope, carrying the same stable code, and no catch filter or fault ladder that names
/// <see cref="InvalidOperationException"/>, <see cref="ArgumentException"/>, <see cref="FormatException"/>,
/// <see cref="NotSupportedException"/> or <c>JsonException</c> turns it into corruption or a 400.
/// </para>
/// <para>
/// <see cref="SchemaWriteRefusedException.WriteVersion"/> is the version this host observes as finalized for the family,
/// the version it may write; <see cref="SchemaWriteRefusedException.RequiredVersion"/> the version the operation needs.
/// <see cref="Reason"/> is caller-neutral: it names no host and says nothing about the fleet's topology (spec 182,
/// FR-011), because a domain API returns it to whoever sent the request.
/// </para>
/// </remarks>
public sealed class SchemaDormancyRefusedException : SchemaWriteRefusedException
{
    public SchemaDormancyRefusedException(string family, string observedVersion, string requiredVersion, string? featureId, string reason)
        : base(family, observedVersion, requiredVersion, Describe(family, observedVersion, requiredVersion, featureId, reason))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        FeatureId = string.IsNullOrWhiteSpace(featureId) ? null : featureId;
        Reason = reason;
    }

    /// <summary>The dormant feature, or <see langword="null"/> when the operation that asked named none.</summary>
    public string? FeatureId { get; }

    /// <summary>Why the data is not available yet, for each unmet requirement, without naming any host.</summary>
    public string Reason { get; }

    private static string Describe(string family, string observedVersion, string requiredVersion, string? featureId, string reason)
    {
        var subject = string.IsNullOrWhiteSpace(featureId) ? "This operation" : $"Feature '{featureId}'";
        return $"{subject} is dormant: it needs schema version '{requiredVersion}' of schema family '{family}', and this host may use " +
               $"only '{observedVersion}' so far. {reason} The request was refused whole, and nothing it carried was saved.";
    }
}
