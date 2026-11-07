using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A persisted external stimulus, independent of any publishing workflow.</summary>
/// <remarks>
/// This is a proposed provider wire contract. No routing path enables it until the selected physical
/// source, target admission and retention participants have been qualified together. Neither this
/// record nor its client key grants delivery authority; delivery rereads the canonical row and claim.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressSource
{
    public const int CurrentVersion = 1;

    [JsonConstructor]
    public StimulusIngressSource(int version, string sourceId, StimulusDeliveryScope originScope,
        string? clientKey, StimulusIngressRequest request, DateTimeOffset acceptedAt,
        StimulusIngressCohort cohort)
    {
        if (version != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(originScope);
        if (clientKey is not null) ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        ArgumentNullException.ThrowIfNull(request);
        if (acceptedAt == default || acceptedAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));
        ArgumentNullException.ThrowIfNull(cohort);
        if (cohort.Scope != originScope.PersistenceScope || cohort.SelectedAt > acceptedAt)
            throw new ArgumentException("The captured cohort has a foreign scope or selection after admission.", nameof(cohort));
        if (request.Mode == StimulusRoutingMode.ResumeOnly && cohort.Starts.Count != 0 ||
            request.Mode == StimulusRoutingMode.StartOnly && cohort.Resumes.Count != 0 ||
            request.StartKeyScope == StimulusStartKeyScope.Occurrence && (clientKey is null || cohort.Starts.Count > 1))
            throw new ArgumentException("The cohort contradicts its routing mode or start-key scope.", nameof(cohort));
        var legacyMode = request.ResumeAllMatchingBookmarks ? StimulusIngressLegacyMode.AllMatches : StimulusIngressLegacyMode.SingleMatch;
        if (cohort.Legacy.Any(resolution => resolution.Mode != legacyMode))
            throw new ArgumentException("Legacy resolution contradicts the request's declared cardinality.", nameof(cohort));
        if (cohort.Starts.Any(start => start.Binding.StimulusType != request.StimulusType ||
                start.Binding.StimulusHash != request.StimulusHash || request.SelectionOwnerTriggerBindingId is { } owner &&
                start.Binding.TriggerBindingId != owner) ||
            cohort.Resumes.Any(resume => resume.StimulusType != request.StimulusType || resume.StimulusHash != request.StimulusHash ||
                request.TargetWorkflowExecutionId is { } target && resume.WorkflowExecutionId != target ||
                request.CorrelationId is { } correlation &&
                    (!resume.RegistrationCapture!.Metadata.TryGetValue(RuntimeMetadataKeys.CorrelationId, out var retained) || retained != correlation)))
            throw new ArgumentException("The cohort does not match the source's declared routing identity.", nameof(cohort));
        Version = version;
        SourceId = sourceId;
        OriginScope = originScope;
        ClientKey = clientKey;
        Request = request;
        AcceptedAt = acceptedAt;
        Cohort = cohort;
    }

    public int Version { get; }
    public string SourceId { get; }
    /// <summary>Immutable trusted origin, retained even when selection is explicitly empty.</summary>
    public StimulusDeliveryScope OriginScope { get; }
    [JsonIgnore]
    public PersistenceScope Scope => OriginScope.PersistenceScope;
    public string? ClientKey { get; }
    [JsonIgnore]
    public string RequestFingerprint => RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(Request);
    /// <summary>Versioned routing data only; live dispatch options and ambient services are never serialized.</summary>
    public StimulusIngressRequest Request { get; }
    public DateTimeOffset AcceptedAt { get; }
    public StimulusIngressCohort Cohort { get; }

    /// <summary>Derive ordinary keyed start responsibility from this committed source, never a fake workflow.</summary>
    public KeyedWorkflowStartIdentity GetStartIdentity(string triggerBindingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerBindingId);
        var start = Cohort.Starts.SingleOrDefault(candidate => candidate.Binding.TriggerBindingId == triggerBindingId)
            ?? throw new ArgumentException("The source did not select that start binding.", nameof(triggerBindingId));
        if (ClientKey is null)
            return KeyedWorkflowStartIdentity.ForIngressSource(OriginScope, SourceId, start.Binding.TriggerBindingId, start.Executable.ArtifactId);
        if (OriginScope.RunKind != WorkflowRunKind.PublishedRun || OriginScope.TestScope is not null)
            return KeyedWorkflowStartIdentity.ForScopedIngressKey(OriginScope, ClientKey,
                Request.StartKeyScope == StimulusStartKeyScope.Occurrence ? null : start.Executable.ArtifactId);
        // Existing published caller-keyed routing preserves its frozen ordinary derivation.
        return Request.StartKeyScope == StimulusStartKeyScope.Occurrence
            ? KeyedWorkflowStartIdentity.ForOccurrence(ClientKey)
            : KeyedWorkflowStartIdentity.For(ClientKey, start.Executable.ArtifactId);
    }

    /// <summary>Exact start/resume execution dependencies protected with the original source install.</summary>
    public IReadOnlyCollection<string> GetExecutionDependencies() => Array.AsReadOnly(Cohort.Resumes
        .Select(recipient => recipient.WorkflowExecutionId)
        .Concat(Cohort.Starts.Select(recipient => GetStartIdentity(recipient.Binding.TriggerBindingId).WorkflowExecutionId))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    /// <summary>Original material roots; normal material closure and source retirement remain authoritative.</summary>
    public IReadOnlyCollection<WorkflowExecutableIdentity> GetPinnedExecutables()
    {
        var pins = Cohort.Starts.Select(start => start.Executable)
            .Concat(Cohort.Resumes.Select(resume => resume.RegistrationCapture!.Executable))
            .Distinct().OrderBy(pin => pin.ArtifactId, StringComparer.Ordinal).ToArray();
        if (pins.GroupBy(pin => pin.ArtifactId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("A canonical source cannot assign conflicting executable pins to one artifact.");
        return Array.AsReadOnly(pins);
    }

    /// <summary>Compare every origin fact before keyed reuse; this is not independent authorization.</summary>
    public bool MatchesOrigin(StimulusDeliveryScope expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return OriginScope.PersistenceScope == expected.PersistenceScope && OriginScope.TenantId == expected.TenantId &&
            OriginScope.Partition == expected.Partition && OriginScope.RunKind == expected.RunKind &&
            WorkflowTestScope.ContextEquals(OriginScope.TestScope, expected.TestScope);
    }


}

/// <summary>The complete pre-effect selection, including explicit empty sets and legacy ambiguity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressCohort
{
    [JsonConstructor]
    public StimulusIngressCohort(PersistenceScope scope, DateTimeOffset selectedAt,
        IReadOnlyList<StimulusStartRecipient> starts, IReadOnlyList<StimulusResumeRecipient> resumes,
        IReadOnlyList<StimulusIngressConsumer> consumers, IReadOnlyList<StimulusIngressLegacyResolution> legacy)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (selectedAt == default || selectedAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(selectedAt));
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(resumes);
        ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(legacy);
        if (starts.Any(start => start is null || !start.SourceProvenanceCaptured) ||
            resumes.Any(resume => resume?.RegistrationCapture is not { } capture || capture.Scope.PersistenceScope != scope))
            throw new ArgumentException("Ingress recipients require complete original captures in the selected scope.");
        if (starts.Select(start => start.Binding.TriggerBindingId).Distinct(StringComparer.Ordinal).Count() != starts.Count ||
            resumes.Select(resume => (resume.WorkflowExecutionId, resume.BookmarkId)).Distinct().Count() != resumes.Count)
            throw new ArgumentException("An ingress cohort cannot repeat a start binding or physical wait.");
        if (consumers.Any(consumer => consumer is null) ||
            consumers.Select(consumer => (consumer.WorkflowExecutionId, consumer.InvocationId)).Distinct().Count() != consumers.Count ||
            legacy.Any(resolution => resolution is null) ||
            legacy.Select(resolution => resolution.WorkflowExecutionId).Distinct(StringComparer.Ordinal).Count() != legacy.Count)
            throw new ArgumentException("A consumer or legacy resolution can occur only once.");
        var captured = resumes.ToDictionary(resume => (resume.WorkflowExecutionId, resume.BookmarkId));
        var receivers = consumers.ToDictionary(consumer => (consumer.WorkflowExecutionId, consumer.InvocationId));
        foreach (var consumer in consumers)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { consumer.InvocationId };
            var current = consumer;
            while (current.ParentInvocationId is { } parentId)
            {
                if (!seen.Add(parentId) || !receivers.TryGetValue((consumer.WorkflowExecutionId, parentId), out var parent) ||
                    parent.Executable != consumer.Executable || !SameScope(parent.Scope, consumer.Scope))
                    throw new ArgumentException("Captured trigger ownership requires an acyclic, same-execution receiver chain.");
                var parentSlots = current.Slots.Select(slot => parent.Slots.SingleOrDefault(candidate => candidate.BookmarkId == slot.BookmarkId))
                    .ToArray();
                if (parentSlots.Any(slot => slot is null) || parentSlots.Select(slot =>
                        (slot!.VisitId, slot!.TriggerOperationId, slot!.TriggerActivationId)).Distinct().Count() != 1)
                    throw new ArgumentException("A nested receiver must belong to one exact enclosing trigger activation.");
                current = parent;
            }
        }
        var memberships = new Dictionary<(string, string), List<StimulusIngressConsumer>>();
        foreach (var consumer in consumers)
        foreach (var slot in consumer.Slots)
        {
            var key = (consumer.WorkflowExecutionId, slot.BookmarkId);
            if (!captured.TryGetValue(key, out var resume) ||
                resume.ActivityExecutionId != slot.ActivityExecutionId ||
                resume.RegistrationCapture!.Generation != slot.Generation ||
                resume.RegistrationCapture.Executable != consumer.Executable ||
                !SameScope(resume.RegistrationCapture.Scope, consumer.Scope))
                throw new ArgumentException("A tracked slot must name one exact captured wait and executable.");
            if (!memberships.TryGetValue(key, out var owners)) memberships.Add(key, owners = []);
            if (owners.Any(owner => !IsAncestor(owner, consumer) && !IsAncestor(consumer, owner)))
                throw new ArgumentException("One physical wait cannot belong to unrelated receiver invocations.");
            owners.Add(consumer);
        }
        var assigned = memberships.Keys.ToHashSet();
        foreach (var resolution in legacy)
        {
            var all = resumes.Where(resume => resume.WorkflowExecutionId == resolution.WorkflowExecutionId)
                .Select(resume => resume.BookmarkId).ToArray();
            if (all.Length == 0 || all.All(bookmark => assigned.Contains((resolution.WorkflowExecutionId, bookmark))))
                throw new ArgumentException("A wholly tracked execution has no legacy branch.");
            // Ordinary instance lookup does not apply the global correlation filter. Its original
            // match set can therefore contain additional waits that are not routed recipients.
            // Capturing that full set is required to preserve its original ambiguity result.
            if (all.Any(bookmark => !resolution.OriginalBookmarkIds.Contains(bookmark, StringComparer.Ordinal)))
                throw new ArgumentException("Legacy resolution must include every routed original wait.");
            foreach (var bookmark in resolution.BookmarkIdsToResume)
                if (!captured.ContainsKey((resolution.WorkflowExecutionId, bookmark)) ||
                    !assigned.Add((resolution.WorkflowExecutionId, bookmark)))
                    throw new ArgumentException("A legacy dispatch must identify an unassigned original wait.");
        }
        // An ambiguous legacy set deliberately has no dispatch. Every other original wait must
        // belong to a tracked consumer or the retained legacy decision, never an implicit fallback.
        foreach (var resume in resumes)
            if (!assigned.Contains((resume.WorkflowExecutionId, resume.BookmarkId)) &&
                !legacy.Any(resolution => resolution.WorkflowExecutionId == resume.WorkflowExecutionId &&
                    resolution.Status == StimulusIngressLegacyStatus.Ambiguous &&
                    resolution.OriginalBookmarkIds.Contains(resume.BookmarkId, StringComparer.Ordinal)))
                throw new ArgumentException("The cohort has an unaccounted original wait.");
        Scope = scope;
        SelectedAt = selectedAt;
        Starts = Array.AsReadOnly(starts.ToArray());
        Resumes = Array.AsReadOnly(resumes.ToArray());
        Consumers = Array.AsReadOnly(consumers.ToArray());
        Legacy = Array.AsReadOnly(legacy.ToArray());

        bool IsAncestor(StimulusIngressConsumer ancestor, StimulusIngressConsumer descendant)
        {
            var current = descendant;
            while (current.ParentInvocationId is { } parentId)
            {
                if (parentId == ancestor.InvocationId) return true;
                current = receivers[(descendant.WorkflowExecutionId, parentId)];
            }
            return false;
        }
    }

    public PersistenceScope Scope { get; }
    public DateTimeOffset SelectedAt { get; }
    [JsonIgnore]
    public string Fingerprint => RuntimeCheckpointCommitFingerprint.ComputeIngressCohort(this);
    public IReadOnlyList<StimulusStartRecipient> Starts { get; }
    public IReadOnlyList<StimulusResumeRecipient> Resumes { get; }
    public IReadOnlyList<StimulusIngressConsumer> Consumers { get; }
    public IReadOnlyList<StimulusIngressLegacyResolution> Legacy { get; }

    private static bool SameScope(StimulusDeliveryScope first, StimulusDeliveryScope second) =>
        first.PersistenceScope == second.PersistenceScope && first.TenantId == second.TenantId &&
        first.Partition == second.Partition && first.RunKind == second.RunKind && first.TestScope == second.TestScope;
}

