namespace Elsa.Persistence.EntityFramework.ResourceResolution;

/// <summary>Resolves the selected resource without reading configuration providers or touching a database.</summary>
internal sealed class PersistenceResourceResolver : IPersistenceResourceResolver
{
    private const string UnenrolledParticipant = "resource-participant-unenrolled";

    public PersistenceResolutionResult Resolve(PersistenceResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var participants = new List<PersistenceParticipantResolution>(input.Participants.Count);
        var refusals = new List<PersistenceResolutionRefusal>();
        foreach (var participant in input.Participants)
            participants.Add(ResolveParticipant(input, participant, refusals));

        // Bindings for known but inactive participants are inert. Unknown keys remain authored and unresolved.
        var knownIds = input.Participants.Select(x => x.FeatureId)
            .Concat(input.LegacyTargets.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolved = input.ShellSelection.FeatureBindings.Keys.Any(key => !knownIds.Contains(key))
            ? new[] { UnenrolledParticipant }
            : [];

        return new PersistenceResolutionResult(
            participants,
            refusals,
            new PersistenceSourceEvidence(input.SourceContext, input.SourceContext.CheckedSources, unresolved));
    }

    private static PersistenceParticipantResolution ResolveParticipant(
        PersistenceResolutionInput input,
        EnrolledPersistenceParticipant participant,
        List<PersistenceResolutionRefusal> refusals)
    {
        var (selection, selected) = Select(input, participant.FeatureId);
        if (selection == PersistenceSelectionKind.Legacy)
        {
            var legacySource = TryGet(input.LegacyTargets, participant.FeatureId, out var target) &&
                               target.Provider.Presence != PersistencePresence.Absent
                ? target.Provider.Source
                : new PersistenceSourceProvenance("feature", "legacy", false, false);
            return new PersistenceParticipantResolution(participant, selection, null, null, null, legacySource, []);
        }

        if (selected.Presence != PersistencePresence.Value || string.IsNullOrWhiteSpace(selected.Value))
        {
            Refuse(refusals, "resource-selection-invalid", participant.FeatureId, null,
                selection == PersistenceSelectionKind.ShellBinding ? "Bindings" : "DefaultResource");
            return Unresolved(participant, selection, selected.Source);
        }

        if (!TryGet(input.RootCatalog.Resources, selected.Value, out var resource))
        {
            Refuse(refusals, "resource-not-found", participant.FeatureId, null, null);
            return Unresolved(participant, selection, selected.Source);
        }

        var resourceName = resource.Name;
        var providerValid = resource.Provider.Presence == PersistencePresence.Value &&
                            !string.IsNullOrWhiteSpace(resource.Provider.Value);
        var connectionValid = resource.ConnectionName.Presence == PersistencePresence.Value &&
                              !string.IsNullOrWhiteSpace(resource.ConnectionName.Value);
        if (!providerValid)
            Refuse(refusals, "resource-definition-invalid", participant.FeatureId, resourceName, "Provider");
        if (!connectionValid)
            Refuse(refusals, "resource-definition-invalid", participant.FeatureId, resourceName, "ConnectionName");

        if (participant.HasOpaqueConfigurator)
            Refuse(refusals, "resource-configurator-unsupported", participant.FeatureId, resourceName, null);

        if (TryGet(input.LegacyTargets, participant.FeatureId, out var legacy))
        {
            if (!legacy.IsEnabled)
                Refuse(refusals, "resource-required-feature-disabled", participant.FeatureId, resourceName, null);
            RefuseIfAuthored(legacy.Provider, "Provider");
            RefuseIfAuthored(legacy.ConnectionName, "ConnectionName");
            RefuseIfAuthored(legacy.ConnectionString, "ConnectionString");
        }

        return new PersistenceParticipantResolution(
            participant,
            selection,
            resourceName,
            providerValid && connectionValid ? resource.Provider.Value : null,
            providerValid && connectionValid ? resource.ConnectionName.Value : null,
            selected.Source,
            []);

        void RefuseIfAuthored(PersistenceLegacyFieldPresence field, string name)
        {
            if (field.Presence != PersistencePresence.Absent && (!legacy.IsReset || field.IsFinalComposed))
                Refuse(refusals, "resource-legacy-conflict", participant.FeatureId, resourceName, name);
        }
    }

    private static (PersistenceSelectionKind Kind, PersistenceAuthoredValue Value) Select(
        PersistenceResolutionInput input,
        string featureId)
    {
        if (TryGet(input.ShellSelection.FeatureBindings, featureId, out var binding) &&
            binding.Presence != PersistencePresence.Absent)
            return (PersistenceSelectionKind.ShellBinding, binding);
        if (input.ShellSelection.ShellDefault.Presence != PersistencePresence.Absent)
            return (PersistenceSelectionKind.ShellDefault, input.ShellSelection.ShellDefault);
        if (input.RootCatalog.RootDefault.Presence != PersistencePresence.Absent)
            return (PersistenceSelectionKind.RootDefault, input.RootCatalog.RootDefault);
        return (PersistenceSelectionKind.Legacy, input.RootCatalog.RootDefault);
    }

    private static PersistenceParticipantResolution Unresolved(
        EnrolledPersistenceParticipant participant,
        PersistenceSelectionKind selection,
        PersistenceSourceProvenance source) =>
        new(participant, selection, null, null, null, source, []);

    private static void Refuse(
        List<PersistenceResolutionRefusal> refusals,
        string code,
        string featureId,
        string? resourceName,
        string? fieldName) =>
        refusals.Add(new PersistenceResolutionRefusal(code, featureId, resourceName, fieldName));

    private static bool TryGet<T>(IReadOnlyDictionary<string, T> values, string key, out T value)
    {
        if (values.TryGetValue(key, out value!))
            return true;
        foreach (var (candidate, found) in values)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(candidate, key))
                continue;

            value = found;
            return true;
        }

        value = default!;
        return false;
    }
}
