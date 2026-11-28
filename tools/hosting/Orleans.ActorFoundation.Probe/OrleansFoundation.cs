using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Orleans;

namespace Elsa.Orleans.ActorFoundation.Probe;

// Limited to start, durable self-timer and journal reads in the probe host.
public sealed class OrleansFoundation(IGrainFactory grains, ProbeStore store) : IIngress, IActorReads
{
    public static ActorRef Reference(BoundScope scope, string id) =>
        new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope.Tenant, id })))));

    public async ValueTask<Admission> SendAsync(BoundScope scope, Target target, string id, string kind,
        string? payload = null, AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target is not Target.Start { Spec: var spec } || kind != Kinds.Start || options is not null ||
            payload is not null || scope.IsHostIsolated || spec.Ephemeral || spec.History != History.Full)
            throw new NotSupportedException("The Orleans probe supports ordinary durable starts with Full history.");

        var actor = Reference(scope, id);
        var added = await Grain(actor).AdmitAsync(scope.Tenant, id, JsonSerializer.Serialize(spec)).WaitAsync(cancellationToken);
        return new(added ? AdmissionOutcome.Accepted : AdmissionOutcome.Duplicate, actor);
    }

    public Task<bool> DeliverTimerAsync(BoundScope scope, ActorRef actor, string timerId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Grain(actor).DeliverTimerAsync(scope.Tenant, timerId).WaitAsync(token);
    }

    public async Task<int> DispatchDueTimersAsync(CancellationToken token)
    {
        var delivered = 0;
        foreach (var (actor, tenant, timerId) in await store.DueAsync(token))
        {
            token.ThrowIfCancellationRequested();
            if (await Grain(actor).DeliverTimerAsync(tenant, timerId).WaitAsync(token)) delivered++;
        }
        return delivered;
    }

    public async ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
    {
        var current = await store.LoadAsync(actor, scope.Tenant, cancellationToken);
        return current is null ? null : new(current.Header, null, null, await store.ReadClockAsync(cancellationToken));
    }

    public async ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        var current = await store.LoadAsync(actor, scope.Tenant, cancellationToken);
        var journal = current?.Journal ?? [];
        var offset = from is { } previous ? int.Parse(previous.Value, CultureInfo.InvariantCulture) : 0;
        if (offset < 0 || offset > journal.Count) throw new ArgumentOutOfRangeException(nameof(from));
        var remaining = journal.Skip(offset).ToArray();
        PageToken? next = remaining.Length == 0 ? null : new(journal.Count.ToString(CultureInfo.InvariantCulture));
        return new(remaining, next, await store.ReadClockAsync(cancellationToken)) { JournalHeadRevision = current?.Revision };
    }

    public ValueTask<Reply> RequestAsync(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? add, RequestOptions request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask SetStartAsync(BoundScope scope, string key, string wait, StartSpec? spec,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private IProbeActor Grain(ActorRef actor) => grains.GetGrain<IProbeActor>(actor.Id);
}

public interface IProbeActor : IGrainWithStringKey
{
    Task<bool> AdmitAsync(string tenant, string operation, string startJson);
    Task<bool> DeliverTimerAsync(string tenant, string timerId);
}

public sealed record ProbeSilo(string Name);

public sealed class ProbeFault
{
    private string? _next;

    public void Arm(string stage) => _next = stage;

    public void At(string stage)
    {
        if (Interlocked.CompareExchange(ref _next, null, stage) == stage)
            throw new InvalidOperationException("Probe interruption " + stage);
    }
}

public sealed class ProbeActor(ProbeStore store, IActorType actorType, ProbeSilo silo, ProbeFault fault) : Grain, IProbeActor
{
    public async Task<bool> AdmitAsync(string tenant, string operation, string startJson)
    {
        var actor = new ActorRef(this.GetPrimaryKeyString());
        var start = JsonSerializer.Deserialize<StartSpec>(startJson)!;
        var current = await store.LoadAsync(actor, tenant, CancellationToken.None);
        if (current is not null)
        {
            if (current.Start != start) throw new InvalidOperationException("The operation already selected a different workflow.");
            return false;
        }

        if (start.Type != actorType.Type) throw new NotSupportedException("This silo serves one workflow actor type.");
        var header = new ActorHeader(actor, start.Type, tenant, ActorStatus.Running, null, null, null, start.TypeData,
            new Dictionary<string, string>());
        var initial = new Checkpoint(0, header, start, null, null, [], silo.Name);
        var now = await store.ReadClockAsync(CancellationToken.None);
        var turn = new ProbeTurn(initial, new Message(operation, Kinds.Start, start.Input), now);
        await CommitAsync(initial, turn);
        return true;
    }

    public async Task<bool> DeliverTimerAsync(string tenant, string timerId)
    {
        var actor = new ActorRef(this.GetPrimaryKeyString());
        var current = await store.LoadAsync(actor, tenant, CancellationToken.None);
        if (current is not { Header.Status: ActorStatus.Running, Timer: { } timer } || timer.Id != timerId) return false;
        var now = await store.ReadClockAsync(CancellationToken.None);
        if (timer.Due > now) return false;

        var turn = new ProbeTurn(current, new Message(timer.Id, timer.Kind, timer.Payload, actor), now);
        await CommitAsync(current, turn);
        return true;
    }

    private async Task CommitAsync(Checkpoint previous, ProbeTurn turn)
    {
        if (actorType.Withholds(turn.Header)) throw new InvalidOperationException("The actor type withheld this checkpoint.");
        await actorType.TurnAsync(turn, CancellationToken.None);
        fault.At("before commit");
        await store.CommitAsync(turn.Finish(silo.Name), previous.Revision, CancellationToken.None);
        fault.At("after commit");
    }
}

internal sealed class ProbeTurn(Checkpoint previous, Message message, long now) : IActorTurn
{
    private readonly List<JournalEntry> _journal = [.. previous.Journal];
    private PendingTimer? _timer;

    public ActorHeader Header { get; private set; } = previous.Header;
    public ActorStartSettings? StartSettings => previous.Revision == 0 ? new(previous.Start.Ephemeral, previous.Start.History) : null;
    public long Now => now;
    public IReadOnlyList<Message> Inbox { get; } = [message];
    public string? State { get; set; } = previous.State;
    public object? Ambient => null;

    public string Send(ActorRef target, string kind, string? payload = null, long? dueAt = null)
    {
        if (target != Header.Ref || dueAt is null || _timer is not null)
            throw new NotSupportedException("The probe supports one pending durable self-timer.");
        var id = "timer-" + previous.Revision.ToString(CultureInfo.InvariantCulture);
        _timer = new(id, kind, payload, dueAt.Value);
        return id;
    }

    public void Recall(string id)
    {
        if (_timer?.Id == id) _timer = null;
    }

    public void Record(object entry) => _journal.Add(new(previous.Revision + 1, JsonSerializer.Serialize(entry)));

    public void End(ActorStatus status, object? output = null)
    {
        Header = Header with { Status = status, EndedAt = Now };
        _timer = null;
        Record("end");
    }

    public void SetHeader(string typeData, IReadOnlyDictionary<string, string> labels) =>
        Header = Header with { TypeData = typeData, Labels = new Dictionary<string, string>(labels) };

    public Checkpoint Finish(string silo) => new(previous.Revision + 1, Header, previous.Start, State, _timer, _journal, silo);

    public void Emit(string channel, object? item) => throw new NotSupportedException();
    public void Reply(Message request, string channel, object? item) => throw new NotSupportedException();
    public ActorRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false) => throw new NotSupportedException();
    public void StartJob(string key, string? arguments = null, TimeSpan? timeout = null) => throw new NotSupportedException();
    public void CancelJob(string key) => throw new NotSupportedException();
    public void Hold(Message item) => throw new NotSupportedException();
    public void Subscribe(string key, string wait, bool keep = false) => throw new NotSupportedException();
    public void Unsubscribe(string key, string wait) => throw new NotSupportedException();
    public string Publish(string key, string? payload, TimeSpan keepFor) => throw new NotSupportedException();
}
