using Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>An exact held root prepared by the built-in admission path.</summary>
public sealed record PendingRootStart(string Tenant, ActorRef Actor, string Type, string TypeData, string Issuance);

/// <summary>Protects the selected executable after preparation commits and before its root is admitted.</summary>
public delegate ValueTask<bool> ProtectRootStartAsync(PendingRootStart start, CancellationToken cancellationToken);
