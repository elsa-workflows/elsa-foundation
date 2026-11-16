using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Evaluates an executable artifact's declared runtime requirements against the registries installed
/// in this runtime, including consumer capabilities, durable-value storage drivers, and CLR activity
/// type availability.
/// </summary>
/// <remarks>
/// The contract is runtime-owned so Publishing and artifact import can share one provider-neutral
/// evaluation. Callers project the result into their own diagnostics or rejection models.
/// </remarks>
public interface IRuntimeRequirementChecker
{
    RuntimeRequirementCheckResult Check(RuntimeRequirementCheckSubject subject);
}
