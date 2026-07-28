using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Builds the one immutable physical-schema source from the storage manifest sources selected by
/// the current host. Provider leaves supply only their capability evidence and name normalizer.
/// </summary>
public sealed class GroundworkStorageCompositionFactory
{
    private readonly GroundworkStorageCompositionHandler handler;
    private readonly GroundworkStorageCompositionValidator validator;
    private readonly GroundworkStorageNamingPolicyOptions namingPolicy;
    private readonly IInlineEventPublisher? inlineEventPublisher;
    private readonly GroundworkDeploymentSchemaSelection? deploymentSelection;

    public GroundworkStorageCompositionFactory(
        GroundworkStorageCompositionHandler handler,
        GroundworkStorageCompositionValidator validator,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IInlineEventPublisher? inlineEventPublisher = null)
        : this(handler, validator, namingPolicy, inlineEventPublisher, deploymentSelection: null)
    {
    }

    internal GroundworkStorageCompositionFactory(
        GroundworkStorageCompositionHandler handler,
        GroundworkStorageCompositionValidator validator,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IInlineEventPublisher? inlineEventPublisher,
        GroundworkDeploymentSchemaSelection? deploymentSelection)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.namingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
        this.inlineEventPublisher = inlineEventPublisher;
        this.deploymentSelection = deploymentSelection;
    }

    /// <summary>Creates and validates the exact selected source without inspecting or changing provider state.</summary>
    public async ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSourceAsync(
        GroundworkProviderCapabilitySnapshot providerCapabilities,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        CancellationToken cancellationToken = default,
        IGroundworkPhysicalSchemaTargetCompiler? targetCompiler = null)
    {
        ArgumentNullException.ThrowIfNull(providerCapabilities);
        ArgumentNullException.ThrowIfNull(providerNameNormalizer);
        deploymentSelection?.EnsureExactNamingPolicy(namingPolicy);

        var context = new GroundworkStorageCompositionContext();
        var composing = new OnGroundworkStorageComposing(context);
        if (inlineEventPublisher is not null)
            await inlineEventPublisher.Publish(composing, cancellationToken);
        else
            await handler.Handle(composing, cancellationToken);
        var declarations = context.Declarations;
        deploymentSelection?.EnsureExactDeclarations(declarations);
        var requiredStoreContracts = declarations
            .SelectMany(declaration => declaration.RequiredStoreContracts)
            .Distinct()
            .ToArray();
        var result = validator.Validate(new GroundworkStorageCompositionValidationRequest(
            GroundworkStorageCompositionDescriptor.Identity,
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
}

internal static class GroundworkStorageCompositionDescriptor
{
    public static StorageManifestIdentity Identity { get; } = new("elsa-documents");

    public static StorageManifestOwner Owner { get; } = new("elsa.documents");

    public static StorageManifestVersion Version { get; } = new("1.0.0");
}
