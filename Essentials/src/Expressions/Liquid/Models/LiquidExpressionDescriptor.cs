using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using System.Reflection;

namespace Elsa.Expressions.Liquid.Models;

public sealed class LiquidExpressionDescriptor : IExpressionDescriptor
{
    public LiquidExpressionDescriptor()
    {
        Properties = ToDictionary(new { MonacoLanguage = "liquid" });
    }

    /// <summary>
    /// Gets the name of the expression type.
    /// </summary>
    public const string TypeName = "Liquid";

    public string DisplayName => TypeName;

    public ExpressionEditingMode EditingMode => ExpressionEditingMode.Text;

    public IDictionary<string, object> Properties { get; }

    string IExpressionDescriptor.TypeName => TypeName;


    private static Dictionary<string, object> ToDictionary(object source, BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance) =>
        source.GetType().GetProperties(bindingAttr).ToDictionary
        (
            propInfo => propInfo.Name,
            propInfo => propInfo.GetValue(source, null)!
        );
}
