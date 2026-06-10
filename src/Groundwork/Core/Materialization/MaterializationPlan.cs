using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Validation;

namespace Groundwork.Core.Materialization;

public sealed record MaterializationPlan(
    string Identity,
    ProviderIdentity Provider,
    StorageManifestIdentity ManifestIdentity,
    StorageManifestVersion ManifestVersion,
    IReadOnlyList<MaterializationOperation> Operations,
    SchemaHistoryEntry SchemaHistory,
    IReadOnlyList<GroundworkDiagnostic> Diagnostics);

public sealed record MaterializationOperation(
    MaterializationOperationKind Kind,
    string Target,
    IReadOnlyDictionary<string, string> Metadata);
