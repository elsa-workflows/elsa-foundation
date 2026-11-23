using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>One allocation sequence per (partition, epoch) a worker holds. A turn keeps the issuer of its instance's
/// partition at its own epoch. Only a fenced parent commit publishes the returned address and permits delivery of the creator.</summary>
internal sealed class ChildIssuer(int partition, long epoch)
{
    private long _last;
    public long Epoch => epoch;
    public InstanceRef Next(int destination)
    {
        lock (this)
            return InstanceRef.ForChild(destination, partition, epoch, checked(++_last));
    }
}
