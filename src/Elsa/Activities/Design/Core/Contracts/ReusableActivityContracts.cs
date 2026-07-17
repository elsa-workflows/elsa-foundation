using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Design-time strategy for one stable provider key. Implementations validate and compile source;
/// they never persist drafts, versions, templates, references, or dependency edges.
/// </summary>
public interface IActivityProvider
{
    string ProviderKey { get; }

    IReadOnlySet<string> SupportedManifestSchemas { get; }

    ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; }

    ValueTask<ActivityContractProposal> ProposeContractAsync(
        ActivityProviderContractProposalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
        ActivityProviderManifest manifest,
        ActivityContract contract,
        CancellationToken cancellationToken = default);

    ValueTask<ActivityManifestMigration> MigrateAsync(
        ActivityManifestMigrationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IActivityContractCapabilityCatalog
{
    IReadOnlyCollection<ActivityContractTypeCapability> Types { get; }
}

/// <summary>Publishing bridge contribution for durable storage drivers activated by Runtime.</summary>
public interface IActivityContractStorageDriverProvider
{
    IReadOnlyCollection<string> DriverKeys { get; }
}

public interface IActivityTypeKeyPolicy
{
    ActivityTypeKeyRules Rules { get; }

    string Generate(string displayName, string definitionId);

    string NormalizeAndValidateOverride(string activityTypeKey);
}

public interface IActivityProviderRegistry
{
    IReadOnlyCollection<IActivityProvider> Providers { get; }

    void Add(IActivityProvider provider);

    IActivityProvider Resolve(string providerKey, string manifestSchemaVersion);

    bool TryResolve(string providerKey, string manifestSchemaVersion, out IActivityProvider? provider);
}

/// <summary>
/// Runs platform and provider validation against one exact draft revision. The returned diagnostic
/// sequence is deterministic and is the requested result, not an exceptional control path.
/// </summary>
public interface IActivityDraftValidator
{
    ValueTask<ActivityDraftValidation> ValidateAsync(
        ActivityDraftValidationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the platform compatibility baseline and then merges provider rules, which may strengthen
/// but never weaken the required semantic-version increment.
/// </summary>
public interface IActivityVersionDiffer
{
    ValueTask<ActivityVersionDiff> DiffAsync(
        ActivityVersionDiffRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishing-owned adapter that compiles one exact draft revision for preview while returning only
/// provider-neutral, disclosure-safe diff facts to Design. Implementations never expose descriptors
/// or provider payloads through this contract.
/// </summary>
public interface IActivityDraftDiffCandidateCompiler
{
    ValueTask<ActivityDraftDiffCandidate> CompileAsync(
        ActivityDraftDiffCandidateRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates measured template resource use for a host/tenant context. Foundation's default policy
/// accepts while still preserving measurements; replaceable host policies may reject.
/// </summary>
public interface IActivityTemplateAdmissionPolicy
{
    ValueTask<ActivityAdmissionDecision> EvaluateAsync(
        ActivityResourceMeasurements measurements,
        ActivityAdmissionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Produces an immutable, exact-version, bottom-up plan. Applying a plan that spans activity and
/// workflow drafts belongs to the Publishing bridge rather than this Design contract.
/// </summary>
public interface IActivityUpgradePlanner
{
    ValueTask<ActivityUpgradePlan> PlanAsync(
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cross-owner discovery port used by the Design-owned planner. Publishing supplies the bridge that
/// can inspect both activity and workflow authoring stores without introducing a Design dependency
/// on workflow persistence.
/// </summary>
public interface IActivityUpgradeDiscoverySource
{
    ValueTask<ActivityUpgradeDiscovery> DiscoverAsync(
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Publishing-owned atomic application exposed to Design endpoints by inversion.</summary>
public interface IActivityUpgradePlanApplier
{
    ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradeApplyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional provider-owned source rewriter. Opaque manifests are never rewritten by universal JSON
/// guessing; the provider that owns the schema must opt in and replace exact occurrence references.
/// </summary>
public interface IActivityProviderReferenceRewriter
{
    string ProviderKey { get; }

    IReadOnlySet<string> SupportedManifestSchemas { get; }

    ValueTask<ActivityProviderManifest> RewriteReferencesAsync(
        ActivityProviderManifest manifest,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements,
        CancellationToken cancellationToken = default);
}
