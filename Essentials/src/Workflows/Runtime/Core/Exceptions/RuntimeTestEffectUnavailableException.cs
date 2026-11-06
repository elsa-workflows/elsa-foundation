namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>An infrastructure/control refusal, never an expected business fault.</summary>
public sealed class RuntimeTestEffectUnavailableException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
