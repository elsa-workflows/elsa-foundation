namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>An infrastructure/control refusal, never an expected business fault.</summary>
/// <remarks>
/// The message is safe for public fault projections. An inner exception retains the original provider failure for
/// trusted diagnostics; its text is not safe for public projection.
/// </remarks>
public sealed class RuntimeTestEffectUnavailableException : InvalidOperationException
{
    public RuntimeTestEffectUnavailableException(string code, string message)
        : this(code, message, null)
    {
    }

    public RuntimeTestEffectUnavailableException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
