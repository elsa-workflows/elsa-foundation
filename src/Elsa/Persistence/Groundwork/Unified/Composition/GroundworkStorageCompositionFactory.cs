using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Builds one immutable physical-schema source per Groundwork target, from the storage manifest sources
/// bound to that target. Provider leaves supply only their capability evidence and name normalizer.
/// <para>
/// Every declared target composes independently: a target admits the storage units of the lanes bound to
/// it and no others, which is what lets a design-only node carry only design tables. The composed manifest
/// identity is derived from the target name so two targets never contend for one Groundwork schema-state
/// row when an operator points them at the same database.
/// </para>
/// </summary>
public sealed class GroundworkStorageCompositionFactory
{
    private readonly GroundworkStorageCompositionHandler handler;
    private readonly GroundworkStorageCompositionValidator validator;
    private readonly GroundworkStorageNamingPolicyOptions namingPolicy;
    private readonly IReadOnlyList<IGroundworkStorageManifestSource> sources;
    private readonly GroundworkManifestBindings bindings;
    private readonly IInlineEventPublisher? inlineEventPublisher;
    private readonly GroundworkDeploymentSchemaSelection? deploymentSelection;

    /// <summary>
    /// Creates a composition factory. <paramref name="sources"/> and <paramref name="bindings"/> are what
    /// let the factory tell one target's lanes from another's; omitting them means no lane is bound
    /// anywhere, so everything composes into the default target exactly as a single-database host does.
    /// </summary>
    public GroundworkStorageCompositionFactory(
        GroundworkStorageCompositionHandler handler,
        GroundworkStorageCompositionValidator validator,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IInlineEventPublisher? inlineEventPublisher = null,
        IEnumerable<IGroundworkStorageManifestSource>? sources = null,
        GroundworkManifestBindings? bindings = null)
        : this(handler, validator, namingPolicy, inlineEventPublisher, sources, bindings, deploymentSelection: null)
    {
    }

    internal GroundworkStorageCompositionFactory(
        GroundworkStorageCompositionHandler handler,
        GroundworkStorageCompositionValidator validator,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IInlineEventPublisher? inlineEventPublisher,
        IEnumerable<IGroundworkStorageManifestSource>? sources,
        GroundworkManifestBindings? bindings,
        GroundworkDeploymentSchemaSelection? deploymentSelection)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.namingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
        this.sources = sources?.ToArray() ?? [];
        this.bindings = bindings ?? new GroundworkManifestBindings();
        this.inlineEventPublisher = inlineEventPublisher;
        this.deploymentSelection = deploymentSelection;
    }

    /// <summary>
    /// Creates and validates the exact selected source for <paramref name="targetName"/> without inspecting
    /// or changing provider state.
    /// </summary>
    public async ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSourceAsync(
        GroundworkProviderCapabilitySnapshot providerCapabilities,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        CancellationToken cancellationToken = default,
        IGroundworkPhysicalSchemaTargetCompiler? targetCompiler = null,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(providerCapabilities);
        ArgumentNullException.ThrowIfNull(providerNameNormalizer);
        var target = GroundworkTargetNames.Normalize(targetName);
        deploymentSelection?.EnsureExactNamingPolicy(namingPolicy);

        var context = new GroundworkStorageCompositionContext();
        var composing = new OnGroundworkStorageComposing(context);
        if (inlineEventPublisher is not null)
            await inlineEventPublisher.Publish(composing, cancellationToken);
        else
            await handler.Handle(composing, cancellationToken);

        var targetsByFeature = TargetsByFeature();
        var declarations = context.Declarations
            .Where(declaration => TargetOf(targetsByFeature, declaration.FeatureIdentity) == target)
            .ToArray();
        if (declarations.Length == 0)
        {
            throw new GroundworkStorageCompositionException(
                $"Groundwork target '{target}' has no storage manifest bound to it, so there is nothing to " +
                "admit. Bind at least one persistence lane to the target, or stop declaring it.");
        }

        deploymentSelection?.EnsureDeclarationsForTarget(target, declarations, targetsByFeature);
        var requiredStoreContracts = declarations
            .SelectMany(declaration => declaration.RequiredStoreContracts)
            .Distinct()
            .ToArray();
        var result = validator.Validate(new GroundworkStorageCompositionValidationRequest(
            GroundworkStorageCompositionDescriptor.IdentityFor(target),
            GroundworkStorageCompositionDescriptor.Owner,
            GroundworkStorageCompositionDescriptor.Version,
            declarations,
            requiredStoreContracts,
            namingPolicy,
            providerCapabilities,
            providerNameNormalizer,
            targetCompiler));

        if (result.Snapshot is null || !result.IsValid)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message} [{diagnostic.Target}]"));
            throw new GroundworkStorageCompositionException(
                $"The selected Groundwork storage composition is invalid.{Environment.NewLine}{diagnostics}");
        }

        return new GroundworkPhysicalSchemaManifestSource(result.Snapshot);
    }

    /// <summary>
    /// Maps each contributing feature identity to its owning target. Built from the resolved sources rather
    /// than from the declarations, because the binding is recorded against the source's CLR type.
    /// </summary>
    private Dictionary<string, string> TargetsByFeature()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
            result[source.FeatureIdentity] = bindings.TargetFor(source.GetType());
        return result;
    }

    /// <summary>
    /// A declaration contributed by something other than a registered source (a host-supplied event handler,
    /// for example) has no recorded binding and belongs to the default target.
    /// </summary>
    private static string TargetOf(IReadOnlyDictionary<string, string> targetsByFeature, string featureIdentity) =>
        targetsByFeature.GetValueOrDefault(featureIdentity, GroundworkTargetNames.Default);
}

internal static class GroundworkStorageCompositionDescriptor
{
    private const string BaseIdentity = "elsa-documents";

    /// <summary>
    /// The composed manifest identity for one target. Groundwork keys its persisted schema state on
    /// (manifest identity, provider name), so each target needs its own identity or two targets pointed at
    /// one database would overwrite each other's state row. The default target keeps the bare identity, so
    /// databases admitted before targets existed are unaffected.
    /// </summary>
    public static StorageManifestIdentity IdentityFor(string targetName) =>
        GroundworkTargetNames.IsDefault(targetName)
            ? new(BaseIdentity)
            : new($"{BaseIdentity}.{GroundworkTargetNames.Normalize(targetName)}");

    public static StorageManifestIdentity Identity { get; } = new(BaseIdentity);

    public static StorageManifestOwner Owner { get; } = new("elsa.documents");

    public static StorageManifestVersion Version { get; } = new("1.0.0");
}
