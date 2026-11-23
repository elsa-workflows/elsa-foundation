namespace Elsa.Tasks.Core.Attributes;

/// <summary>
/// Runs a startup task one node at a time: on each node it waits for the others, then runs. It does not run the task on
/// only one node.
/// </summary>
/// <remarks>
/// <para>
/// The executor takes a distributed lock keyed by the shell's name and the task's type name before the task runs, and
/// releases it when the task ends. A node that finds the lock held waits, for at most the lock provider's acquisition
/// timeout, and then runs the task itself; when the wait runs out, the task fails with a <see cref="TimeoutException"/>
/// naming the lock. Losing the lock while the task runs cancels the token the task was given, and that surfaces as a
/// failure, never as an orderly cancellation.
/// </para>
/// <para>
/// That is the whole guarantee: one at a time, at shell start. There is no failover, no fencing and no record that the task
/// ran, so the task must be safe to run again on every node and after every restart. A task that ignores its token can
/// still finish after its lock is lost. Work that must happen on exactly one node needs a claim of its own instead.
/// </para>
/// <para>
/// Only a lock provider that every node shares makes this hold across nodes. The file-system provider's default folder is
/// node-local, which is why a host that joined a cluster through a durable membership provider refuses it at startup.
/// </para>
/// <para>
/// It is meant for startup tasks. On a background or recurring task, each start, run and stop takes the lock the same way,
/// which serializes the calls but elects no leader.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class SingleNodeTaskAttribute : Attribute;
