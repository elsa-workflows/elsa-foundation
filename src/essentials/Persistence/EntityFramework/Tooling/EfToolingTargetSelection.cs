using Elsa.Persistence.EntityFramework.ResourceResolution;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Chooses module names from the composed shell's detached target identities.</summary>
internal static class EfToolingTargetSelection
{
    internal static IReadOnlyList<string> Select(
        EfPersistencePreparationResult preparation,
        IReadOnlyList<string> discovered,
        EfToolingSelection? selection,
        string? resource)
    {
        if (preparation.RefusalCodes.Count > 0)
            throw EfToolingRefusal.Resolution(preparation.RefusalCodes[0],
                "The selected shell has unresolved persistence resource configuration.");

        var participants = preparation.ResolvedParticipants;
        (string Provider, string ConnectionName)? group = resource is null
            ? null
            : ResolveGroup(preparation.ResourceDefinitions, participants, resource);
        var candidates = group is null
            ? participants
            : participants.Where(participant => InGroup(participant, group.Value)).ToArray();
        var candidateModules = candidates.SelectMany(participant => participant.Participant.ModuleNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> selected;
        if (selection is null)
            selected = group is null ? discovered : candidateModules.Order(StringComparer.Ordinal).ToArray();
        else if (selection.Kind == EfToolingSelection.AllKind && selection.Modules is null)
            selected = discovered;
        else if (selection.Kind == EfToolingSelection.FromHostKind && selection.Modules is null)
            selected = candidateModules.Order(StringComparer.Ordinal).ToArray();
        else if (selection.Kind == EfToolingSelection.ModulesKind && selection.Modules is { Count: > 0 } names &&
                 names.All(name => !string.IsNullOrWhiteSpace(name)) &&
                 names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count)
            selected = names;
        else
            throw EfToolingRefusal.Usage("invalid-request", "The module selection is not valid.");

        if (selected.Count == 0)
            throw EfToolingRefusal.Resolution("from-host-selected-nothing",
                "The selected shell and resource contain no enabled enrolled module.");

        var catalog = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Any(name => !catalog.Contains(name)))
            throw EfToolingRefusal.Resolution("unknown-module", "A selected module is not declared in this host closure.");

        if (group is not null && selected.Any(name => !candidateModules.Contains(name) ||
                participants.Any(participant => participant.Participant.ModuleNames.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                    !InGroup(participant, group.Value))))
            throw EfToolingRefusal.Resolution("resource-target-scope",
                "A selected module has an enabled owner outside the selected declared target group.");

        return selected;
    }

    private static (string Provider, string ConnectionName) ResolveGroup(
        IReadOnlyDictionary<string, PersistenceResourceDefinition> resources,
        IReadOnlyList<PersistenceParticipantResolution> participants,
        string resource)
    {
        if (string.IsNullOrWhiteSpace(resource) || !resources.TryGetValue(resource, out var definition))
            throw EfToolingRefusal.Resolution("resource-not-found", "The selected persistence resource is not defined.");
        if (definition.Provider.Presence != PersistencePresence.Value ||
            definition.ConnectionName.Presence != PersistencePresence.Value ||
            string.IsNullOrWhiteSpace(definition.Provider.Value) ||
            string.IsNullOrWhiteSpace(definition.ConnectionName.Value))
            throw EfToolingRefusal.Resolution("resource-definition-invalid", "The selected persistence resource has an invalid target definition.");

        string provider;
        try
        {
            provider = EfRelationalProviderBinding.Select(definition.Provider.Value, "relational",
                "Sqlite", "SqlServer", "PostgreSql", "MySql");
        }
        catch (ArgumentException)
        {
            throw EfToolingRefusal.Resolution("resource-definition-invalid", "The selected persistence resource has an unsupported provider.");
        }
        var group = (provider, definition.ConnectionName.Value);
        if (!participants.Any(participant =>
                StringComparer.OrdinalIgnoreCase.Equals(participant.ResourceName, definition.Name) &&
                InGroup(participant, group)))
            throw EfToolingRefusal.Resolution("resource-target-scope",
                "The selected persistence resource has no enabled enrolled participant in this shell.");
        return group;
    }

    private static bool InGroup(PersistenceParticipantResolution participant,
        (string Provider, string ConnectionName) group) =>
        participant.Selection != PersistenceSelectionKind.Legacy &&
        participant.Provider is not null &&
        string.Equals(EfRelationalProviderBinding.Normalize(participant.Provider),
            EfRelationalProviderBinding.Normalize(group.Provider), StringComparison.Ordinal) &&
        string.Equals(participant.ConnectionName, group.ConnectionName, StringComparison.OrdinalIgnoreCase);
}
