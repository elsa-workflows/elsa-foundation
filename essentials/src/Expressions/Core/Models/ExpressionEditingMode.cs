namespace Elsa.Expressions.Core.Models;

/// <summary>
/// Describes the semantic authoring mode required by an expression type.
/// </summary>
public enum ExpressionEditingMode
{
    Literal,
    Text,
    Structured,
    Reference
}
