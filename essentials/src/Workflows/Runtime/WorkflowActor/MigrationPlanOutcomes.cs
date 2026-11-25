using System.Text.Json;
using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

public enum MigrationPlanOutcome { Migrated = 0, Refused = 1, NotAtSource = 2 }

internal sealed record MigrationPlanResult(MigrationPlanOutcome Outcome, string? Finding = null)
{
    internal const string Kind = "migration-reply";
}

internal abstract record MigrationPlanJournal(string Kind)
{
    private const string CreatedKind = "migration-plan.created.v1";
    private const string SelectedKind = "migration-plan.selected.v1";
    private const string DispatchedKind = "migration-plan.dispatched.v1";
    private const string AnsweredKind = "migration-plan.answered.v1";
    private const string ClosedKind = "migration-plan.closed.v1";

    internal sealed record Created(string From, string To, long Deadline, string? Model = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Definition = null)
        : MigrationPlanJournal(CreatedKind);
    internal sealed record Selected(ActorRef[] Actors, long Number = 0, PageToken? After = null, bool AllSelected = false)
        : MigrationPlanJournal(SelectedKind);
    internal sealed record Dispatched(long Number) : MigrationPlanJournal(DispatchedKind);
    internal sealed record Answered(ActorRef Actor, MigrationPlanOutcome Outcome) : MigrationPlanJournal(AnsweredKind);
    internal sealed record Closed() : MigrationPlanJournal(ClosedKind);

    internal static MigrationPlanJournal? Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var record = document.RootElement;
        if (record.ValueKind == JsonValueKind.String && record.GetString() == "end") return new Closed();
        if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty("Operation", out var operation) &&
            operation.ValueKind == JsonValueKind.String && record.TryGetProperty("Cleared", out var cleared) &&
            cleared.ValueKind == JsonValueKind.Number && record.TryGetProperty("Outcome", out var outcome) &&
            outcome.ValueKind == JsonValueKind.String && outcome.GetString() is "terminated" or "discarded" or "expired")
            return new Closed();
        if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty(nameof(Kind), out var kind) ||
            kind.ValueKind != JsonValueKind.String) return null;
        return kind.GetString() switch
        {
            CreatedKind => record.Deserialize<Created>() is { From.Length: > 0, To.Length: > 0, Deadline: > 0 } created
                ? created : throw new InvalidOperationException("The migration plan needs both definitions and a deadline."),
            SelectedKind => record.Deserialize<Selected>() is { Actors: { } actors } batch &&
                (actors.Length > 0 || batch.Number > 0) &&
                actors.All(actor => !string.IsNullOrEmpty(actor.Id))
                    ? batch : throw new InvalidOperationException("The migration batch needs exact actor references."),
            DispatchedKind => record.Deserialize<Dispatched>() is { Number: > 0 } dispatched
                ? dispatched : throw new InvalidOperationException("The acknowledgment needs its batch number."),
            AnsweredKind => record.Deserialize<Answered>() is { Actor.Id.Length: > 0 } answer &&
                record.TryGetProperty(nameof(Answered.Outcome), out _) && Enum.IsDefined(answer.Outcome)
                    ? answer : throw new InvalidOperationException("The migration answer needs an exact actor reference and outcome."),
            ClosedKind => new Closed(),
            _ => null
        };
    }
}

internal sealed record MigrationPlanView(bool Closed, long AsOf, IReadOnlyDictionary<ActorRef, MigrationPlanOutcome?> Results)
{
    internal IEnumerable<ActorRef> Pending => Closed ? [] : Results.Where(row => row.Value is null).Select(row => row.Key);
    internal IEnumerable<ActorRef> NoAnswer => Closed ? Results.Where(row => row.Value is null).Select(row => row.Key) : [];
    // This describes projected selections. The dispatcher establishes that selection is complete separately.
    internal bool AllAnswered => (Closed || Results.Count > 0) && Results.Values.All(result => result is not null);
}

/// <summary>Projects one plan's committed journal. Each atomic page advances the opaque bookmark and the result rows together.</summary>
internal sealed class MigrationPlanOutcomes(IActorReads reads, BoundScope scope, ActorRef plan)
{
    private readonly object _gate = new();
    private readonly Dictionary<ActorRef, MigrationPlanOutcome?> _outcomes = [];
    private PageToken? _bookmark;
    private bool _closed;
    private long _asOf;
    private long _lastSelection;
    private int _unanswered;
    private long? _deadline;
    private long? _finalBatch;
    private bool _finalDispatched;

    internal MigrationPlanView Read()
    {
        lock (_gate)
            return new(_closed, _asOf, new Dictionary<ActorRef, MigrationPlanOutcome?>(_outcomes));
    }

    internal ActorRef[]? Unselected(long selection, IEnumerable<ActorRef> candidates, int take)
    {
        lock (_gate)
            return _closed || _lastSelection != selection ? null
                : candidates.Where(actor => !_outcomes.ContainsKey(actor)).Distinct().Take(take).ToArray();
    }

    internal (long Batch, long Deadline)? Completion()
    {
        lock (_gate)
            return !_closed && _deadline is { } deadline && _finalBatch is { } batch && _finalDispatched &&
                _unanswered == 0 ? (batch, deadline) : null;
    }

    internal async Task<bool> ProjectNextAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            PageToken? before;
            lock (_gate) before = _bookmark;
            var page = await reads.TailAsync(scope, plan, before, cancellationToken);
            if (page.Items.Count > 0 && (page.Next is null || page.Next == before))
                throw new InvalidOperationException("A nonempty plan journal page needs an advancing bookmark.");
            var records = page.Items.Select(item => MigrationPlanJournal.Read(item.Entry)).ToArray();
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_bookmark != before) continue;
                foreach (var record in records) Apply(record);
                if (page.Items.Count > 0) _bookmark = page.Next;
                else _asOf = Math.Max(_asOf, page.AsOf);
                return page.Items.Count > 0;
            }
        }
    }

    private void Apply(MigrationPlanJournal? record)
    {
        switch (record)
        {
            case MigrationPlanJournal.Created created:
                _deadline = created.Deadline;
                break;
            case MigrationPlanJournal.Selected batch when !_closed:
                _lastSelection = Math.Max(_lastSelection, batch.Number);
                foreach (var actor in batch.Actors)
                    if (_outcomes.TryAdd(actor, null)) _unanswered++;
                if (batch is { AllSelected: true, Number: > 0 })
                {
                    _finalBatch = batch.Number;
                    _finalDispatched = batch.Actors.Length == 0;
                }
                break;
            case MigrationPlanJournal.Dispatched dispatch when _finalBatch == dispatch.Number:
                _finalDispatched = true;
                break;
            case MigrationPlanJournal.Answered answer when
                _outcomes.TryGetValue(answer.Actor, out var prior) && prior is null or MigrationPlanOutcome.NotAtSource:
                // The first decisive answer wins. A repeated batch and a later receipt cannot erase it.
                if (prior is null) _unanswered--;
                _outcomes[answer.Actor] = answer.Outcome;
                break;
            case MigrationPlanJournal.Closed:
                _closed = true;
                break;
        }
    }
}
