namespace Elsa.Expressions.Core.Models;

public sealed record ArgumentValue(
    string? Value,
    string? ExpressionType = null
);
