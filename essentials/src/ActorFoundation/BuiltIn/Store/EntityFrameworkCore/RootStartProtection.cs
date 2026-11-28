using Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>An exact root prepared by the built-in protected admission path.</summary>
public sealed record PendingRootStart(string Tenant, ActorRef Actor, string Type, string TypeData, string Issuance)
{
    internal const string IssuancePrefix = "held-root/1.";

    /// <summary>Identifies which proof reader handles an opaque issuance. The reader still validates it.</summary>
    public static bool OwnsIssuance(string issuance) => issuance.StartsWith(IssuancePrefix, StringComparison.Ordinal);
}

/// <summary>Protects the selected executable after preparation commits and before its root is admitted.</summary>
public delegate ValueTask<bool> ProtectRootStartAsync(PendingRootStart start, CancellationToken cancellationToken);

/// <summary>Admits a protected workflow root on an installed provider bound to one tenant and actor type.</summary>
public interface IProtectedRootStarts
{
    /// <summary>Returns after durable admission without holding a caller or waiting for the actor to answer.</summary>
    ValueTask<Admission> StartAsync(string operationId, Kernel.Foundation.Contracts.StartSpec spec, CancellationToken cancellationToken = default);
}

/// <summary>What the durable Foundation state establishes about the exact prepared root.</summary>
public enum RootStartProof
{
    Unresolved,
    BirthRetired,
    PreparationDisarmed
}

/// <summary>The physically observed disposition of one exact provider-issued preparation.</summary>
public enum PreparedRootStartDisposition { Unresolved, Accepted, Disarmed }

/// <summary>Trusted host composition for privately qualifying a source-owned protected start.</summary>
public interface IPreparedRootStartHandoff
{
    ValueTask<Admission> StartAsync(string operationId, Kernel.Foundation.Contracts.StartSpec spec, ProtectRootStartAsync protect, CancellationToken cancellationToken = default);
    ValueTask<PreparedRootStartDisposition> ObserveAsync(ActorRef actor, string issuance, CancellationToken cancellationToken = default);

    /// <summary>Reads the first retained response for an exact signed birth and its private response key.</summary>
    ValueTask<PreparedRootResponse?> ReadResponseAsync(ActorRef actor, string issuance, string key,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<PreparedRootResponse?>(null);

    /// <summary>Transfers an exact persisted source to its ordinary Foundation waits and returns the durable receipt.</summary>
    ValueTask<PreparedWaitReceipt> DeliverWaitsAsync(string operation, string sourceDigest, IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<PreparedWaitReceipt>(new NotSupportedException("This Foundation owner does not support prepared wait delivery."));
}

internal sealed class BoundPreparedRootStartHandoff(
    Func<ProtectRootStartAsync, IProtectedRootStarts> bind,
    Func<ActorRef, string, CancellationToken, Task<bool>> accepted,
    Func<ActorRef, string, CancellationToken, Task<RootStartProof>> lifetime,
    Func<ActorRef, string, string, CancellationToken, Task<PreparedRootResponse?>> readResponse,
    Func<string, string, IReadOnlyList<PreparedWaitDelivery>, bool, CancellationToken, Task<PreparedWaitReceipt>> deliverWaits) : IPreparedRootStartHandoff
{
    public ValueTask<Admission> StartAsync(string operationId, Kernel.Foundation.Contracts.StartSpec spec, ProtectRootStartAsync protect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protect);
        return bind(protect).StartAsync(operationId, spec, cancellationToken);
    }

    public ValueTask<PreparedRootResponse?> ReadResponseAsync(ActorRef actor, string issuance, string key,
        CancellationToken cancellationToken = default) =>
        key is { Length: 64 } && key.All(char.IsAsciiHexDigitLower)
            ? new(readResponse(actor, issuance, key, cancellationToken))
            : ValueTask.FromResult<PreparedRootResponse?>(null);

    public ValueTask<PreparedWaitReceipt> DeliverWaitsAsync(string operation, string sourceDigest, IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew,
        CancellationToken cancellationToken = default) => new(deliverWaits(operation, sourceDigest, deliveries, allowNew, cancellationToken));

    public async ValueTask<PreparedRootStartDisposition> ObserveAsync(ActorRef actor, string issuance, CancellationToken cancellationToken = default)
    {
        if (await accepted(actor, issuance, cancellationToken)) return PreparedRootStartDisposition.Accepted;
        return await lifetime(actor, issuance, cancellationToken) switch
        {
            RootStartProof.BirthRetired => PreparedRootStartDisposition.Accepted,
            RootStartProof.PreparationDisarmed => PreparedRootStartDisposition.Disarmed,
            _ => PreparedRootStartDisposition.Unresolved
        };
    }
}

/// <summary>The first committed response for an exact prepared root.</summary>
/// <remarks>A present response with no payload exceeded the provider's bound. An absent response is a committed end.</remarks>
public sealed record PreparedRootResponse(long CapturedAt, bool Present, byte[]? Payload);

/// <summary>One exact wait key and detached input of a prepared external source.</summary>
public sealed record PreparedWaitDelivery(string Key, string Payload);

/// <summary>The same immutable cohort receipt is returned after a lost acknowledgement, including for an empty cohort.</summary>
public sealed record PreparedWaitReceipt(string CohortDigest, int Count);
