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
    /// Inspects the runtime schema target, optionally applying safe pending operations when
    /// <see cref="GroundworkRuntimeSchemaAdmissionOptions.AutoApplyOnStartup"/> is enabled.
    /// </summary>
    public async ValueTask<GroundworkRuntimeSchemaAdmissionResult> InspectRuntimeAdmissionAsync(
        IPhysicalSchemaExecutor executor,
        GroundworkRuntimeSchemaAdmissionOptions? options = null,
        Action<GroundworkRuntimeSchemaAdmissionLogEntry>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var coreResult = await GroundworkRuntimeSchemaAdmission.InspectRuntimeAdmissionAsync(
                executor,
                PhysicalTarget,
                options,
                log,
                cancellationToken);
            var diagnostics = coreResult.Inspection.IsAppliedSchemaValid ||
                              coreResult.Diagnostics.Any(x => x.Code == "ELSA-GW-SCHEMA-DRIFT")
                ? coreResult.Diagnostics
                : coreResult.Diagnostics.Append(DriftDiagnostic()).ToArray();
            return new GroundworkRuntimeSchemaAdmissionResult(
                PhysicalTarget,
                CompositionFingerprint,
                ResolvedNames,
                coreResult.Inspection.History.AppliedState?.TargetFingerprint,
                coreResult.Inspection.IsAppliedSchemaValid,
                coreResult.PendingOperations,
                diagnostics,
                coreResult.IsReady,
                coreResult.AppliedOperationCount);
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
                diagnostics: [DriftDiagnostic()],
                isReady: false,
                appliedOperationCount: 0);
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

/// <summary>Immutable runtime schema-admission evidence, including auto-apply outcomes.</summary>
public sealed class GroundworkRuntimeSchemaAdmissionResult
{
    public GroundworkRuntimeSchemaAdmissionResult(
        PhysicalSchemaTarget physicalTarget,
        string compositionFingerprint,
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> resolvedNames,
        string? appliedTargetFingerprint,
        bool isAppliedSchemaValid,
        IReadOnlyCollection<PhysicalSchemaOperation> pendingOperations,
        IReadOnlyCollection<GroundworkDiagnostic> diagnostics,
        bool isReady,
        int appliedOperationCount)
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
        IsReady = isReady;
        AppliedOperationCount = appliedOperationCount;
    }

    public PhysicalSchemaTarget PhysicalTarget { get; }

    public string TargetFingerprint => PhysicalTarget.Fingerprint;

    public string CompositionFingerprint { get; }

    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> ResolvedNames { get; }

    public string? AppliedTargetFingerprint { get; }

    public bool IsAppliedSchemaValid { get; }

    public IReadOnlyList<PhysicalSchemaOperation> PendingOperations { get; }

    public IReadOnlyList<GroundworkDiagnostic> Diagnostics { get; }

    /// <summary>
    /// True when the target is admitted — either it was already up-to-date or safe auto-apply
    /// succeeded. Accounts for the <see cref="GroundworkRuntimeSchemaAdmissionOptions.AutoApplyOnStartup"/>
    /// outcome from the Groundwork core admission gate.
    /// </summary>
    public bool IsReady { get; }

    /// <summary>Number of schema operations that were auto-applied at startup (0 when auto-apply is off).</summary>
    public int AppliedOperationCount { get; }
}
