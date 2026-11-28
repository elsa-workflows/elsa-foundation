namespace Kernel.Foundation.Contracts;

/// <summary>Confirms external obligations before the foundation retires the selected terminal checkpoint.</summary>
public interface IActorTerminalDeletionGuard
{
    /// <summary>Whether this actor's configured composition has external obligations.</summary>
    bool HasExternalDeletionGuard { get; }

    /// <summary>Returns true while deletion is protected. Positive release evidence must be immutable.</summary>
    ValueTask<bool> ProtectsTerminalDeletionAsync(ActorHeader header, string state, long now, CancellationToken cancellationToken);
}
