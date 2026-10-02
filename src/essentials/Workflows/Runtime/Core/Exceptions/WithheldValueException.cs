namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A withheld value reached a reader that needs it and cannot resolve it (<c>VF-ACT-010</c>). Only the
/// <see cref="SecretBindingDiagnostics"/> factories create it, so its message is always their fixed text, which names
/// an input, a variable or a conversion target and never carries a value.
/// </summary>
/// <remarks>
/// A reader that redacts the failures it wraps, such as the portable expression evaluator, rethrows this type as it is,
/// so the fixed code reaches the fault record instead of being redacted away. Derives from
/// <see cref="InvalidOperationException"/> so existing callers that catch that type are unaffected.
/// </remarks>
public sealed class WithheldValueException : InvalidOperationException
{
    internal WithheldValueException(string message) : base(message)
    {
    }
}
