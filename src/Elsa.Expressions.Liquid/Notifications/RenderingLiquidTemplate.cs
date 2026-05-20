using Elsa.Expressions.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Fluid;

namespace Elsa.Expressions.Liquid.Notifications
{
    public sealed record RenderingLiquidTemplate(TemplateContext TemplateContext, IExpressionExecutionContext Context)
        : IDomainEvent;
}
