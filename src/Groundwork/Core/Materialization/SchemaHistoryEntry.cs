using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;

namespace Groundwork.Core.Materialization;

public sealed record SchemaHistoryEntry(
    StorageManifestIdentity ManifestIdentity,
    StorageManifestVersion ManifestVersion,
    ProviderIdentity Provider,
    IReadOnlyList<string> AppliedOperationTargets);
