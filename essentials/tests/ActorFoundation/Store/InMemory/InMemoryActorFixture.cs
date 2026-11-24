using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Composes a public actor against a test's in-memory store.</summary>
public sealed class InMemoryActorFixture(Durable.InMemoryStore store, string ingressNode, int pageSize,
    Func<BoundScope, string, bool>? authorizeOperator = null)
{
    public IIngress Ingress { get; } = new InMemoryIngress(store, new Durable.Requests(ingressNode), ingressNode, authorizeOperator);
    public IActorReads Reads { get; } = new ActorList(store, pageSize);

    public Durable.IActorType Bind(IActorType actor) => new Durable.ActorAdapter(store, actor);

    public IActorReads OpenReads(int pageSize) => new ActorList(store, pageSize);

    public Durable.InstanceRow? ReadCheckpoint(string tenant, string reference) =>
        store.ReadFoundationActor(tenant, reference).Actor?.Row;

    public static int MessageContentLimit => Durable.FoundationMetadataLimits.MessageContentBytes;
    public static int RecoveryCreditLimit => Durable.ParkRecoveryCredit.LimitBytes;

    public static Durable.InMemoryStore CreateWithParkRetention(Func<long> clock, TimeSpan retention) =>
        new(1, clock) { ParkedRetentionWindow = retention };

    public string ResolveTargetId(BoundScope scope, ActorRef actor) =>
        store.FoundationTarget(scope.Tenant, actor.Id)?.Id ??
        throw new InvalidOperationException("The fixture actor has no physical target.");

    public bool HasConsumedDelivery(string target, string kind) =>
        store.Messages.Values.Any(row => row.Message.Target.Id == target &&
            row.Message.Kind == kind && row.KeptBy == Durable.ParkKeepReason.Consumed);

    public bool HasDiscardDrain(string target) => store.Instances[target].ParkDiscard is not null;
}
