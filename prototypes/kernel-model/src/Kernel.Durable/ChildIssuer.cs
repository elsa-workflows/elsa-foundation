using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>One allocation sequence per successful partition take. A turn keeps the issuer of its own epoch.
/// Only a fenced parent commit publishes the returned address and permits delivery of the creator.</summary>
internal sealed class ChildIssuer(int partition, long epoch)
{
    private long _last;
    public InstanceRef Next(int destination)
    {
        lock (this)
            return InstanceRef.ForChild(destination, partition, epoch, checked(++_last));
    }
}
