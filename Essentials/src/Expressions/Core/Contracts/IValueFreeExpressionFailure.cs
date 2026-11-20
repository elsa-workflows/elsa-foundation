namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// An expression failure with a form that carries no value: <see cref="ValueFreeMessage"/> names codes, inputs and
/// declared variables only, never a member path or a name the script computed, which can come from a stored value. The
/// runtime keeps that message when a value the expression read is protected, where it would otherwise redact the whole
/// failure (spec 1228 FR-006a: the write diagnostic names the variable; spec 1229 FR-006).
/// </summary>
public interface IValueFreeExpressionFailure
{
    /// <summary>The failure's message with nothing in it that can come from a value.</summary>
    string ValueFreeMessage { get; }
}
