using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases;

/// <summary>Exact-key strategy composition; constructing the scoped verifier resolves and validates this registry.</summary>
internal sealed class ReleaseSemanticRegistry
{
    internal sealed record ArtifactRegistration(IReleaseArtifactSemanticVerifier Verifier, ReleaseSemanticAdapterCapability Capability);
    internal sealed record SiteRegistration(IReleaseOccurrenceInspector Inspector, ReleaseSemanticAdapterCapability Capability);
    internal IReadOnlyDictionary<ReleaseCodecKey, ArtifactRegistration> Artifacts { get; }
    internal IReadOnlyDictionary<ReleaseSiteKey, SiteRegistration> Sites { get; }

    public ReleaseSemanticRegistry(IEnumerable<IReleaseArtifactSemanticVerifier> verifiers, IEnumerable<IReleaseOccurrenceInspector> inspectors)
    {
        var artifacts = new Dictionary<ReleaseCodecKey, ArtifactRegistration>();
        var sites = new Dictionary<ReleaseSiteKey, SiteRegistration>();
        foreach (var verifier in verifiers)
        {
            var identity = verifier?.Identity; var supplied = verifier?.SupportedCodecs;
            if (verifier is null || !Identity(identity) || supplied is not { Count: > 0 and <= 10_000 }) throw Invalid();
            var claims = supplied.Take(10_001).ToArray();
            if (claims.Length is 0 or > 10_000) throw Invalid();
            var capability = new ReleaseSemanticAdapterCapability(identity!, claims, []);
            foreach (var key in claims)
            {
                if (key is null || !Text(key.Kind) || !Text(key.Codec) || key.CodecVersion <= 0) throw Invalid();
                if (!artifacts.TryAdd(key, new(verifier, capability))) throw new InvalidOperationException("Duplicate release artifact codec claim; select exactly one verifier per exact key.");
            }
        }
        foreach (var inspector in inspectors)
        {
            var identity = inspector?.Identity; var supplied = inspector?.SupportedSites;
            if (inspector is null || !Identity(identity) || supplied is not { Count: > 0 and <= 10_000 }) throw Invalid();
            var claims = supplied.Take(10_001).ToArray();
            if (claims.Length is 0 or > 10_000) throw Invalid();
            var capability = new ReleaseSemanticAdapterCapability(identity!, [], claims);
            foreach (var key in claims)
            {
                if (key is null || !Text(key.SiteKind) || !Text(key.Discriminator) || key.SchemaVersion <= 0) throw Invalid();
                if (!sites.TryAdd(key, new(inspector, capability))) throw new InvalidOperationException("Duplicate release occurrence-site claim; select exactly one inspector per exact key.");
            }
        }
        Artifacts = artifacts; Sites = sites;
    }

    private static bool Identity(ReleaseSemanticAdapterIdentity? identity) => identity is not null && Text(identity.Name) && Text(identity.Version);
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static InvalidOperationException Invalid() => new("Invalid or empty release semantic adapter identity or exact-key claim.");
}
