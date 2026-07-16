namespace Elsa.Expressions.Core.Contracts;

public interface IExpressionExecutionContext
{
    bool IsContainedWithinCompositeActivity();

    bool TryGetActivityInput(string key, out object? value);

    bool TryGetWorkflowInput(string key, out object? value);

    object? GetVariableValueOrDefault(string variableName);

    string GetCorrelationId();

    string GetWorkflowDefinitionId();

    string GetWorkflowDefinitionVersionId();

    int GetWorkflowDefinitionVersion();

    string GetWorkflowInstanceId();

    object? GetRequiredService(Type type);

    TService GetRequiredService<TService>() where TService : notnull
        => (TService)GetRequiredService(typeof(TService))!;

    /// <summary>
    /// Provides access to the parent <see cref="IExpressionExecutionContext"/>, if there is any.
    /// </summary>
    IExpressionExecutionContext? ParentContext { get; set; }

    /// <summary>
    /// A cancellation token.
    /// </summary>
    CancellationToken CancellationToken { get; }

    IVariable? GetVariable(string name, bool localScopeOnly = false);

    IEnumerable<IVariable> EnumerateVariablesInScope();

    /// <summary>
    /// Returns the value of the specified variable.
    /// </summary>
    object? GetVariableInScope(string variableName) => GetVariableValueOrDefault(variableName);

    /// <summary>
    /// Gets all variables names in scope.
    /// </summary>
    IEnumerable<string> GetVariableNamesInScope() =>
        EnumerateVariablesInScope()
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct();
}
