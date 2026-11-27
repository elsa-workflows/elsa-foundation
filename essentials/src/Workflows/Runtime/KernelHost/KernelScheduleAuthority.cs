using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;
using static Elsa.Workflows.Runtime.Core.StandaloneTriggers.RuntimeKernelScheduleControl;
using WorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHost;

internal static class KernelScheduleAuthority
{
    // The built-in Actor Foundation limits StartSpec.TypeData and SetHeader to 16 KiB of UTF-8.
    private const int MaxTypeDataBytes = 16 * 1024;
    private const int LegacyAuthorityVersion = 1;
    private const int AuthorityVersion = 2;

    /// <summary>Builds a managed header from the selected committed control and immutable publication material.</summary>
    /// <returns>Null when the control has been released or its exact source or artifact no longer matches.</returns>
    internal static async ValueTask<ScheduleHeader?> BindAsync(RuntimeKernelScheduleControl control, TriggerScope scope, BoundScope actors,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutableStore executables, KernelHostCapabilities capabilities,
        ICronCalendar? calendar, IPersistenceAccessContextAccessor access, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        RequireBoundScope(scope, actors);
        if (access.Current.RequireScope() != scope.Persistence)
            throw new InvalidOperationException("A schedule must read its source in the selected Runtime publication scope.");
        if (CanonicalScope(control.Scope) != CanonicalScope(scope) || control is not { Revision: > 0, Owner: { } owner } ||
            !MatchesClaim(control.ScheduleKey, control.DefinitionId, control.TriggerNode, owner)) return null;

        var source = await references.FindAsync(owner.SourceReferenceId, cancellationToken);
        if (source is not { Scope: WorkflowExecutableReferenceScope.Published } ||
            source.SourceReferenceId != owner.SourceReferenceId || source.ActivationId != owner.ActivationId || source.SlotId != owner.SlotId ||
            source.TenantId != control.Scope.TenantId || source.PublicationScope is { } publishedScope && publishedScope != scope.Persistence ||
            source.ArtifactId != owner.Executable.ArtifactId || source.DefinitionId != owner.Executable.DefinitionId ||
            source.DefinitionVersionId != owner.Executable.DefinitionVersionId || source.ArtifactVersion != owner.Executable.ArtifactVersion ||
            !source.IsLive(timeProvider.GetUtcNow())) return null;

        var executable = await executables.FindAsync(owner.Executable.ArtifactId, cancellationToken);
        if (executable is null || executable.Identity.ArtifactId != owner.Executable.ArtifactId ||
            executable.Identity.ArtifactHash != owner.Executable.ArtifactHash) return null;
        var workflow = NativeExecutableMapper.Map(executable, capabilities, control.DefinitionId);
        var schedules = NativeSchedules.ForPublications([workflow], calendar is null ? null : expression => CronError(calendar, expression));
        var schedule = schedules.All.SingleOrDefault(item => item.Workflow == owner.Executable.ArtifactId &&
            item.Definition == control.DefinitionId && item.Node == control.TriggerNode && item.Key == control.ScheduleKey);
        if (schedule is null || owner.Continuity is { } continuity &&
            continuity.Stimulus != KernelScheduleClaim.Stimulus(schedule)) return null;

        var start = JsonSerializer.Serialize(WorkflowActor.Start(schedule.Start() with { ExecutableSelection = workflow.ExecutableSelection }));
        var authority = new Authority(AuthorityVersion, control.Scope, actors.Tenant, control.ScheduleKey, control.TriggerNode, owner, start);
        var header = new ScheduleHeader(new(schedule.Kind, schedule.Value), schedule.StartKey, control.Revision)
        {
            Authority = JsonSerializer.Serialize(authority),
            Continuity = owner.Continuity?.Stamp,
            Cutover = owner.Continuity?.FoundationActivatedAt.ToUnixTimeMilliseconds() ?? 0
        };
        if (!FitsTypeData(JsonSerializer.Serialize(header)))
            throw new MetadataLimitExceeded(nameof(ScheduleHeader.Authority), MaxTypeDataBytes);
        return header;
    }

    internal static RuntimeKernelScheduleOccurrence? ReadOffer(ScheduleOccurrence offer, TriggerScope scope, BoundScope actors) =>
        ReadOffer(offer, scope, actors, out _, out _);

