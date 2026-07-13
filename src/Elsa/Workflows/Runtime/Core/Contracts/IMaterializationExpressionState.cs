namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Exposes the workflow-scoped state (variables, workflow inputs, and prior activity outputs) that an
/// activity input expression may reference while it is evaluated at materialization time — before the
/// activity's real execution context exists. The materialization-time
/// <see cref="Elsa.Expressions.Core.Contracts.IExpressionExecutionContext"/> implements this so that
/// language-specific pre-processors (e.g. JavaScript) can surface <c>variables.foo</c>, <c>input.bar</c>,
/// and prior-output accessors without depending on a live workflow execution context.
/// </summary>
public interface IMaterializationExpressionState : Elsa.Expressions.Core.Contracts.IExpressionTenantContext
{
    /// <summary>Current workflow variable values keyed by variable name.</summary>
    IReadOnlyDictionary<string, object?> WorkflowVariables { get; }

    /// <summary>Workflow input values keyed by input name.</summary>
    IReadOnlyDictionary<string, object?> WorkflowInputs { get; }

    /// <summary>Prior activity output values keyed by output name.</summary>
    IReadOnlyDictionary<string, object?> ActivityOutputValues { get; }
}
