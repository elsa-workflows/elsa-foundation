using Elsa.Expressions.Core;
using Elsa.Expressions.Liquid.Contracts;
using Elsa.Expressions.Liquid.Options;
using Fluid;

namespace Elsa.Expressions.Liquid.Extensions
{
    internal static class TemplateContextExtensions
    {
        internal static void AddFilters(this TemplateContext templateContext, IExpressionExecutionContext context, LiquidTemplateManagerOptions options)
        {
            foreach (var (key, value) in options.FilterRegistrations)
            {
                templateContext.Options.Filters.AddFilter(key, (input, arguments, ctx) =>
                {
                    var filter = (ILiquidFilter)context.GetRequiredService(value)!;
                    return filter.ProcessAsync(input, arguments, ctx);
                });
            }

            options.ConfigureFilters(templateContext);
        }
    }
}
