using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Contracts;
using Elsa.Expressions.Liquid.Extensions;
using Elsa.Expressions.Liquid.Notifications;
using Elsa.Expressions.Liquid.Options;
using Elsa.Events.Core.Contracts;
using Fluid;
using Microsoft.Extensions.Caching.Memory;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// Constructor.
/// </summary>
public sealed class LiquidTemplateManager(FluidParser parser, IMemoryCache memoryCache, IInlineEventPublisher eventPublisher, LiquidTemplateManagerOptions options)
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
                    // Fall back to rendering the parse error itself. If that re-parse ALSO fails
                    // (e.g. the error text embeds template markup), surface a clear error instead of
                    // caching null and crashing on render (#422).
                    var wrappedError = "{% raw %}\n" + error + "\n{% endraw %}";
                    if (!TryParse(wrappedError, out parsed, out _))
                        throw new InvalidOperationException($"Failed to parse Liquid template: {error}");

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
        await eventPublisher.Publish(new RenderingLiquidTemplate(context, expressionExecutionContext), cancellationToken);
        context.SetValue("ExpressionExecutionContext", expressionExecutionContext);
        return context;
    }
}