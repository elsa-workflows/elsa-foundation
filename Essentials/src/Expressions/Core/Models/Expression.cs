using System.Text.Json.Serialization;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// Represents an expression.
/// </summary>
public partial class Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Expression"/> class.
    /// </summary>
    [JsonConstructor]
    public Expression()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Expression"/> class.
    /// </summary>
    /// <param name="type">The expression type.</param>
    /// <param name="value">The expression.</param>
    public Expression(string type, object? value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>
    /// Gets or sets the expression type.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Gets or sets the expression.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Creates an expression that represents a literal value.
    /// </summary>
    /// <param name="value">The literal value.</param>
    /// <returns>An expression that represents a literal value.</returns>
    public static Expression LiteralExpression(object? value) => new("Literal", value);

}
