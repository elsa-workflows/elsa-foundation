using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid.Services;
using Microsoft.Extensions.DependencyInjection;
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

    public Func<IServiceProvider, IExpressionHandler> HandlerFactory => ActivatorUtilities.GetServiceOrCreateInstance<LiquidExpressionHandler>;

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
