using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>
/// Applies Elsa's provider-neutral compatibility floor. Provider findings are merged last and may
/// strengthen a platform finding or add a safe namespaced finding, but can never weaken the floor.
/// </summary>
public sealed class ActivityVersionDiffer : IActivityVersionDiffer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ValueTask<ActivityVersionDiff> DiffAsync(
        ActivityVersionDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changes = new Dictionary<string, ActivityVersionChange>(StringComparer.Ordinal);

        if (!StringComparer.Ordinal.Equals(request.FromContract.ContractSchemaVersion, request.ToContract.ContractSchemaVersion))
            Add(changes, SimpleChange(
                "contract:schema:contract-schema-changed",
                ActivityVersionChangeArea.Contract,
                "ContractSchemaChanged",
                Element(new { schemaVersion = request.FromContract.ContractSchemaVersion }),
                Element(new { schemaVersion = request.ToContract.ContractSchemaVersion }),
                ActivityVersionChangeImpact.Breaking,
                ActivityVersionBump.Major,
                "The public activity contract schema changed."));

        CompareMembers(request.FromContract.Inputs, request.ToContract.Inputs, "Input", changes, CompareInput, AddedInput, RemovedMember);
        CompareMembers(request.FromContract.Outputs, request.ToContract.Outputs, "Output", changes, CompareOutput, AddedOutput, RemovedMember);
        CompareMembers(request.FromContract.Outcomes, request.ToContract.Outcomes, "Outcome", changes, CompareOutcome, AddedOutcome, RemovedMember);
        CompareProvider(request.FromProvider, request.ToProvider, changes);
        CompareImplementation(request, changes);
        if (request.CompareDependencies)
            CompareDependencies(request.FromDependencies, request.ToDependencies, changes);
        MergeProviderChanges(request.ProviderCompatibilityChanges ?? [], changes);

        var ordered = changes.Values.OrderBy(x => x.ChangeId, StringComparer.Ordinal).ToArray();
        var requiredBump = ordered.Select(x => x.RequiredBump).DefaultIfEmpty(ActivityVersionBump.None).Max();
        var compatibility = requiredBump switch
        {
            ActivityVersionBump.None => ActivityVersionCompatibility.Identical,
            ActivityVersionBump.Patch => ActivityVersionCompatibility.NonBehavioral,
            ActivityVersionBump.Minor => ActivityVersionCompatibility.Compatible,
            _ => ActivityVersionCompatibility.Breaking
        };
        var diagnostics = ActivityDiagnosticOrderer.Order(request.Diagnostics ?? []);
        var summary = new ActivityVersionDiffSummary(
            ordered.Count(x => x.Impact == ActivityVersionChangeImpact.Breaking),
            ordered.Count(x => x.Impact == ActivityVersionChangeImpact.Additive),
            ordered.Count(x => x.Impact == ActivityVersionChangeImpact.NonBehavioral),
            diagnostics.Count(x => x.Severity == ActivityDiagnosticSeverity.Warning));
        var behaviorChanged = ordered.Any(x => x.Area != ActivityVersionChangeArea.Presentation);

        return ValueTask.FromResult(new ActivityVersionDiff(
            request.From,
            request.To,
            compatibility,
            requiredBump,
            behaviorChanged,
            new(
                request.FromProvider.ProviderKey,
                request.FromProvider.SchemaVersion,
                request.ToProvider.ProviderKey,
                request.ToProvider.SchemaVersion,
                !StringComparer.Ordinal.Equals(request.FromProvider.ProviderKey, request.ToProvider.ProviderKey) ||
                !StringComparer.Ordinal.Equals(request.FromProvider.SchemaVersion, request.ToProvider.SchemaVersion)),
            summary,
            ordered,
            diagnostics));
    }

    private static void CompareMembers<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes,
        Action<T, T, string, IDictionary<string, ActivityVersionChange>> compare,
        Func<T, string, ActivityVersionChange> added,
        Func<T, string, ActivityVersionChange> removed)
        where T : notnull
    {
        var beforeByKey = before.ToDictionary(MemberKey, StringComparer.Ordinal);
        var afterByKey = after.ToDictionary(MemberKey, StringComparer.Ordinal);
        foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!beforeByKey.TryGetValue(key, out var oldMember))
                Add(changes, added(afterByKey[key], memberKind));
            else if (!afterByKey.TryGetValue(key, out var newMember))
                Add(changes, removed(oldMember, memberKind));
            else
                compare(oldMember, newMember, memberKind, changes);
        }

        static string MemberKey(T member) => member switch
        {
            ActivityInputContract input => input.ReferenceKey,
            ActivityOutputContract output => output.ReferenceKey,
            ActivityOutcomeContract outcome => outcome.ReferenceKey,
            _ => throw new InvalidOperationException($"Unsupported activity contract member '{typeof(T)}'.")
        };
    }

    private static ActivityVersionChange AddedInput(ActivityInputContract member, string memberKind)
    {
        var breaking = member.IsRequired && member.Default is null;
        return MemberChange(member, memberKind, "MemberAdded", null, Project(member),
            breaking ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            breaking ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
            breaking
                ? $"Required input '{member.ReferenceKey}' was added without a compatible default."
                : $"Input '{member.ReferenceKey}' was added.");
    }

    private static ActivityVersionChange AddedOutput(ActivityOutputContract member, string memberKind) =>
        MemberChange(member, memberKind, "MemberAdded", null, Project(member),
            member.IsRequired ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            member.IsRequired ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
            member.IsRequired
                ? $"Required output '{member.ReferenceKey}' was added."
                : $"Optional output '{member.ReferenceKey}' was added.");

    private static ActivityVersionChange AddedOutcome(ActivityOutcomeContract member, string memberKind) =>
        MemberChange(member, memberKind, "MemberAdded", null, Project(member),
            member.IsEmitted ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            member.IsEmitted ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
            member.IsEmitted
                ? $"Emitted outcome '{member.ReferenceKey}' was added."
                : $"Non-emitted outcome '{member.ReferenceKey}' was added.");

    private static ActivityVersionChange RemovedMember<T>(T member, string memberKind) where T : notnull =>
        MemberChange(member, memberKind, "MemberRemoved", ProjectMember(member), null,
            ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
            $"{memberKind} '{ReferenceKey(member)}' was removed.");

    private static void CompareInput(
        ActivityInputContract before,
        ActivityInputContract after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes)
    {
        CompareCommon(before, after, memberKind, changes);
        CompareNullability(before, after, memberKind, changes);
        if (before.IsRequired != after.IsRequired)
        {
            var breaking = after.IsRequired && after.Default is null;
            Add(changes, MemberChange(after, memberKind, "RequirednessChanged", Project(before), Project(after),
                breaking ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
                breaking ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
                breaking
                    ? $"Input '{after.ReferenceKey}' changed from optional to required without a default."
                    : $"Input '{after.ReferenceKey}' requiredness changed compatibly."));
        }
        CompareDefault(before, after, memberKind, changes);
    }

    private static void CompareOutput(
        ActivityOutputContract before,
        ActivityOutputContract after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes)
    {
        CompareCommon(before, after, memberKind, changes);
        CompareNullability(before, after, memberKind, changes);
        if (before.IsRequired == after.IsRequired) return;
        var breaking = before.IsRequired && !after.IsRequired;
        Add(changes, MemberChange(after, memberKind, "RequirednessChanged", Project(before), Project(after),
            breaking ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            breaking ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
            $"Output '{after.ReferenceKey}' requiredness changed."));
    }

    private static void CompareNullability<T>(
        T before,
        T after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes)
        where T : notnull
    {
        var wasNullable = IsNullable(before);
        var isNullable = IsNullable(after);
        if (wasNullable == isNullable) return;
        var tightening = wasNullable && !isNullable;
        Add(changes, MemberChange(after, memberKind, "NullabilityChanged", ProjectMember(before), ProjectMember(after),
            tightening ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            tightening ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
            tightening
                ? $"{memberKind} '{ReferenceKey(after)}' nullability was tightened."
                : $"{memberKind} '{ReferenceKey(after)}' nullability was relaxed."));
    }

    private static void CompareOutcome(
        ActivityOutcomeContract before,
        ActivityOutcomeContract after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes)
    {
        if (!StringComparer.Ordinal.Equals(before.Name, after.Name))
            Add(changes, MemberChange(after, memberKind, "MemberRenamed", ProjectMember(before), ProjectMember(after), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"Outcome '{after.ReferenceKey}' was renamed."));
        if (before.IsEmitted != after.IsEmitted)
        {
            var breaking = after.IsEmitted;
            Add(changes, MemberChange(after, memberKind, "OutcomeEmissionChanged", Project(before), Project(after),
                breaking ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
                breaking ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
                $"Outcome '{after.ReferenceKey}' emission changed."));
        }
        if (!StringComparer.Ordinal.Equals(before.Description, after.Description))
            Add(changes, PresentationChange(after.ReferenceKey, memberKind, "DescriptionChanged", before.Description, after.Description));
    }

    private static void CompareCommon<T>(T before, T after, string memberKind, IDictionary<string, ActivityVersionChange> changes)
        where T : notnull
    {
        var key = ReferenceKey(after);
        var oldName = Name(before);
        var newName = Name(after);
        if (!StringComparer.Ordinal.Equals(oldName, newName))
            Add(changes, MemberChange(after, memberKind, "MemberRenamed", ProjectMember(before), ProjectMember(after), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"{memberKind} '{key}' was renamed."));

        var oldType = Type(before);
        var newType = Type(after);
        if (oldType != newType)
            Add(changes, MemberChange(after, memberKind, "TypeChanged", ProjectMember(before), ProjectMember(after), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"{memberKind} '{key}' changed type."));

        if (!StringComparer.Ordinal.Equals(StorageDriver(before), StorageDriver(after)))
            Add(changes, MemberChange(after, memberKind, "StorageDriverChanged", ProjectMember(before), ProjectMember(after), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"{memberKind} '{key}' changed storage driver.", ActivityVersionChangeArea.Durability));
        if (Durability(before) != Durability(after))
            Add(changes, MemberChange(after, memberKind, "DurabilityChanged", ProjectMember(before), ProjectMember(after), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"{memberKind} '{key}' changed durable-boundary policy.", ActivityVersionChangeArea.Durability));

        ComparePresentation(before, after, memberKind, changes);
    }

    private static void CompareDefault(
        ActivityInputContract before,
        ActivityInputContract after,
        string memberKind,
        IDictionary<string, ActivityVersionChange> changes)
    {
        if (before.Default is null && after.Default is null) return;
        if (before.Default is null)
            Add(changes, MemberChange(after, memberKind, "DefaultAdded", null, ProjectDefault(after.Default!), ActivityVersionChangeImpact.Additive, ActivityVersionBump.Minor,
                $"A default was added to input '{after.ReferenceKey}'.", ActivityVersionChangeArea.Default));
        else if (after.Default is null)
            Add(changes, MemberChange(after, memberKind, "DefaultRemoved", ProjectDefault(before.Default), null, ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"The default was removed from input '{after.ReferenceKey}'.", ActivityVersionChangeArea.Default));
        else if (!StringComparer.Ordinal.Equals(DefaultHash(before.Default), DefaultHash(after.Default)))
            Add(changes, MemberChange(after, memberKind, "DefaultChanged", ProjectDefault(before.Default), ProjectDefault(after.Default), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
                $"The default changed for input '{after.ReferenceKey}'.", ActivityVersionChangeArea.Default));
    }

    private static void ComparePresentation<T>(T before, T after, string memberKind, IDictionary<string, ActivityVersionChange> changes)
        where T : notnull
    {
        var key = ReferenceKey(after);
        AddPresentationIfChanged("DisplayNameChanged", DisplayName(before), DisplayName(after));
        AddPresentationIfChanged("DescriptionChanged", Description(before), Description(after));
        AddPresentationIfChanged("CategoryChanged", Category(before), Category(after));
        if (Order(before) != Order(after))
            Add(changes, PresentationChange(key, memberKind, "OrderChanged", Order(before), Order(after)));
        if (!StringComparer.Ordinal.Equals(UiHash(before), UiHash(after)))
            Add(changes, PresentationChange(key, memberKind, "UiMetadataChanged", UiSummary(before), UiSummary(after)));

        void AddPresentationIfChanged(string kind, string? oldValue, string? newValue)
        {
            if (!StringComparer.Ordinal.Equals(oldValue, newValue))
                Add(changes, PresentationChange(key, memberKind, kind, oldValue, newValue));
        }
    }

    private static void CompareProvider(
        ActivityProviderManifest before,
        ActivityProviderManifest after,
        IDictionary<string, ActivityVersionChange> changes)
    {
        if (!StringComparer.Ordinal.Equals(before.ProviderKey, after.ProviderKey))
            Add(changes, SimpleChange("provider:key:provider-changed", ActivityVersionChangeArea.Provider, "ProviderChanged",
                Element(new { key = before.ProviderKey, schemaVersion = before.SchemaVersion }),
                Element(new { key = after.ProviderKey, schemaVersion = after.SchemaVersion }),
                ActivityVersionChangeImpact.Additive, ActivityVersionBump.Minor, "The activity provider changed."));
        if (!StringComparer.Ordinal.Equals(before.SchemaVersion, after.SchemaVersion))
            Add(changes, SimpleChange("provider:schema:provider-schema-changed", ActivityVersionChangeArea.Provider, "ProviderSchemaChanged",
                Element(new { key = before.ProviderKey, schemaVersion = before.SchemaVersion }),
                Element(new { key = after.ProviderKey, schemaVersion = after.SchemaVersion }),
                ActivityVersionChangeImpact.Additive, ActivityVersionBump.Minor, "The provider manifest schema changed."));
    }

    private static void CompareImplementation(ActivityVersionDiffRequest request, IDictionary<string, ActivityVersionChange> changes)
    {
        var before = request.FromImplementation;
        var after = request.ToImplementation;
        var templateChanged = request.From.TemplateHash is not null && request.To.TemplateHash is not null &&
                              !StringComparer.Ordinal.Equals(request.From.TemplateHash, request.To.TemplateHash);
        var fingerprintChanged = before?.ProviderFingerprint is not null && after?.ProviderFingerprint is not null &&
                                 !StringComparer.Ordinal.Equals(before.ProviderFingerprint, after.ProviderFingerprint);
        if (templateChanged || fingerprintChanged)
            Add(changes, SimpleChange("implementation:template:implementation-changed", ActivityVersionChangeArea.Implementation, "ImplementationChanged",
                ImplementationProjection(request.From.TemplateHash, before), ImplementationProjection(request.To.TemplateHash, after),
                ActivityVersionChangeImpact.Additive, ActivityVersionBump.Minor, "The compiled activity implementation changed."));

        if (before?.RuntimeRequirements is not null && after?.RuntimeRequirements is not null)
            CompareRequirements(before.RuntimeRequirements, after.RuntimeRequirements, changes);
        if (!StringComparer.Ordinal.Equals(before?.LayoutHash, after?.LayoutHash) || before?.LayoutCount != after?.LayoutCount)
            Add(changes, SimpleChange("presentation:layout:layout-changed", ActivityVersionChangeArea.Presentation, "LayoutChanged",
                Element(new { layoutHash = before?.LayoutHash, layoutCount = before?.LayoutCount }),
                Element(new { layoutHash = after?.LayoutHash, layoutCount = after?.LayoutCount }),
                ActivityVersionChangeImpact.NonBehavioral, ActivityVersionBump.Patch, "The activity layout changed."));
    }

    private static void CompareRequirements(
        IReadOnlyList<ActivityRuntimeRequirementDeclaration> before,
        IReadOnlyList<ActivityRuntimeRequirementDeclaration> after,
        IDictionary<string, ActivityVersionChange> changes)
    {
        var oldByKey = GroupRequirements(before);
        var newByKey = GroupRequirements(after);
        foreach (var key in oldByKey.Keys.Union(newByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var idKey = IdSegment(key);
            if (!oldByKey.TryGetValue(key, out var oldSchemas))
                Add(changes, SimpleChange($"implementation:runtime-requirement:{idKey}:added", ActivityVersionChangeArea.Implementation, "RuntimeRequirementAdded", null,
                    RequirementProjection(key, newByKey[key]), ActivityVersionChangeImpact.Additive, ActivityVersionBump.Minor, $"Runtime requirement '{key}' was added."));
            else if (!newByKey.TryGetValue(key, out var newSchemas))
                Add(changes, SimpleChange($"implementation:runtime-requirement:{idKey}:removed", ActivityVersionChangeArea.Implementation, "RuntimeRequirementRemoved",
                    RequirementProjection(key, oldSchemas), null, ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major, $"Runtime requirement '{key}' was removed."));
            else if (oldSchemas.Length == 1 && newSchemas.Length == 1 && !StringComparer.Ordinal.Equals(oldSchemas[0], newSchemas[0]))
                Add(changes, SimpleChange($"implementation:runtime-requirement:{idKey}:schema-changed", ActivityVersionChangeArea.Implementation, "RuntimeRequirementSchemaChanged",
                    RequirementProjection(key, oldSchemas), RequirementProjection(key, newSchemas), ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major, $"Runtime requirement '{key}' changed schema."));
            else if (!oldSchemas.SequenceEqual(newSchemas, StringComparer.Ordinal))
            {
                var removedSchemas = oldSchemas.Except(newSchemas, StringComparer.Ordinal).ToArray();
                var breaking = removedSchemas.Length > 0;
                Add(changes, SimpleChange(
                    $"implementation:runtime-requirement:{idKey}:schema-set-changed",
                    ActivityVersionChangeArea.Implementation,
                    "RuntimeRequirementSchemaSetChanged",
                    RequirementProjection(key, oldSchemas),
                    RequirementProjection(key, newSchemas),
                    breaking ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
                    breaking ? ActivityVersionBump.Major : ActivityVersionBump.Minor,
                    $"Runtime requirement '{key}' changed its supported schema set."));
            }
        }

        static Dictionary<string, string[]> GroupRequirements(IReadOnlyList<ActivityRuntimeRequirementDeclaration> requirements) =>
            requirements
                .GroupBy(x => x.ConsumerKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(x => x.SchemaVersion).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

        static JsonElement RequirementProjection(string consumerKey, IReadOnlyList<string> schemas) =>
            Element(new { consumerKey, schemaVersions = schemas });
    }

    private static void CompareDependencies(
        IReadOnlyList<ActivityDependencyItem> before,
        IReadOnlyList<ActivityDependencyItem> after,
        IDictionary<string, ActivityVersionChange> changes)
    {
        var oldByOccurrence = before.ToDictionary(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal);
        var newByOccurrence = after.ToDictionary(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal);
        foreach (var occurrence in oldByOccurrence.Keys.Intersect(newByOccurrence.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var id = IdSegment(occurrence);
            var oldDependency = oldByOccurrence[occurrence];
            var newDependency = newByOccurrence[occurrence];
            if (!StringComparer.Ordinal.Equals(oldDependency.Dependency.VersionId, newDependency.Dependency.VersionId) ||
                !StringComparer.Ordinal.Equals(oldDependency.Dependency.TemplateHash, newDependency.Dependency.TemplateHash))
                Add(changes, DependencyChange($"dependency:{id}:{IdSegment(oldDependency.Dependency.VersionId ?? "unknown")}->{IdSegment(newDependency.Dependency.VersionId ?? "unknown")}",
                    "DependencyVersionChanged", oldDependency, newDependency, ActivityVersionBump.Minor));
        }

        var oldRemaining = oldByOccurrence.Values.Where(x => !newByOccurrence.ContainsKey(x.Occurrence.OccurrenceId)).ToList();
        var newRemaining = newByOccurrence.Values.Where(x => !oldByOccurrence.ContainsKey(x.Occurrence.OccurrenceId)).ToList();
        var oldGroups = oldRemaining.GroupBy(DependencyIdentity).ToDictionary(x => x.Key, x => x.ToArray());
        var newGroups = newRemaining.GroupBy(DependencyIdentity).ToDictionary(x => x.Key, x => x.ToArray());
        foreach (var identity in oldGroups.Keys.Intersect(newGroups.Keys).OrderBy(x => x.VersionId, StringComparer.Ordinal).ThenBy(x => x.TemplateHash, StringComparer.Ordinal))
        {
            if (oldGroups[identity] is not [var oldDependency] || newGroups[identity] is not [var newDependency]) continue;
            oldRemaining.Remove(oldDependency);
            newRemaining.Remove(newDependency);
            Add(changes, DependencyChange(
                $"dependency:{IdSegment(oldDependency.Occurrence.OccurrenceId)}->{IdSegment(newDependency.Occurrence.OccurrenceId)}:occurrence-moved",
                "DependencyOccurrenceMoved",
                oldDependency,
                newDependency,
                ActivityVersionBump.Minor));
        }

        foreach (var dependency in oldRemaining.OrderBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal))
            Add(changes, DependencyChange($"dependency:{IdSegment(dependency.Occurrence.OccurrenceId)}:removed", "DependencyRemoved", dependency, null, ActivityVersionBump.Minor));
        foreach (var dependency in newRemaining.OrderBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal))
            Add(changes, DependencyChange($"dependency:{IdSegment(dependency.Occurrence.OccurrenceId)}:added", "DependencyAdded", null, dependency, ActivityVersionBump.Minor));

        static (string? VersionId, string? TemplateHash) DependencyIdentity(ActivityDependencyItem item) =>
            (item.Dependency.VersionId, item.Dependency.TemplateHash);
    }

    private static void MergeProviderChanges(
        IReadOnlyList<ActivityVersionChange> providerChanges,
        IDictionary<string, ActivityVersionChange> changes)
    {
        foreach (var provider in providerChanges.OrderBy(x => x.ChangeId, StringComparer.Ordinal))
        {
            if (changes.TryGetValue(provider.ChangeId, out var platform))
            {
                var bump = Max(platform.RequiredBump, provider.RequiredBump);
                var impact = Max(platform.Impact, provider.Impact);
                changes[provider.ChangeId] = platform with
                {
                    RequiredBump = bump,
                    Impact = impact,
                    Message = bump > platform.RequiredBump || impact > platform.Impact ? provider.Message : platform.Message
                };
            }
            else
            {
                // Provider-supplied arbitrary JSON is not part of the trusted safe-projection surface.
                var isBehavioral = provider.Area != ActivityVersionChangeArea.Presentation;
                changes[provider.ChangeId] = provider with
                {
                    Before = null,
                    After = null,
                    Impact = isBehavioral ? Max(provider.Impact, ActivityVersionChangeImpact.Additive) : provider.Impact,
                    RequiredBump = isBehavioral ? Max(provider.RequiredBump, ActivityVersionBump.Minor) : provider.RequiredBump
                };
            }
        }
    }

    private static ActivityVersionChange DependencyChange(
        string id,
        string kind,
        ActivityDependencyItem? before,
        ActivityDependencyItem? after,
        ActivityVersionBump bump)
    {
        var current = after ?? before!;
        return new(
            id,
            ActivityVersionChangeArea.Dependency,
            kind,
            new(DependencyVersionId: after?.Dependency.VersionId, OccurrenceId: current.Occurrence.OccurrenceId),
            before is null ? null : Project(before.Dependency, before.Occurrence.OccurrenceId),
            after is null ? null : Project(after.Dependency, after.Occurrence.OccurrenceId),
            bump == ActivityVersionBump.Major ? ActivityVersionChangeImpact.Breaking : ActivityVersionChangeImpact.Additive,
            bump,
            $"Placed dependency '{current.Occurrence.OccurrenceId}' changed.");
    }

    private static ActivityVersionChange MemberChange<T>(
        T member,
        string memberKind,
        string kind,
        JsonElement? before,
        JsonElement? after,
        ActivityVersionChangeImpact impact,
        ActivityVersionBump bump,
        string message,
        ActivityVersionChangeArea? area = null) where T : notnull => new(
        $"{(area ?? MemberArea(member)).ToString().ToLowerInvariant()}:{memberKind.ToLowerInvariant()}:{IdSegment(ReferenceKey(member))}:{Kebab(kind)}",
        area ?? MemberArea(member),
        kind,
        new(memberKind, ReferenceKey(member)),
        before,
        after,
        impact,
        bump,
        message);

    private static ActivityVersionChange PresentationChange(string key, string memberKind, string kind, object? before, object? after) => new(
        $"presentation:{memberKind.ToLowerInvariant()}:{IdSegment(key)}:{Kebab(kind)}",
        ActivityVersionChangeArea.Presentation,
        kind,
        new(memberKind, key),
        Element(new { value = before }),
        Element(new { value = after }),
        ActivityVersionChangeImpact.NonBehavioral,
        ActivityVersionBump.Patch,
        $"{memberKind} '{key}' presentation changed.");

    private static ActivityVersionChange SimpleChange(
        string id,
        ActivityVersionChangeArea area,
        string kind,
        JsonElement? before,
        JsonElement? after,
        ActivityVersionChangeImpact impact,
        ActivityVersionBump bump,
        string message) => new(id, area, kind, new(), before, after, impact, bump, message);

    private static void Add(IDictionary<string, ActivityVersionChange> changes, ActivityVersionChange change) =>
        changes.Add(change.ChangeId, change);

    private static JsonElement Project(ActivityInputContract member) => Element(new
    {
        member.Name,
        type = TypeProjection(member.Type),
        member.IsRequired,
        member.IsNullable,
        hasDefault = member.Default is not null,
        defaultHash = member.Default is null ? null : DefaultHash(member.Default),
        member.StorageDriverKey,
        member.Durability
    });

    private static JsonElement Project(ActivityOutputContract member) => Element(new
    {
        member.Name,
        type = TypeProjection(member.Type),
        member.IsRequired,
        member.IsNullable,
        member.StorageDriverKey,
        member.Durability
    });

    private static JsonElement Project(ActivityOutcomeContract member) => Element(new { member.Name, member.IsEmitted });

    private static JsonElement ProjectMember<T>(T member) => member switch
    {
        ActivityInputContract input => Project(input),
        ActivityOutputContract output => Project(output),
        ActivityOutcomeContract outcome => Project(outcome),
        _ => throw new InvalidOperationException($"Unsupported member '{typeof(T)}'.")
    };

    private static JsonElement ProjectDefault(ActivityInputDefault value) => Element(new
    {
        value.Syntax,
        valueHash = DefaultHash(value)
    });

    private static JsonElement Project(ActivityDefinitionReference reference, string occurrenceId) => Element(new
    {
        reference.DefinitionId,
        reference.VersionId,
        reference.Version,
        reference.TemplateHash,
        occurrenceId
    });

    private static object TypeProjection(TypeReference type) => new
    {
        type.Alias,
        collectionKind = type.CollectionKind.ToString()
    };

    private static JsonElement ImplementationProjection(string? templateHash, ActivityVersionImplementationFacts? facts) => Element(new
    {
        templateHash,
        facts?.ProviderFingerprint,
        facts?.NodeCount,
        facts?.ResumeTargetCount
    });

    private static ActivityVersionChangeArea MemberArea<T>(T member) => member is ActivityOutcomeContract
        ? ActivityVersionChangeArea.Outcome
        : ActivityVersionChangeArea.Contract;

    private static string ReferenceKey<T>(T member) => member switch
    {
        ActivityInputContract value => value.ReferenceKey,
        ActivityOutputContract value => value.ReferenceKey,
        ActivityOutcomeContract value => value.ReferenceKey,
        _ => throw new InvalidOperationException($"Unsupported member '{typeof(T)}'.")
    };

    private static string Name<T>(T member) => member switch
    {
        ActivityInputContract value => value.Name,
        ActivityOutputContract value => value.Name,
        _ => throw new InvalidOperationException()
    };

    private static TypeReference Type<T>(T member) => member switch
    {
        ActivityInputContract value => value.Type,
        ActivityOutputContract value => value.Type,
        _ => throw new InvalidOperationException()
    };

    private static string StorageDriver<T>(T member) => member switch
    {
        ActivityInputContract value => value.StorageDriverKey,
        ActivityOutputContract value => value.StorageDriverKey,
        _ => throw new InvalidOperationException()
    };

    private static ActivityBoundaryDurability Durability<T>(T member) => member switch
    {
        ActivityInputContract value => value.Durability,
        ActivityOutputContract value => value.Durability,
        _ => throw new InvalidOperationException()
    };

    private static bool IsNullable<T>(T member) => member switch
    {
        ActivityInputContract value => value.IsNullable,
        ActivityOutputContract value => value.IsNullable,
        _ => throw new InvalidOperationException()
    };

    private static string? DisplayName<T>(T member) => member switch
    {
        ActivityInputContract value => value.DisplayName,
        ActivityOutputContract value => value.DisplayName,
        _ => null
    };

    private static string? Description<T>(T member) => member switch
    {
        ActivityInputContract value => value.Description,
        ActivityOutputContract value => value.Description,
        _ => null
    };

    private static string? Category<T>(T member) => member switch
    {
        ActivityInputContract value => value.Category,
        ActivityOutputContract value => value.Category,
        _ => null
    };

    private static float Order<T>(T member) => member switch
    {
        ActivityInputContract value => value.Order,
        ActivityOutputContract value => value.Order,
        _ => 0
    };

    private static string? UiHash<T>(T member) => member switch
    {
        ActivityInputContract value => HashUi(value.UiHint, value.UiSpecifications),
        ActivityOutputContract value => HashUi(value.UiHint, value.UiSpecifications),
        _ => null
    };

    private static object UiSummary<T>(T member) => member switch
    {
        ActivityInputContract value => new { value.UiHint, specificationsHash = HashElement(value.UiSpecifications) },
        ActivityOutputContract value => new { value.UiHint, specificationsHash = HashElement(value.UiSpecifications) },
        _ => new { }
    };

    private static string DefaultHash(ActivityInputDefault value) => Hash($"{value.Syntax}\u001f{CanonicalJson(value.Value)}");

    private static string? HashUi(string? hint, JsonElement? specifications) =>
        hint is null && specifications is null ? null : Hash($"{hint}\u001f{(specifications is null ? null : CanonicalJson(specifications.Value))}");

    private static string? HashElement(JsonElement? value) => value is null ? null : Hash(CanonicalJson(value.Value));

    private static string Hash(string value) => $"sha256-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string Kebab(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $"-{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static string IdSegment(string value)
    {
        var normalized = string.Concat(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? char.ToLowerInvariant(character) : '-'));
        return StringComparer.Ordinal.Equals(normalized, value) ? normalized : $"{normalized}-{Hash(value)[..15]}";
    }

    private static ActivityVersionBump Max(ActivityVersionBump left, ActivityVersionBump right) => left > right ? left : right;
    private static ActivityVersionChangeImpact Max(ActivityVersionChangeImpact left, ActivityVersionChangeImpact right) => left > right ? left : right;
}
