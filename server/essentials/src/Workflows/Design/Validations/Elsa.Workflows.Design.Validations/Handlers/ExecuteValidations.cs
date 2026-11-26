using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Handlers;

/// <summary>
/// The single <c>DraftValidating</c> handler (Sipke's 2026-06-01 one-handler proposal).
/// Resolves every registered <see cref="IDraftValidator"/>, runs each against the post-mutation
/// Draft, and aggregates the returned errors onto the event. Replaces the former one-event-handler
/// -per-validator sprawl; features now contribute by registering an <see cref="IDraftValidator"/>.
/// </summary>
public sealed class ExecuteValidations(IEnumerable<IDraftValidator> validators) : IEventHandler<DraftValidating>
{
    public async Task Handle(DraftValidating domainEvent, CancellationToken cancellationToken)
    {
        var findings = new List<ValidationError>();
        foreach (var validator in validators)
        {
            var errors = await validator.Validate(domainEvent.Draft, cancellationToken);
            findings.AddRange(errors);
        }
        // Source admission still returns its complete findings to publication and direct consumers.
        // On the composed draft panel, retain the existing whole-variable owner's stable rule ID.
        var resolvedVariablePaths = findings.Where(error => error.Type == "Expressions/UnresolvedVariable")
            .Select(error => error.Path).ToHashSet(StringComparer.Ordinal);
        var inputRefusals = new HashSet<(string Path, string Type)>();
        foreach (var error in findings)
        {
            if (error.Type == "Variable/UnresolvableReference" && resolvedVariablePaths.Contains(error.Path))
                continue;
            if (error.Type is "Inputs/CredentialLiteral" or "Inputs/ConnectionReference" &&
                !inputRefusals.Add((error.Path, error.Type)))
                continue;
            domainEvent.Errors.Add(error);
        }
    }
}
