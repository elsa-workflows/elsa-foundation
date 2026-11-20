using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Collects the warnings of one evaluation or one step. Thread-safe; the order is the order reported. A protecting
/// collector keeps each warning's <see cref="ExecutionWarning.ProtectedForm"/>, for a step or evaluation that read a
/// protected value.
/// </summary>
public sealed class ExecutionWarningCollector(bool protect = false) : IExecutionWarningSink
{
    private readonly List<ExecutionWarning> _warnings = [];
    private readonly object _gate = new();

    public void Report(ExecutionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        // An unprotecting collector keeps the protected form with the warning, so the runtime can still choose it.
        var kept = protect ? warning.ProtectedForm ?? warning : warning;
        lock (_gate)
            _warnings.Add(kept);
    }

    /// <summary>The warnings reported so far.</summary>
    public IReadOnlyList<ExecutionWarning> Warnings
    {
        get
        {
            lock (_gate)
                return _warnings.ToArray();
        }
    }
}
