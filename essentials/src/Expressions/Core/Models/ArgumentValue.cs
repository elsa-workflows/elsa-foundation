namespace Elsa.Expressions.Core.Models;

public sealed record ArgumentValue(
    object? Value,
    string? ExpressionType = null
);
