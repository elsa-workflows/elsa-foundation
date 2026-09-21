using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>
/// Resolves one provider owner by its persisted provider key and exact manifest schema. Providers
/// returned by the registry are guarded so infrastructure exceptions cannot escape as API details.
/// </summary>
public sealed class ActivityProviderRegistry : IActivityProviderRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IActivityProvider> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ProviderKey, string SchemaVersion), IActivityProvider> _providers = new();

    public ActivityProviderRegistry(IEnumerable<IActivityProvider>? providers = null)
    {
        AddAll(providers ?? []);
    }

    public IReadOnlyCollection<IActivityProvider> Providers
    {
        get
        {
            lock (_lock)
                return _owners.Values.OrderBy(x => x.ProviderKey, StringComparer.Ordinal).ToArray();
        }
    }

    public void Add(IActivityProvider provider) => AddAll([provider]);

    private void AddAll(IEnumerable<IActivityProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var additions = providers.Select(provider =>
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (string.IsNullOrWhiteSpace(provider.ProviderKey))
                throw new ArgumentException("An activity provider must declare a non-empty provider key.", nameof(providers));
            if (provider.SupportedManifestSchemas.Count == 0 || provider.SupportedManifestSchemas.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("An activity provider must declare at least one non-empty manifest schema.", nameof(providers));
            ValidateAuthoringCapabilities(provider);
            return (Provider: provider, Schemas: provider.SupportedManifestSchemas.Order(StringComparer.Ordinal).ToArray());
        }).ToArray();

        lock (_lock)
        {
            var ownerKeys = _owners.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var addition in additions)
                if (!ownerKeys.Add(addition.Provider.ProviderKey))
                    throw new InvalidOperationException($"Activity provider key '{addition.Provider.ProviderKey}' has more than one owner.");

            foreach (var addition in additions)
            {
                var guarded = new GuardedActivityProvider(addition.Provider);
                _owners.Add(addition.Provider.ProviderKey, guarded);
                foreach (var schema in addition.Schemas)
                    _providers.Add((addition.Provider.ProviderKey, schema), guarded);
            }
        }
    }

    private static void ValidateAuthoringCapabilities(IActivityProvider provider)
    {
        var capabilities = provider.AuthoringCapabilities
                           ?? throw new ArgumentException("An activity provider must declare authoring capabilities.", nameof(provider));
        if (string.IsNullOrWhiteSpace(capabilities.DisplayName))
            throw new ArgumentException("An activity provider must declare a non-empty authoring display name.", nameof(provider));
        var schemas = capabilities.ManifestSchemas.Select(x => x.SchemaVersion).ToArray();
        if (schemas.Any(string.IsNullOrWhiteSpace) || schemas.Distinct(StringComparer.Ordinal).Count() != schemas.Length ||
            !provider.SupportedManifestSchemas.SetEquals(schemas))
            throw new ArgumentException("Authoring manifest schemas must exactly match the provider's supported manifest schemas.", nameof(provider));
        if (capabilities.ManifestSchemas.Any(x => x.MigratableFromSchemaVersions.Any(schema => !provider.SupportedManifestSchemas.Contains(schema))))
            throw new ArgumentException("Authoring migration sources must reference a supported manifest schema.", nameof(provider));
        var outcomes = capabilities.ContractConstraints.RequiredOutcomes;
        if (outcomes.Any(x => string.IsNullOrWhiteSpace(x.ReferenceKey) || string.IsNullOrWhiteSpace(x.Name)) ||
            outcomes.Select(x => x.ReferenceKey).Distinct(StringComparer.Ordinal).Count() != outcomes.Count)
            throw new ArgumentException("Required provider outcomes must have unique, non-empty reference keys and names.", nameof(provider));
    }

    public IActivityProvider Resolve(string providerKey, string manifestSchemaVersion)
    {
        if (TryResolve(providerKey, manifestSchemaVersion, out var provider))
            return provider!;

        lock (_lock)
        {
            if (_owners.ContainsKey(providerKey))
                throw new InvalidOperationException(
                    $"Activity provider '{providerKey}' does not support manifest schema '{manifestSchemaVersion}'.");
        }

        throw new InvalidOperationException($"Activity provider '{providerKey}' is not registered.");
    }

    public bool TryResolve(string providerKey, string manifestSchemaVersion, out IActivityProvider? provider)
    {
        lock (_lock)
            return _providers.TryGetValue((providerKey, manifestSchemaVersion), out provider);
    }

    private sealed class GuardedActivityProvider(IActivityProvider inner) : IActivityProvider
    {
        public string ProviderKey => inner.ProviderKey;
        public IReadOnlySet<string> SupportedManifestSchemas => inner.SupportedManifestSchemas;
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities => inner.AuthoringCapabilities;

        public async ValueTask<ActivityContractProposal> ProposeContractAsync(
            ActivityProviderContractProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.ProposeContractAsync(request, cancellationToken);
                return result with { Diagnostics = ActivityProviderDiagnosticSanitizer.Sanitize(result.Diagnostics, ProviderKey) };
            }
            catch (Exception exception) when (ShouldWrap(exception, cancellationToken))
            {
                return new([], [Failure("propose-contract", request.Manifest.SchemaVersion)]);
            }
        }

        public async ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
            ActivityProviderManifest manifest,
            ActivityContract contract,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return ActivityProviderDiagnosticSanitizer.Sanitize(
                    await inner.ValidateAsync(manifest, contract, cancellationToken),
                    ProviderKey);
            }
            catch (Exception exception) when (ShouldWrap(exception, cancellationToken))
            {
                return [Failure("validate", manifest.SchemaVersion)];
            }
        }

        public async ValueTask<ActivityManifestMigration> MigrateAsync(
            ActivityManifestMigrationRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.MigrateAsync(request, cancellationToken);
                return result with { Diagnostics = ActivityProviderDiagnosticSanitizer.Sanitize(result.Diagnostics, ProviderKey) };
            }
            catch (Exception exception) when (ShouldWrap(exception, cancellationToken))
            {
                return new(null, [Failure("migrate", request.Source.SchemaVersion)]);
            }
        }

        private static bool ShouldWrap(Exception exception, CancellationToken cancellationToken) =>
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

        private ActivityDiagnostic Failure(
            string operation,
            string schemaVersion) => new(
            ActivityErrorCodes.ProviderFailure,
            ActivityDiagnosticSeverity.Error,
            $"Activity provider '{ProviderKey}' failed during '{operation}'.",
            new("ActivityDefinition", ProviderKey),
            new(ProviderKey),
            "Retry the operation or inspect provider logs using the request trace identifier.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operation"] = operation,
                ["schemaVersion"] = schemaVersion
            });

    }
}
