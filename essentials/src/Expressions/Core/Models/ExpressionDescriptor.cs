using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models;

public class ExpressionDescriptor(string typeName, ExpressionEditingMode editingMode) : IExpressionDescriptor
{
    /// <summary>
    /// Gets or sets the syntax name.
    /// </summary>
    public string TypeName { get; } = typeName;

    /// <summary>
    /// Gets or sets the display name of the expression type.
    /// </summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// Gets the semantic authoring mode required by this expression type.
    /// </summary>
    public ExpressionEditingMode EditingMode { get; } = editingMode;

    /// <summary>
    /// Gets or sets the expression type properties.
    /// </summary>
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
