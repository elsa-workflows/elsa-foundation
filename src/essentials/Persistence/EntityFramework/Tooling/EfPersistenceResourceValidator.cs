using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Checks EF-owned constraints on a detached resource plan before feature activation.</summary>
/// <remarks>Connection values are used only for equality and never leave this method.</remarks>
internal static class EfPersistenceResourceValidator
{
    internal static IReadOnlyList<string> Validate(
        PersistenceResolutionResult resolution,
        IReadOnlyList<EfModuleDescriptor> modules,
        IReadOnlyDictionary<string, string?> composedSettings,
        IConfiguration rootConfiguration)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(composedSettings);
        ArgumentNullException.ThrowIfNull(rootConfiguration);

        var refusals = new HashSet<string>(StringComparer.Ordinal);
        var targets = new List<Target>();
        foreach (var selected in resolution.Participants.Where(x => x.Selection != PersistenceSelectionKind.Legacy))
        {
            if (selected.Provider is null || selected.ConnectionName is null)
                continue; // The resolver has already refused an incomplete selection.

            var participant = selected.Participant;
            if (participant.ModuleNames.Count != 1 ||
                EfModuleCatalog.Find(modules, participant.ModuleNames[0]) is not { } module ||
                !StringComparer.Ordinal.Equals(module.ContextType.FullName, participant.ContextIdentity))
            {
                refusals.Add("resource-ownership-unresolved");
                continue;
            }

            var connection = ConnectionString(selected.ConnectionName, composedSettings, rootConfiguration);
            if (string.IsNullOrWhiteSpace(connection))
            {
                refusals.Add("resource-definition-invalid");
                continue;
            }
            if (CreateTarget(participant.FeatureId, module, selected.Provider, connection,
                    composedSettings, rootConfiguration, refusals) is { } target)
                targets.Add(target);
        }

        // A resource binding on one Runtime feature cannot silently split the same context
        // from another Runtime feature that retained legacy settings.
        var selectedContexts = targets.Select(x => x.ContextType).ToHashSet();
        foreach (var legacy in resolution.Participants.Where(x => x.Selection == PersistenceSelectionKind.Legacy))
        {
            var participant = legacy.Participant;
            if (participant.ModuleNames.Count != 1 ||
                EfModuleCatalog.Find(modules, participant.ModuleNames[0]) is not { } module ||
                !selectedContexts.Contains(module.ContextType))
                continue;

            var provider = Get(composedSettings, $"{participant.FeatureId}:Provider") ??
                           EfProviderAgreement.UnsetProvider;
            var connection = Get(composedSettings, $"{participant.FeatureId}:ConnectionString");
            if (string.IsNullOrWhiteSpace(connection))
            {
                var authoredName = Get(composedSettings, $"{participant.FeatureId}:ConnectionName");
                var name = string.IsNullOrWhiteSpace(authoredName)
                    ? module.DefaultConnectionName
                    : authoredName;
                connection = ConnectionString(name, composedSettings, rootConfiguration);
                if (string.IsNullOrWhiteSpace(connection) &&
                    string.IsNullOrWhiteSpace(authoredName) &&
                    EfRelationalProviderBinding.Normalize(provider) == "sqlite")
                    connection = module.DefaultSqliteConnectionString;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                refusals.Add("resource-context-conflict");
                continue;
            }

            if (CreateTarget(participant.FeatureId, module, provider, connection,
                    composedSettings, rootConfiguration, refusals) is { } target)
                targets.Add(target);
        }

        // Several Runtime features configure the same DbContext. The resource name is not a
        // physical-target identity: compare the resolved connection and all shared options.
        foreach (var group in targets.GroupBy(x => x.ContextType))
        {
            var first = group.First();
            if (group.Skip(1).Any(x =>
                    !StringComparer.Ordinal.Equals(x.Provider, first.Provider) ||
                    !StringComparer.Ordinal.Equals(x.Connection, first.Connection) ||
                    !StringComparer.Ordinal.Equals(x.Schema, first.Schema) ||
                    x.Pooling != first.Pooling))
                refusals.Add("resource-context-conflict");
        }

        // Selection never grants permission to migrate; an invalid authored policy must not
        // silently fall back to AutoMigrate. No migration probe runs in this validator.
        var policy = Get(composedSettings, $"{EfMigrateOptions.SectionName}:Policy") ??
                     rootConfiguration[$"{EfMigrateOptions.SectionName}:Policy"];
        if (!string.IsNullOrWhiteSpace(policy) &&
            (!Enum.TryParse<EfMigratePolicy>(policy, true, out var parsed) || !Enum.IsDefined(parsed)))
            refusals.Add("resource-context-conflict");

        return refusals.Order(StringComparer.Ordinal).ToArray();
    }

    private static Target? CreateTarget(
        string featureId,
        EfModuleDescriptor module,
        string provider,
        string connection,
        IReadOnlyDictionary<string, string?> composedSettings,
        IConfiguration rootConfiguration,
        ISet<string> refusals)
    {
        try
        {
            provider = EfRelationalProviderBinding.Normalize(provider);
            if (module.ProviderContext(provider) is null ||
                EfRelationalProviderBinding.DescribeBindingFailure(provider) is not null)
            {
                refusals.Add("resource-context-conflict");
                return null;
            }
        }
        catch (ArgumentException)
        {
            refusals.Add("resource-context-conflict");
            return null;
        }

        var schema = Get(composedSettings, $"{featureId}:Schema") ??
                     Get(composedSettings, EfSchema.ConfigurationKey) ??
                     rootConfiguration[EfSchema.ConfigurationKey];
        try
        {
            schema = EfSchema.Normalize(module.Owner, provider, schema);
        }
        catch (InvalidOperationException)
        {
            refusals.Add("resource-context-conflict");
            return null;
        }

        var poolingValue = Get(composedSettings, $"{featureId}:Pooling");
        if (poolingValue is not null && !bool.TryParse(poolingValue, out _))
        {
            refusals.Add("resource-context-conflict");
            return null;
        }

        return new Target(module.ContextType, provider, connection, schema,
            bool.TryParse(poolingValue, out var pooling) && pooling);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> settings, string key)
    {
        if (settings.TryGetValue(key, out var value))
            return value;
        return settings.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Key, key)).Value;
    }

    private static string? ConnectionString(
        string name,
        IReadOnlyDictionary<string, string?> composedSettings,
        IConfiguration rootConfiguration) =>
        Get(composedSettings, $"ConnectionStrings:{name}") ?? rootConfiguration.GetConnectionString(name);

    private sealed record Target(Type ContextType, string Provider, string Connection, string? Schema, bool Pooling);
}