    internal static RuntimeKernelScheduleOccurrence? ReadOffer(ScheduleOccurrence offer, TriggerScope scope, BoundScope actors,
        out string? continuity, out WorkflowExecutableIdentity? material)
    {
        continuity = null;
        material = null;
        if (offer.Tenant != actors.Tenant || !IsReference(offer.Schedule.Id) ||
            offer.TimerDeliveryId is not { Length: 65 } timer || timer[0] != '~' || timer.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") >= 0 ||
            offer.ArmGeneration <= 0 || offer.DueAtUnixMilliseconds <= 0 || offer.DueAtUnixMilliseconds >= offer.OfferUntilUnixMilliseconds ||
            offer.OfferUntilUnixMilliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() ||
            offer.Authority is not { Length: > 0 } payload || !FitsTypeData(payload)) return null;

        try
        {
            if (JsonSerializer.Deserialize<Authority>(payload) is not
                    { Version: LegacyAuthorityVersion or AuthorityVersion, Scope: { Persistence: not null } exactScope,
                      Owner: { Executable: { } executable } owner } authority ||
                CanonicalScope(exactScope) != CanonicalScope(scope) || authority.FoundationTenant != actors.Tenant ||
                !MatchesClaim(authority.ScheduleKey, executable.DefinitionId, authority.TriggerNode, owner) ||
                ReadPinnedStart(authority.PinnedStart, authority.FoundationTenant, executable, authority.TriggerNode, authority.ScheduleKey,
                    out material) is null || (authority.Version == AuthorityVersion) != (material is not null))
                return null;

            if (owner.Continuity is { } span)
            {
                if (!IsReference(span.Stamp) || !IsReference(span.Stimulus) || span.FoundationActivatedAt <= DateTimeOffset.UnixEpoch)
                    return null;
                continuity = span.Stamp;
            }
            return new(exactScope, authority.ScheduleKey, offer.Schedule.Id, owner.SlotName, owner.ActivationId,
                owner.PublicationRevision, offer.ArmGeneration, timer, DateTimeOffset.FromUnixTimeMilliseconds(offer.DueAtUnixMilliseconds),
                DateTimeOffset.FromUnixTimeMilliseconds(offer.OfferUntilUnixMilliseconds), executable, authority.TriggerNode, authority.PinnedStart);
        }
        catch (Exception invalid) when (invalid is JsonException or ArgumentException) { return null; }
    }

    internal static void RequireBoundScope(TriggerScope scope, BoundScope actors)
    {
        var tenant = scope.TenantId ?? PersistenceScope.DefaultValue;
        if (scope.Persistence.Value != tenant || actors.Tenant != tenant)
            throw new ArgumentException("The Runtime publication scope and Actor Foundation tenant must match.", nameof(scope));
    }

    private static bool MatchesClaim(string scheduleKey, string definition, string triggerNode, RuntimeKernelScheduleControlOwner owner) =>
        !string.IsNullOrWhiteSpace(definition) && !string.IsNullOrWhiteSpace(triggerNode) &&
        scheduleKey == $"schedule:{definition}:{triggerNode}" &&
        owner.Executable is { } executable && executable.DefinitionId == definition &&
        !string.IsNullOrWhiteSpace(executable.ArtifactId) && !string.IsNullOrWhiteSpace(executable.ArtifactHash) &&
        !string.IsNullOrWhiteSpace(executable.DefinitionVersionId) && !string.IsNullOrWhiteSpace(executable.ArtifactVersion) &&
        !string.IsNullOrWhiteSpace(owner.SlotName) && !string.IsNullOrWhiteSpace(owner.ActivationId) &&
        owner.PublicationRevision > 0 && owner.SlotId == WorkflowActivationSlotIdentity.Create(definition, owner.SlotName) &&
        owner.SourceReferenceId == WorkflowActivationReferenceIdentity.Create(owner.ActivationId);

    // Verify the pinned start structurally and retain the original bytes for immutable receipt replay.
    internal static StartSpec? ReadPinnedStart(string? pinned, string tenant, WorkflowExecutableIdentity executable, string triggerNode, string scheduleKey) =>
        ReadPinnedStart(pinned, tenant, executable, triggerNode, scheduleKey, out _);

    private static StartSpec? ReadPinnedStart(string? pinned, string tenant, WorkflowExecutableIdentity executable, string triggerNode,
        string scheduleKey, out WorkflowExecutableIdentity? material)
    {
        material = null;
        if (pinned is not { Length: > 0 } || !FitsTypeData(pinned) ||
            JsonSerializer.Deserialize<StartSpec>(pinned) is not
                { Type: WorkflowActor.ActorType, TypeData.Length: > 0, Input: { } input, Ephemeral: false, History: History.Full } start)
            return null;
        using var typeData = JsonDocument.Parse(start.TypeData);
        var header = typeData.RootElement;
        if (header.ValueKind != JsonValueKind.Object ||
            header.TryGetProperty("Version", out var version) && version.ValueKind != JsonValueKind.Null ||
            header.TryGetProperty("Ephemeral", out var ephemeral) && ephemeral.ValueKind != JsonValueKind.False)
            return null;
        if (header.TryGetProperty("ExecutableSelection", out var selection))
        {
            if (selection.ValueKind != JsonValueKind.String ||
                NativeExecutableMapper.ReadMaterialSelection(selection.GetString(), executable.ArtifactId) is not { } canonical ||
                canonical.ArtifactHash != executable.ArtifactHash) return null;
            material = canonical;
        }
        var run = WorkflowActor.DescribeRun(new(default, start.Type, tenant, ActorStatus.Running,
            null, null, null, start.TypeData, new Dictionary<string, string>()));
        var matches = run is { Model: NativeExecutionModel.ModelKey, StartedAt: null, Suspended: false, SubStatus: null, History: History.Full } &&
            run.Workflow == executable.ArtifactId && run.Definition == executable.DefinitionId &&
            JsonSerializer.Deserialize<NativeStart>(input) is { Mode: Modes.Balanced, Input: null, Trigger: System.Text.Json.Nodes.JsonValue key } native &&
            native.TriggerNode == triggerNode && key.TryGetValue<string>(out var value) && value == scheduleKey;
        return matches ? start : null;
    }

