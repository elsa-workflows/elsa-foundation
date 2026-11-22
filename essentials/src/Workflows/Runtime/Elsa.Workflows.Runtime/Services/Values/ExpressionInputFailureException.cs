namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Identifies a portable expression input failure without carrying the input's source or value.</summary>
public sealed class ExpressionInputFailureException : InvalidOperationException
{
    public const string ExpressionEvaluationFailed = "ExpressionEvaluationFailed";
    public const string InputMaterializationFailed = "InputMaterializationFailed";
    public const string EvaluationPhaseName = "Evaluation";
    public const string MaterializationPhase = "Materialization";

    public ExpressionInputFailureException(
        string code,
        string inputKey,
        string expressionLanguage,
        string phase,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        InputFailureCode = code;
        InputKey = inputKey;
        ExpressionLanguage = expressionLanguage;
        EvaluationPhase = phase;
    }

    public string InputFailureCode { get; }
    public string InputKey { get; }
    public string ExpressionLanguage { get; }
    public string EvaluationPhase { get; }
}
