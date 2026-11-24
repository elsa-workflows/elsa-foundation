using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Composes a public actor against a test's in-memory store.</summary>
public sealed class InMemoryActorFixture(Durable.InMemoryStore store, string ingressNode, int pageSize)
{
    public IIngress Ingress { get; } = new InMemoryIngress(store, new Durable.Requests(ingressNode), ingressNode);
    public IActorReads Reads { get; } = new ActorList(store, pageSize);

    public Durable.IActorType Bind(IActorType actor) => new Durable.ActorAdapter(store, actor);
}
