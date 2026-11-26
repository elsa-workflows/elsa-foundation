using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed record BoundTriggerSource<TSource>(StandaloneTriggerSourceDescriptor Descriptor) where TSource : class;

internal sealed class StandaloneTriggerIngressFactory(IServiceProvider services, StandaloneTriggerAdmission admission,
    IStandaloneTriggerStore resources, IRuntimeIngressDeliveryStore delivery) : IStandaloneTriggerIngressFactory
{
    public IStandaloneTriggerIngress<TSource> Create<TSource>() where TSource : class
    {
        var descriptors = services.GetKeyedServices<StandaloneTriggerSourceDescriptor>(typeof(TSource)).ToArray();
        var contexts = services.GetServices<IStandaloneTriggerSourceContext<TSource>>().ToArray();
        if (descriptors.Length != 1 || contexts.Length != 1 || !contexts[0].SupportsResourceAuthorization)
            throw new NotSupportedException("A producer requires one registered descriptor and an explicit bound resource authorization policy.");
        return new StandaloneTriggerIngress<TSource>(new(descriptors[0]), services, admission, resources, delivery);
    }
}

internal sealed class StandaloneTriggerIngress<TSource>(BoundTriggerSource<TSource> source,
    IServiceProvider services, StandaloneTriggerAdmission admission, IStandaloneTriggerStore resources,
    IRuntimeIngressDeliveryStore delivery) : IStandaloneTriggerIngress<TSource> where TSource : class
{
    public async ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        IStandaloneTriggerSourceContext<TSource> context;
        try
        {
            // Resolution proves this operation scope is still alive; a retained port never preserves a disposed caller's context.
            var contexts = services.GetServices<IStandaloneTriggerSourceContext<TSource>>().ToArray();
            if (contexts.Length != 1 || !contexts[0].SupportsResourceAuthorization)
                throw new NotSupportedException("The registered source context or resource policy is unavailable.");
            context = contexts[0];
        }
        catch (ObjectDisposedException exception)
        { throw new TriggerOperationException("unavailable", "The source operation scope has ended.", innerException: exception); }
        if (!delivery.SupportsAdmission) throw new NotSupportedException("The selected Runtime provider cannot accept standalone trigger work.");
        var invocation = await context.GetAsync(cancellationToken);
        var aggregate = await resources.FindAsync(invocation.Scope, signal.TriggerId, cancellationToken) ??
            throw new TriggerOperationException("trigger-not-found", "The source resource is unavailable in this scope.");
        var trigger = aggregate.Trigger;
        if (trigger.Id != signal.TriggerId || trigger.TenantId != invocation.Scope.TenantId || trigger.TypeKey != source.Descriptor.TypeKey ||
            trigger.SchemaVersion != source.Descriptor.SchemaVersion || !StandaloneTriggerAdmission.SchemasMatch(source.Descriptor.PayloadSchema, signal.PayloadSchema))
            throw new TriggerOperationException("source-type-mismatch", "This registered producer does not own the resource type or submitted schema.");
        if (!context.SupportsResourceAuthorization || !await context.AuthorizeAsync(trigger, signal, cancellationToken))
            throw new TriggerOperationException("forbidden", "The current source is not authorized for this resource and signal.");
        var accepted = await admission.AdmitAsync(source.Descriptor, invocation, signal, cancellationToken, aggregate);
        var selection = accepted.Item.SubscriptionDelivery!;
        return new(accepted.Item.StandaloneIngress!.AdmissionId, accepted.Replayed, selection.Recipients.Select(item => item.SubscriptionId).ToArray(),
            selection.Refusals.Select(item => new StandaloneTriggerIngressRefusal(item.SubscriptionId, item.ReasonCode)).ToArray());
    }
}
