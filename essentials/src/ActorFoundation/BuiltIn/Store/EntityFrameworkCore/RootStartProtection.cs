using Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>An exact held root prepared by the built-in admission path.</summary>
public sealed record PendingRootStart(string Tenant, ActorRef Actor, string Type, string TypeData, string Issuance)
{
    internal const string IssuancePrefix = "held-root/1.";

    /// <summary>Identifies which proof reader handles an opaque issuance. The reader still validates it.</summary>
    public static bool OwnsIssuance(string issuance) => issuance.StartsWith(IssuancePrefix, StringComparison.Ordinal);
}

/// <summary>Protects the selected executable after preparation commits and before its root is admitted.</summary>
public delegate ValueTask<bool> ProtectRootStartAsync(PendingRootStart start, CancellationToken cancellationToken);

/// <summary>What the durable Foundation state establishes about the exact held root.</summary>
public enum RootStartProof
{
    Unresolved,
    BirthRetired,
    PreparationDisarmed
}
