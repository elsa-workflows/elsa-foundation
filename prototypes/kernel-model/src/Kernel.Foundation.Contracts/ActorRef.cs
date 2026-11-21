namespace Kernel.Foundation.Contracts;

/// <summary>
/// An opaque actor identity. A reference issued by the foundation identifies one life of the actor,
/// remains stable across replay and ownership changes, and is compared ordinally.
/// </summary>
public readonly record struct ActorRef(string Id)
{
    public override string ToString() => Id;
}
