using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.StandaloneTriggers;

/// <summary>Normal module composition for an external Event producer; no endpoint, queue or caller authority is created.</summary>
public static class StandaloneEventIngressServiceCollectionExtensions
{
    /// <summary>The application supplies its ordinary trusted scoped IStandaloneTriggerSourceContext for this producer.</summary>
    public static IServiceCollection AddStandaloneEventIngress<TSource>(this IServiceCollection services, ValueTypeDescriptor payloadSchema,
        string sourceFamily = "Elsa.Event.External") where TSource : class =>
        services.AddStandaloneTriggerIngress<TSource>(new("event", 1, sourceFamily, payloadSchema));
}
