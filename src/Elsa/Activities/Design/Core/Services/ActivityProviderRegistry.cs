using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

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
        foreach (var provider in providers ?? [])
            Add(provider);
    }

    public void Add(IActivityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderKey))
            throw new ArgumentException("An activity provider must declare a non-empty provider key.", nameof(provider));
        if (provider.SupportedManifestSchemas.Count == 0 || provider.SupportedManifestSchemas.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("An activity provider must declare at least one non-empty manifest schema.", nameof(provider));

        lock (_lock)
        {
            if (_owners.ContainsKey(provider.ProviderKey))
                throw new InvalidOperationException($"Activity provider key '{provider.ProviderKey}' has more than one owner.");

            var guarded = new GuardedActivityProvider(provider);
            _owners.Add(provider.ProviderKey, guarded);
            foreach (var schema in provider.SupportedManifestSchemas.Order(StringComparer.Ordinal))
                _providers.Add((provider.ProviderKey, schema), guarded);
        }
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

        public async ValueTask<ActivityContractProposal> ProposeContractAsync(
            ActivityProviderManifest manifest,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.ProposeContractAsync(manifest, cancellationToken);
                return result with { Diagnostics = ActivityDiagnosticOrderer.Order(result.Diagnostics) };
            }
            catch (Exception exception) when (ShouldWrap(exception, cancellationToken))
            {
                return new(EmptyContract(), [Failure("propose-contract", manifest.SchemaVersion)]);
            }
        }

        public async ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
            ActivityProviderManifest manifest,
            ActivityContract contract,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return ActivityDiagnosticOrderer.Order(await inner.ValidateAsync(manifest, contract, cancellationToken));
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
                return result with { Diagnostics = ActivityDiagnosticOrderer.Order(result.Diagnostics) };
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
            "activity.provider.failure",
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

        private static ActivityContract EmptyContract() => new("1", [], [], []);

    }
}
