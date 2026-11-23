using System.Collections.Immutable;
using System.Text.Json;

namespace Elsa.Modularity.Planning.Models;

public sealed record DependencyExplanation(
    string FeatureId,
    string DependencyId,
    string Mode,
    string Reason);

public sealed record SelectionDefinition(
    string Kind,
    string Id,
    string Version,
    string Digest,
    ImmutableArray<string> Members,
    string Rationale,
    string Title,
    string Description,
    ImmutableArray<DependencyExplanation> DependencyExplanations);

public sealed record SelectionCatalog(
    string SchemaVersion,
    string Id,
    string Version,
    string Publisher,
    string Digest,
    ImmutableArray<SelectionDefinition> Profiles,
    ImmutableArray<SelectionDefinition> Groups);

public sealed record WorkspaceProfile(
    string SchemaVersion,
    SelectionDefinition Definition);

public sealed record CatalogPin(string Id, string Version, string Digest);

public sealed record DefinitionReference(
    string Origin,
    string Kind,
    string Id,
    string Version,
    string Digest);

public sealed record FeatureLock(
    string FeatureId,
    string Kind,
    string? PackageId,
    string? PackageVersion,
    string? ManifestDigest,
    string EvidenceSource);

public sealed record AcceptedSelection(
    string CatalogDigest,
    ImmutableArray<string> FeatureIds,
    ImmutableArray<FeatureLock> Locks);

public sealed record AuthoredComposition(
    string SchemaVersion,
    CatalogPin Catalog,
    DefinitionReference? Profile,
    ImmutableArray<DefinitionReference> Groups,
    ImmutableArray<string> Add,
    ImmutableArray<string> Remove,
    AcceptedSelection Accepted,
    JsonElement? Settings,
    JsonElement? Resources);
