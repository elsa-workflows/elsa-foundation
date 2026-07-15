using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Services;

public sealed class ActivityTemplateDependencyDiscovererRegistry : IActivityTemplateDependencyDiscovererRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string ProviderKey, string Schema), IActivityTemplateDependencyDiscoverer> _discoverers = new();

    public ActivityTemplateDependencyDiscovererRegistry(IEnumerable<IActivityTemplateDependencyDiscoverer>? discoverers = null)
    {
        foreach (var discoverer in discoverers ?? [])
            Add(discoverer);
    }

    public void Add(IActivityTemplateDependencyDiscoverer discoverer)
    {
        ArgumentNullException.ThrowIfNull(discoverer);
        lock (_lock)
        {
            var claims = discoverer.SupportedManifestSchemas.Select(x => (discoverer.ProviderKey, Schema: x)).ToArray();
            if (claims.Length == 0 || claims.Any(x => string.IsNullOrWhiteSpace(x.ProviderKey) || string.IsNullOrWhiteSpace(x.Schema)))
                throw new InvalidOperationException("An activity dependency discoverer must claim a provider key and schema.");
            if (claims.Distinct().Count() != claims.Length)
                throw new InvalidOperationException($"Activity dependency discoverer '{discoverer.ProviderKey}' contains duplicate schema claims.");
            var conflict = claims.FirstOrDefault(_discoverers.ContainsKey);
            if (conflict != default)
                throw new InvalidOperationException($"An activity dependency discoverer already owns provider '{conflict.ProviderKey}' schema '{conflict.Schema}'.");
            var guarded = new GuardedDiscoverer(discoverer);
            foreach (var claim in claims)
                _discoverers.Add(claim, guarded);
        }
    }

    public IActivityTemplateDependencyDiscoverer Resolve(string providerKey, string manifestSchemaVersion)
    {
        lock (_lock)
            return _discoverers.TryGetValue((providerKey, manifestSchemaVersion), out var discoverer)
                ? discoverer
                : throw new InvalidOperationException($"No activity dependency discoverer owns provider '{providerKey}' schema '{manifestSchemaVersion}'.");
    }

    private sealed class GuardedDiscoverer(IActivityTemplateDependencyDiscoverer inner) : IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => inner.ProviderKey;
        public IReadOnlySet<string> SupportedManifestSchemas => inner.SupportedManifestSchemas;

        public async ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(
            ActivityTemplateDependencyDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.DiscoverDependenciesAsync(request, cancellationToken);
                return result with { Diagnostics = ActivityProviderDiagnosticSanitizer.Sanitize(result.Diagnostics, ProviderKey) };
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new([], [new(
                    "activity.provider.failure",
                    ActivityDiagnosticSeverity.Error,
                    $"Activity provider '{ProviderKey}' failed during 'discover-dependencies'.",
                    new("ActivityDraft", request.DraftId, request.DefinitionId, Revision: request.Revision),
                    new(ProviderKey),
                    "Retry the operation or inspect provider logs using the request trace identifier.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["operation"] = "discover-dependencies",
                        ["schemaVersion"] = request.Provider.SchemaVersion
                    })]);
            }
        }
    }
}