/// <summary>One exact opted-in receiver and its complete captured source slots.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressConsumer
{
    [JsonConstructor]
    public StimulusIngressConsumer(string workflowExecutionId, string invocationId,
        WorkflowExecutableIdentity executable, StimulusDeliveryScope scope, string protocol, int protocolVersion,
        IReadOnlyList<StimulusIngressSlot> slots, string? parentInvocationId = null)
    {
        foreach (var value in new[] { workflowExecutionId, invocationId, protocol })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(scope);
        if (parentInvocationId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parentInvocationId);
            if (parentInvocationId == invocationId) throw new ArgumentException("A receiver cannot enclose itself.", nameof(parentInvocationId));
        }
        foreach (var value in new[] { executable.ArtifactId, executable.DefinitionId, executable.DefinitionVersionId,
                     executable.ArtifactVersion, executable.ArtifactHash }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (protocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0 || slots.Any(slot => slot is null) ||
            slots.Select(slot => slot.SlotId).Distinct(StringComparer.Ordinal).Count() != slots.Count ||
            slots.Select(slot => slot.BookmarkId).Distinct(StringComparer.Ordinal).Count() != slots.Count)
            throw new ArgumentException("A consumer requires a nonempty, unique original slot set.", nameof(slots));
        WorkflowExecutionId = workflowExecutionId;
        InvocationId = invocationId;
        Executable = executable;
        Scope = scope;
        Protocol = protocol;
        ProtocolVersion = protocolVersion;
        Slots = Array.AsReadOnly(slots.ToArray());
        ParentInvocationId = parentInvocationId;
    }

    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public StimulusDeliveryScope Scope { get; }
    public string Protocol { get; }
    public int ProtocolVersion { get; }
    public IReadOnlyList<StimulusIngressSlot> Slots { get; }
    /// <summary>Enclosing trigger receiver, from committed native lineage; absent outside a nested trigger subtree.</summary>
    public string? ParentInvocationId { get; }
}

