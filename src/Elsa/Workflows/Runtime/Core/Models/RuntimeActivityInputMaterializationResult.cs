namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeActivityInputMaterializationResult(
    string Name,
    string InputKey,
    bool IsSensitive,
    RuntimeMaterializedActivityInput? Input,
    RuntimeInputEvaluationFailure? Failure,
    Exception? Exception)
{
    public bool Succeeded => Input is not null && Failure is null;

    public static RuntimeActivityInputMaterializationResult Success(RuntimeMaterializedActivityInput input) =>
        new(input.Name, input.InputKey ?? input.Name, input.IsSensitive, input, null, null);

    public static RuntimeActivityInputMaterializationResult Failed(
        string name,
        string inputKey,
        bool isSensitive,
        RuntimeInputEvaluationFailure failure,
        Exception exception) =>
        new(name, inputKey, isSensitive, null, failure, exception);
}

public sealed record RuntimeActivityInputMaterializationBatch(IReadOnlyList<RuntimeActivityInputMaterializationResult> Results)
{
    public IReadOnlyList<RuntimeMaterializedActivityInput> Inputs => Results
        .Where(result => result.Input is not null)
        .Select(result => result.Input!)
        .ToArray();

    public IReadOnlyList<RuntimeActivityInputMaterializationResult> Failures => Results
        .Where(result => !result.Succeeded)
        .ToArray();

    public bool HasFailures => Results.Any(result => !result.Succeeded);

    public IReadOnlyList<RuntimeMaterializedActivityInput> RequireAllInputs()
    {
        if (HasFailures)
            throw new RuntimeActivityInputMaterializationException(Results);

        return Inputs;
    }
}

public sealed class RuntimeActivityInputMaterializationException : InvalidOperationException
{
    public RuntimeActivityInputMaterializationException(IReadOnlyList<RuntimeActivityInputMaterializationResult> results)
        : base(
            FailureMessage(results),
            FailureInnerException(results))
    {
        Results = results;
    }

    public IReadOnlyList<RuntimeActivityInputMaterializationResult> Results { get; }

    public IReadOnlyList<RuntimeActivityInputMaterializationResult> Failures => Results.Where(result => !result.Succeeded).ToArray();

    private static RuntimeActivityInputMaterializationResult FirstFailure(IReadOnlyList<RuntimeActivityInputMaterializationResult> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return failures.FirstOrDefault(result => !result.Succeeded) ?? throw new ArgumentException("At least one materialization failure is required.", nameof(failures));
    }

    private static string FailureMessage(IReadOnlyList<RuntimeActivityInputMaterializationResult> results)
    {
        var failure = FirstFailure(results);
        return failure.IsSensitive
            ? "A protected activity input could not be materialized."
            : failure.Exception?.Message ?? "One or more activity inputs could not be materialized.";
    }

    private static Exception? FailureInnerException(IReadOnlyList<RuntimeActivityInputMaterializationResult> results)
    {
        var failure = FirstFailure(results);
        return failure.IsSensitive ? null : failure.Exception?.InnerException ?? failure.Exception;
    }
}