    private static string? CronError(ICronCalendar calendar, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "the expression is empty";
        try { _ = calendar.Next(expression.Trim(), 0); return null; }
        catch (Exception invalid) when (invalid is FormatException or ArgumentException) { return invalid.Message; }
    }

    private static bool FitsTypeData(string json) => json.Length <= MaxTypeDataBytes && Encoding.UTF8.GetByteCount(json) <= MaxTypeDataBytes;

    internal static bool IsReference(string? value) =>
        value is { Length: 64 } && value.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") < 0;

    private sealed record Authority(int Version, TriggerScope Scope, string FoundationTenant, string ScheduleKey, string TriggerNode,
        RuntimeKernelScheduleControlOwner Owner, string PinnedStart);
}

internal delegate ValueTask<RuntimeKernelScheduleAttemptOutcome> AcceptScheduleOccurrence(RuntimeKernelScheduleOccurrence occurrence,
    WorkflowExecutableIdentity? material, DateTimeOffset attemptDeadline,
    Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken);

internal delegate ValueTask<RuntimeKernelScheduleAttemptOutcome> HandoffScheduleOccurrence(ScheduleOccurrence job,
    RuntimeKernelScheduleOccurrence origin, string continuity, DateTimeOffset attemptDeadline,
    Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken);

/// <summary>Bridges authenticated managed schedule jobs to the scoped Runtime occurrence transaction.</summary>
internal sealed class KernelScheduleOccurrenceAcceptor : IScheduleOccurrenceAcceptor
{
    private readonly TriggerScope _scope;
    private readonly BoundScope _actors;
    private readonly Func<CancellationToken, ValueTask<long>> _readScheduleClock;
    private readonly AcceptScheduleOccurrence _accept;
    private readonly HandoffScheduleOccurrence? _handoff;

    internal KernelScheduleOccurrenceAcceptor(TriggerScope scope, BoundScope actors, Func<CancellationToken, ValueTask<long>> readScheduleClock,
        AcceptScheduleOccurrence accept, HandoffScheduleOccurrence? handoff = null)
    {
        KernelScheduleAuthority.RequireBoundScope(scope, actors);
        _scope = scope;
        _actors = actors;
        _readScheduleClock = readScheduleClock ?? throw new ArgumentNullException(nameof(readScheduleClock));
        _accept = accept ?? throw new ArgumentNullException(nameof(accept));
        _handoff = handoff;
    }

    public ValueTask<bool> TryAcceptAsync(ScheduleOccurrence occurrence, CancellationToken cancellationToken = default) =>
        TryAcceptAttemptAsync(occurrence, occurrence.OfferUntilUnixMilliseconds, cancellationToken);

    public async ValueTask<bool> TryAcceptAttemptAsync(ScheduleOccurrence occurrence, long attemptDeadline, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (attemptDeadline < occurrence.OfferUntilUnixMilliseconds || attemptDeadline > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() ||
            KernelScheduleAuthority.ReadOffer(occurrence, _scope, _actors, out var continuity, out var material) is not { } offer)
            return false;
        var deadline = DateTimeOffset.FromUnixTimeMilliseconds(attemptDeadline);
        var outcome = await _accept(offer, material, deadline, ReadClockAsync, cancellationToken);
        if (outcome == RuntimeKernelScheduleAttemptOutcome.Refused && continuity is not null && _handoff is not null)
            outcome = await _handoff(occurrence, offer, continuity, deadline, ReadClockAsync, cancellationToken);
        return outcome switch
        {
            RuntimeKernelScheduleAttemptOutcome.Accepted => true,
            RuntimeKernelScheduleAttemptOutcome.Refused => false,
            RuntimeKernelScheduleAttemptOutcome.Retry => throw new TimeoutException("The managed schedule attempt did not settle the occurrence."),
            _ => throw new InvalidOperationException("The Runtime store returned an unknown schedule attempt outcome.")
        };
    }

    private async ValueTask<DateTimeOffset> ReadClockAsync(CancellationToken cancellationToken) =>
        DateTimeOffset.FromUnixTimeMilliseconds(await _readScheduleClock(cancellationToken));
}
