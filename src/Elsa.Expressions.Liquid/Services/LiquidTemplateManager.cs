using Elsa.Expressions.Core;
using Elsa.Expressions.Liquid.Contracts;
using Elsa.Expressions.Liquid.Notifications;
using Fluid;
using Microsoft.Extensions.Caching.Memory;
using Elsa.Notifications.Core;
using Elsa.Expressions.Liquid.Extensions;
using Elsa.Expressions.Liquid.Options;

namespace Elsa.Expressions.Liquid.Services
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public sealed class LiquidTemplateManager(FluidParser parser, IMemoryCache memoryCache, INotificationSender notificationSender, LiquidTemplateManagerOptions options) 
        : ILiquidTemplateManager
    {

        /// <inheritdoc />
        public async Task<string?> RenderAsync(string template, IExpressionExecutionContext expressionExecutionContext, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(template))
                return default!;

            var result = GetCachedTemplate(template);
            var templateContext = await CreateTemplateContextAsync(expressionExecutionContext, cancellationToken);
            var encoder = options.TextEncoder;
            templateContext.AddFilters(expressionExecutionContext, options);

            return await result.RenderAsync(templateContext, encoder);
        }

        private IFluidTemplate GetCachedTemplate(string source)
        {
            var result = memoryCache.GetOrCreate(
                source,
                e =>
                {
                    if (!TryParse(source, out var parsed, out var error))
                    {
                        error = "{% raw %}\n" + error + "\n{% endraw %}";
                        _ = TryParse(error, out parsed, out error);

                        e.SetSlidingExpiration(TimeSpan.FromMilliseconds(100));
                        return parsed;
                    }

                    // TODO: add signal based cache invalidation.
                    e.SetSlidingExpiration(TimeSpan.FromSeconds(30));
                    return parsed;
                });
            return result!;
        }

        /// <inheritdoc />
        public bool Validate(string template, out string error) => TryParse(template, out _, out error);

        private bool TryParse(string template, out IFluidTemplate result, out string error) => parser.TryParse(template, out result, out error);

        private async Task<TemplateContext> CreateTemplateContextAsync(IExpressionExecutionContext expressionExecutionContext, CancellationToken cancellationToken)
        {
            var context = new TemplateContext(expressionExecutionContext, new TemplateOptions());
            await notificationSender.SendAsync(new RenderingLiquidTemplate(context, expressionExecutionContext), cancellationToken);
            context.SetValue("ExpressionExecutionContext", expressionExecutionContext);
            return context;
        }
    }
}
