namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A conditional first fault, checked against the physical accepted start and poison decision.</summary>
/// <remarks>This is evidence to validate, not independent authority to create an execution.</remarks>
public sealed class RuntimeInitialStartFault
{
    public RuntimeInitialStartFault(RuntimeSchedulerWorkItem work, RuntimeSchedulerPoisonRecord poison)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(poison);
        Work = work;
        Poison = poison;
    }
    public RuntimeSchedulerWorkItem Work { get; }
    public RuntimeSchedulerPoisonRecord Poison { get; }
}
