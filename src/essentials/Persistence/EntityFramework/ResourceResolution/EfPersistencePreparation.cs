using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.ResourceResolution;

/// <summary>A metadata-only preparation boundary for runtime and management callers.</summary>
public static class EfPersistencePreparation
{
    /// <summary>Expands the final CShells graph without constructing features or running configurators.</summary>
    public static EfPersistencePreparationResult Prepare(
        ShellSettings settings,
        IReadOnlyDictionary<string, ShellFeatureDescriptor> featureMap,
        IConfiguration rootConfiguration,
        IEnumerable<System.Reflection.Assembly>? hostAssemblies = null,
        bool verifyConnectionValues = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(featureMap);
        ArgumentNullException.ThrowIfNull(rootConfiguration);

        var requested = settings.EnabledFeatures.ToArray();
        var orderedIds = new FeatureDependencyResolver().GetOrderedFeatures(
            requested.Where(featureMap.ContainsKey), featureMap);
        var ordered = orderedIds.Select(id =>
        {
            var feature = featureMap[id];
            return new ShellFeaturePreparationDescriptor(
                id, feature.Dependencies, feature.StartupType,
                settings.FeatureConfigurators.ContainsKey(id));
        }).ToArray();
        var context = new ShellSettingsPreparationContext(
            settings.Id,
            settings.ConfigurationData.ToDictionary(x => x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase),
            orderedIds,
            settings.DisabledFeatures,
            settings.FeatureSettingResets,
            ordered,
            requested,
            orderedIds.Except(requested, StringComparer.OrdinalIgnoreCase).ToArray(),
            requested.Where(id => !featureMap.ContainsKey(id)).ToArray());
        return Prepare(context, rootConfiguration, hostAssemblies, verifyConnectionValues);
    }

    public static EfPersistencePreparationResult Prepare(
        ShellSettingsPreparationContext context,
        IConfiguration rootConfiguration,
        IEnumerable<System.Reflection.Assembly>? hostAssemblies = null,
        bool verifyConnectionValues = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rootConfiguration);

        // Known enrollment includes inactive and metadata-only IDs, so startup types from
        // this graph alone are insufficient. Dynamic assemblies are not part of a host's
        // stable package closure and may include deliberately malformed test declarations.
        var loadedAssemblies = (hostAssemblies ?? AppDomain.CurrentDomain.GetAssemblies())
            .Where(x => !x.IsDynamic)
            .Distinct()
            .ToArray();
        var featureAssemblies = context.OrderedFeatures
            .Select(x => x.StartupType?.Assembly)
            .OfType<System.Reflection.Assembly>()
            .Distinct()
            .ToArray();
        var discovered = EfPersistenceParticipantCatalog.Discover(loadedAssemblies)
            .GroupBy(x => x.FeatureId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Count() == 1 ? group.First() : group.First() with { ContextIdentity = string.Empty },
                StringComparer.OrdinalIgnoreCase);
        var participants = context.OrderedFeatures
            .Where(x => discovered.ContainsKey(x.Id) ||
                x.StartupType?.IsDefined(typeof(EfPersistenceResourceParticipantAttribute), inherit: false) == true)
            .Select(x => discovered.TryGetValue(x.Id, out var participant)
                ? participant with
                {
                    ContextIdentity = x.StartupType?.IsDefined(
                        typeof(EfPersistenceResourceParticipantAttribute), inherit: false) == true
                        ? participant.ContextIdentity
                        : string.Empty,
                    HasOpaqueConfigurator = x.HasConfigurator
                }
                : new EnrolledPersistenceParticipant(x.Id, [], string.Empty, false, x.HasConfigurator))
            .ToArray();

        var read = PersistenceConfigurationAdapter.Read(context, rootConfiguration, participants, discovered.Keys.ToArray());
        var resolution = new PersistenceResourceResolver().Resolve(read.Input);
        var refusals = resolution.Refusals.Select(x => x.Code).ToList();
        var applicable = resolution.Participants.Any(x => x.Selection != PersistenceSelectionKind.Legacy);
        foreach (var selected in resolution.Participants.Where(x => x.Selection != PersistenceSelectionKind.Legacy))
        {
            if (selected.ResourceName is not null &&
                read.UnsupportedResourceDefinitions.Contains(selected.ResourceName))
                refusals.Add("resource-definition-invalid");
            if (!selected.Participant.HasOpaqueConfigurator &&
                (string.IsNullOrEmpty(selected.Participant.ContextIdentity) ||
                selected.Participant.ModuleNames.Count == 0))
                refusals.Add("resource-ownership-unresolved");
        }

        if (applicable && refusals.Count == 0)
            refusals.AddRange(EfPersistenceResourceValidator.Validate(
                resolution,
                EfModuleCatalog.Discover(featureAssemblies),
                context.ConfigurationData,
                rootConfiguration,
                verifyConnectionValues));

        var patch = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (refusals.Count == 0)
        {
            foreach (var selected in resolution.Participants.Where(x => x.Selection != PersistenceSelectionKind.Legacy))
            {
                if (selected.Provider is null || selected.ConnectionName is null)
                    continue;
                patch[$"{selected.Participant.FeatureId}:Provider"] = selected.Provider;
                patch[$"{selected.Participant.FeatureId}:ConnectionName"] = selected.ConnectionName;
            }
        }

        return new EfPersistencePreparationResult(
            new ShellSettingsPreparationResult(patch),
            applicable,
            resolution.Participants.Select(x => new EfPersistenceApplicabilityItem(
                x.Participant.FeatureId, x.ResourceName, x.Selection.ToString())).ToArray(),
            refusals.Distinct(StringComparer.Ordinal).ToArray(),
            resolution.Evidence.UnverifiedPrerequisites.Concat(read.UnresolvedCodes)
                .Concat(applicable && !verifyConnectionValues ? ["expected-connection-unchecked"] : [])
                .Distinct(StringComparer.Ordinal).ToArray())
        {
            ResolvedParticipants = resolution.Participants,
            ResourceDefinitions = read.Input.RootCatalog.Resources,
            ActiveFeatureIds = context.OrderedFeatures.Select(feature => feature.Id).ToArray()
        };
    }
}

/// <summary>Scalar preparation patch and redacted selection evidence.</summary>
public sealed record EfPersistencePreparationResult(
    ShellSettingsPreparationResult Patch,
    bool HasApplicableResource,
    IReadOnlyList<EfPersistenceApplicabilityItem> Participants,
    IReadOnlyList<string> RefusalCodes,
    IReadOnlyList<string> UnresolvedCodes)
{
    // Host-owned tooling needs the complete detached target identities. Runtime and management
    // callers continue to receive only the public patch/applicability view.
    internal IReadOnlyList<PersistenceParticipantResolution> ResolvedParticipants { get; init; } = [];
    internal IReadOnlyDictionary<string, PersistenceResourceDefinition> ResourceDefinitions { get; init; } =
        new Dictionary<string, PersistenceResourceDefinition>(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<string> ActiveFeatureIds { get; init; } = [];
    internal IReadOnlyList<EfFeatureModuleUsage> HostFeatureUsages { get; init; } = [];
    internal IReadOnlyDictionary<string, string?> ConfiguredProviders { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One enrolled feature's effective selection, without connection values.</summary>
public sealed record EfPersistenceApplicabilityItem(
    string FeatureId,
    string? ResourceName,
    string SelectionKind);
