using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.ResourceResolution;

/// <summary>A metadata-only preparation boundary for runtime and management callers.</summary>
public static class EfPersistencePreparation
{
    public static EfPersistencePreparationResult Prepare(
        ShellSettingsPreparationContext context,
        IConfiguration rootConfiguration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rootConfiguration);

        var discovered = EfPersistenceParticipantCatalog.Discover(AppDomain.CurrentDomain.GetAssemblies())
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
            if (string.IsNullOrEmpty(selected.Participant.ContextIdentity) ||
                selected.Participant.ModuleNames.Count == 0)
                refusals.Add("resource-ownership-unresolved");
        }

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
                .Distinct(StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Scalar preparation patch and redacted selection evidence.</summary>
public sealed record EfPersistencePreparationResult(
    ShellSettingsPreparationResult Patch,
    bool HasApplicableResource,
    IReadOnlyList<EfPersistenceApplicabilityItem> Participants,
    IReadOnlyList<string> RefusalCodes,
    IReadOnlyList<string> UnresolvedCodes);

/// <summary>One enrolled feature's effective selection, without connection values.</summary>
public sealed record EfPersistenceApplicabilityItem(
    string FeatureId,
    string? ResourceName,
    string SelectionKind);
