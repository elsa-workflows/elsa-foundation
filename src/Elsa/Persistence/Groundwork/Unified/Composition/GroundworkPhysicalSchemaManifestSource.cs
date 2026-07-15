using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Validation;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Exposes one admitted Elsa composition to both Groundwork.Tool and the runtime readiness gate.
/// Runtime admission only inspects provider history and computes a diff; it never applies schema.
/// </summary>
public sealed class GroundworkPhysicalSchemaManifestSource : IPhysicalSchemaManifestSource
{
    public GroundworkPhysicalSchemaManifestSource(GroundworkStorageCompositionSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ResolvedNames = CreateToolResolvedNames(snapshot.ResolvedNames);
    }

    public GroundworkStorageCompositionSnapshot Snapshot { get; }

    public PhysicalSchemaTarget PhysicalTarget => Snapshot.PhysicalTarget;

    /// <summary>Groundwork's authoritative physical target fingerprint used by runtime and CLI.</summary>
    public string TargetFingerprint => PhysicalTarget.Fingerprint;

    /// <summary>The wider Elsa host-selection fingerprint; never substituted for the physical target.</summary>
    public string CompositionFingerprint => Snapshot.CompositionFingerprint;

    /// <summary>
    /// Effective names in the same logical-name representation emitted by Groundwork.Tool. The
    /// composition snapshot separately retains the host-transformed inputs used during resolution.
    /// </summary>
    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> ResolvedNames { get; }

    public StorageManifest CreateManifest() => Snapshot.Manifest;

    public IPhysicalNamePolicy CreateNamePolicy() => Snapshot.PhysicalNamePolicy;

    /// <summary>
    /// Reads the applied history and live compatibility snapshot, then computes the additive diff
    /// without acquiring an application lock, creating schema infrastructure or applying work.
    /// </summary>
    public async ValueTask<GroundworkRuntimeSchemaAdmissionResult> InspectRuntimeAdmissionAsync(
        IPhysicalSchemaHistoryInspector inspector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var inspection = await inspector.InspectHistoryAsync(PhysicalTarget, cancellationToken);
            var plan = PhysicalSchemaDiffPlanner.Plan(
                PhysicalTarget,
                inspection.History,
                DateTimeOffset.UnixEpoch);
            var diagnostics = new List<GroundworkDiagnostic>(plan.Diagnostics);
            if (!inspection.IsAppliedSchemaValid || !plan.IsApplicable)
            {
                diagnostics.Add(DriftDiagnostic());
            }
            else if (plan.Operations.Count != 0)
            {
                diagnostics.Add(GroundworkDiagnostic.Error(
                    "ELSA-GW-SCHEMA-PENDING",
                    $"Groundwork physical target '{TargetFingerprint}' for provider '{PhysicalTarget.Provider.Name}' has {plan.Operations.Count} pending schema operation(s). Apply the exact reviewed plan through Groundwork.Tool before starting the host.",
                    "schema.pendingOperations"));
            }

            return new GroundworkRuntimeSchemaAdmissionResult(
                PhysicalTarget,
                CompositionFingerprint,
                ResolvedNames,
                inspection.History.AppliedState?.TargetFingerprint,
                inspection.IsAppliedSchemaValid,
                plan.Operations,
                diagnostics);
        }
        catch (InvalidOperationException)
        {
            return new GroundworkRuntimeSchemaAdmissionResult(
                PhysicalTarget,
                CompositionFingerprint,
                ResolvedNames,
                appliedTargetFingerprint: null,
                isAppliedSchemaValid: false,
                pendingOperations: [],
                diagnostics: [DriftDiagnostic()]);
        }
    }

    private GroundworkDiagnostic DriftDiagnostic() => GroundworkDiagnostic.Error(
        "ELSA-GW-SCHEMA-DRIFT",
        $"Live provider state for Groundwork physical target '{TargetFingerprint}' on provider '{PhysicalTarget.Provider.Name}' is incompatible with its durable applied schema history. Runtime admission is blocked and no repair was attempted.",
        "schema.providerState");

    private static IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> CreateToolResolvedNames(
        IEnumerable<GroundworkResolvedPhysicalNameSnapshot> resolvedNames) => Array.AsReadOnly(resolvedNames
        .Select(name => name.ObjectKind is PhysicalObjectKind.EnvelopeField or PhysicalObjectKind.LinkedIndexField
            ? new GroundworkResolvedPhysicalNameSnapshot(
                name.FeatureIdentity,
                name.StorageUnit,
                name.ObjectKind,
                name.FeatureDefaultLogicalName,
                name.FeatureDefaultLogicalName,
                name.Identifier,
                name.CollisionScope)
            : name)
        .OrderBy(name => name.FeatureIdentity, StringComparer.Ordinal)
        .ThenBy(name => name.StorageUnit.Value, StringComparer.Ordinal)
        .ThenBy(name => name.ObjectKind)
        .ThenBy(name => name.FeatureDefaultLogicalName, StringComparer.Ordinal)
        .ThenBy(name => name.Identifier, StringComparer.Ordinal)
        .ToArray());
}

/// <summary>Immutable, side-effect-free runtime schema-admission evidence.</summary>
public sealed class GroundworkRuntimeSchemaAdmissionResult
{
    public GroundworkRuntimeSchemaAdmissionResult(
        PhysicalSchemaTarget physicalTarget,
        string compositionFingerprint,
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> resolvedNames,
        string? appliedTargetFingerprint,
        bool isAppliedSchemaValid,
        IReadOnlyCollection<PhysicalSchemaOperation> pendingOperations,
        IReadOnlyCollection<GroundworkDiagnostic> diagnostics)
    {
        PhysicalTarget = physicalTarget ?? throw new ArgumentNullException(nameof(physicalTarget));
        CompositionFingerprint = string.IsNullOrWhiteSpace(compositionFingerprint)
            ? throw new ArgumentException("A composition fingerprint is required.", nameof(compositionFingerprint))
            : compositionFingerprint;
        ResolvedNames = Array.AsReadOnly((resolvedNames ?? throw new ArgumentNullException(nameof(resolvedNames)))
            .OrderBy(name => name.FeatureIdentity, StringComparer.Ordinal)
            .ThenBy(name => name.StorageUnit.Value, StringComparer.Ordinal)
            .ThenBy(name => name.ObjectKind)
            .ThenBy(name => name.FeatureDefaultLogicalName, StringComparer.Ordinal)
            .ThenBy(name => name.Identifier, StringComparer.Ordinal)
            .ToArray());
        AppliedTargetFingerprint = appliedTargetFingerprint;
        IsAppliedSchemaValid = isAppliedSchemaValid;
        PendingOperations = Array.AsReadOnly(
            (pendingOperations ?? throw new ArgumentNullException(nameof(pendingOperations))).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)))
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Target, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray());
    }

    public PhysicalSchemaTarget PhysicalTarget { get; }

    public string TargetFingerprint => PhysicalTarget.Fingerprint;

    public string CompositionFingerprint { get; }

    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> ResolvedNames { get; }

    public string? AppliedTargetFingerprint { get; }

    public bool IsAppliedSchemaValid { get; }

    public IReadOnlyList<PhysicalSchemaOperation> PendingOperations { get; }

    public IReadOnlyList<GroundworkDiagnostic> Diagnostics { get; }

    public bool IsReady => IsAppliedSchemaValid &&
                           PendingOperations.Count == 0 &&
                           Diagnostics.All(diagnostic => !diagnostic.IsError);
}
