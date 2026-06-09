namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// The value of an argument on a filled-in design-time canvas — either a literal value (the
/// <see cref="Value"/> is taken verbatim) or an expression source (<see cref="ExpressionType"/>
/// names the runtime expression evaluator and the <see cref="Value"/> carries the source).
/// The expression-type string is registry-resolved at runtime; each evaluator module owns its
/// own well-known value (e.g. "Literal", "JavaScript", "Liquid").
/// </summary>
public sealed record ArgumentValue(object? Value, string ExpressionType);