/// <summary>Captured trigger ownership; providers validate it against committed native structure before effects.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressSlot
{
    [JsonConstructor]
    public StimulusIngressSlot(string slotId, string bookmarkId, string activityExecutionId, long generation,
        string visitId, string triggerOperationId, string triggerActivationId)
    {
        foreach (var value in new[] { slotId, bookmarkId, activityExecutionId, visitId, triggerOperationId, triggerActivationId })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        SlotId = slotId;
        BookmarkId = bookmarkId;
        ActivityExecutionId = activityExecutionId;
        Generation = generation;
        VisitId = visitId;
        TriggerOperationId = triggerOperationId;
        TriggerActivationId = triggerActivationId;
    }

    public string SlotId { get; }
    public string BookmarkId { get; }
    public string ActivityExecutionId { get; }
    public long Generation { get; }
    public string VisitId { get; }
    public string TriggerOperationId { get; }
    public string TriggerActivationId { get; }
}

public enum StimulusIngressLegacyStatus { Resolved, Ambiguous }
public enum StimulusIngressLegacyMode { SingleMatch, AllMatches }

/// <summary>A legacy decision made against the full match set, before removing opted-in recipients.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressLegacyResolution
{
    [JsonConstructor]
    public StimulusIngressLegacyResolution(string workflowExecutionId, StimulusIngressLegacyMode mode, StimulusIngressLegacyStatus status,
        IReadOnlyList<string> originalBookmarkIds, IReadOnlyList<string> bookmarkIdsToResume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(originalBookmarkIds);
        ArgumentNullException.ThrowIfNull(bookmarkIdsToResume);
        foreach (var value in originalBookmarkIds.Concat(bookmarkIdsToResume)) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (originalBookmarkIds.Count == 0 ||
            originalBookmarkIds.Distinct(StringComparer.Ordinal).Count() != originalBookmarkIds.Count ||
            bookmarkIdsToResume.Distinct(StringComparer.Ordinal).Count() != bookmarkIdsToResume.Count ||
            bookmarkIdsToResume.Any(id => !originalBookmarkIds.Contains(id, StringComparer.Ordinal)) ||
            status == StimulusIngressLegacyStatus.Ambiguous && bookmarkIdsToResume.Count != 0 ||
            mode == StimulusIngressLegacyMode.SingleMatch &&
                (originalBookmarkIds.Count > 1) != (status == StimulusIngressLegacyStatus.Ambiguous) ||
            mode == StimulusIngressLegacyMode.AllMatches && status == StimulusIngressLegacyStatus.Ambiguous)
            throw new ArgumentException("Legacy dispatch must preserve its original match set and ambiguity.");
        WorkflowExecutionId = workflowExecutionId;
        Status = status;
        Mode = mode;
        OriginalBookmarkIds = Array.AsReadOnly(originalBookmarkIds.ToArray());
        BookmarkIdsToResume = Array.AsReadOnly(bookmarkIdsToResume.ToArray());
    }

    public string WorkflowExecutionId { get; }
    public StimulusIngressLegacyStatus Status { get; }
    public StimulusIngressLegacyMode Mode { get; }
    /// <summary>All ordinary instance-lookup matches, including those outside global correlation filtering.</summary>
    public IReadOnlyList<string> OriginalBookmarkIds { get; }
    public IReadOnlyList<string> BookmarkIdsToResume { get; }
}
