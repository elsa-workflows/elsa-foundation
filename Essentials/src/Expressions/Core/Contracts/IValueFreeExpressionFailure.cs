namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// An expression failure whose message carries no value, only names: codes, inputs, variables and paths. The runtime
/// keeps such a message even when a value the expression read is protected, where it would otherwise redact the whole
/// failure (spec 1228 FR-006a: the write diagnostic names the variable and the path; spec 1229 FR-006).
/// </summary>
public interface IValueFreeExpressionFailure;
