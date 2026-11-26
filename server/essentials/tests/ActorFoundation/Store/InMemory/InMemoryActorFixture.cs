using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Composes a public actor against a test's in-memory store.</summary>
public sealed class InMemoryActorFixture(Durable.InMemoryStore store, string ingressNode, int pageSize,
    Func<BoundScope, string, bool>? authorizeOperator = null)
{
    private readonly InMemoryIngress _ingress = new(store, new Durable.Requests(ingressNode), ingressNode, authorizeOperator);
    public IIngress Ingress => _ingress;
    public IActorReads Reads { get; } = new ActorList(store, pageSize);

    public Durable.IActorType Bind(IActorType actor, Func<ActorHeader, TimeSpan?>? retentionWindow = null) =>
        new Durable.ActorAdapter(store, actor, retentionWindow: retentionWindow);

    public IActorReads OpenReads(int pageSize) => new ActorList(store, pageSize);

    public Durable.InstanceRow? ReadCheckpoint(string tenant, string reference) =>
        store.ReadFoundationActor(tenant, reference).Actor?.Row;

    public static int MessageContentLimit => Durable.FoundationMetadataLimits.MessageContentBytes;
    public static int RecoveryCreditLimit => Durable.ParkRecoveryCredit.LimitBytes;

    public static Durable.InMemoryStore CreateWithParkRetention(Func<long> clock, TimeSpan retention) =>
        new(1, clock) { ParkedRetentionWindow = retention };

    public static Durable.InMemoryStore CreateWithDeduplicationWindow(Func<long> clock, TimeSpan window) =>
        new(1, clock) { IngressDeduplicationWindow = window };

    public ValueTask<Admission> AdmitRetentionDeleteAsync(BoundScope scope, ActorRef actor, string selection,
        CancellationToken cancellationToken) => _ingress.AdmitRetentionDeleteAsync(scope, actor, selection, cancellationToken);

    public IEnumerable<(string Tenant, ActorRef Actor, string Id)> PendingRetentionDeletes() => store.Messages.Values
        .Where(row => !row.Done && row.Message.Operator?.RetentionDelete == true)
        .Select(row => (row.Tenant!, new ActorRef(store.FoundationIdentity(row.Message.Target.Id, row.Message.TargetCreation!.Value).Reference),
            row.Message.Id));

    public int CountRetirementMarkers(BoundScope scope, ActorRef actor)
    {
        var id = store.FoundationTarget(scope.Tenant, actor.Id)?.Id;
        return store.Journal.Values.Count(row => row.Instance == id && row.Revision == -1);
    }

    public string ResolveTargetId(BoundScope scope, ActorRef actor) =>
        store.FoundationTarget(scope.Tenant, actor.Id)?.Id ??
        throw new InvalidOperationException("The fixture actor has no physical target.");

    public bool HasConsumedDelivery(string target, string kind) =>
        store.Messages.Values.Any(row => row.Message.Target.Id == target &&
            row.Message.Kind == kind && row.KeptBy == Durable.ParkKeepReason.Consumed);

    public bool HasDiscardDrain(string target) => store.Instances[target].ParkDiscard is not null;
}
