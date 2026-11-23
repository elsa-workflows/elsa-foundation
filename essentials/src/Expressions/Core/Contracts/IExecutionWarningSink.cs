using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Where an evaluation or an activity step reports a warning (the shared run-time warning channel, spec 1228). The
/// runtime records each warning on the activity execution, and inspection shows it. A warning never carries a value.
/// </summary>
public interface IExecutionWarningSink
{
    void Report(ExecutionWarning warning);
}
