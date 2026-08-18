using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;

namespace Elsa.Serialization.SystemText.Services;

/// <summary>
/// Startup task that drives the framework §2.6.1 Registry + StartUp Task sub-pattern for
/// JSON payload converters (Elsa §E3.3 worked example). On execute:
///
/// 1. Publishes <see cref="JsonPayloadConvertersInitializing"/> through the mediator.
/// 2. Awaits the single <c>RegisterJsonConverters</c> handler, which resolves every
///    <see cref="IJsonConverterSource"/> and adds their converters to
///    <see cref="JsonPayloadConvertersInitializing.Converters"/>.
/// 3. Flushes the accumulated <see cref="JsonPayloadConvertersInitializing.Converters"/>
///    into the singleton <see cref="JsonPayloadConverterRegistry"/>.
///
/// After this task completes, <see cref="JsonPayloadSerializer"/> reads the registry
/// synchronously while building <see cref="System.Text.Json.JsonSerializerOptions"/>.
/// </summary>
/// <remarks>
/// <b>Ordered ahead of every other startup task, because it is a prerequisite for all of them.</b> Any task that
/// deserializes a payload -- an artifact reconciler reading a closure envelope, a version reconciler reading a
/// definition -- reads through <see cref="JsonPayloadSerializer"/>, which builds its options from the registry
/// this task fills. Without an explicit order, a task with the default order of <c>0</c> and no
/// <c>[TaskDependency]</c> edge to this one runs in <em>incidental DI registration order</em>, and if it wins the
/// race it deserializes with an empty converter set: no <c>JsonStringEnumConverter</c>, so enums written as
/// strings at request time cannot be read back at boot.
/// <para>
/// That is not hypothetical. Spec 151's artifact reconciler hit exactly this on a real server -- an exported
/// closure imported at startup failed with "the JSON value could not be converted to ActivityValuePolicy",
/// because the export happened at request time with a populated registry and the import happened at boot without
/// one. It was invisible to every in-process test, which resolves a fully initialised serializer.
/// </para>
/// <para>
/// <c>-100</c> places it in the same infrastructure-prerequisite band as <c>RunMigrationsStartupTask</c>. Fixing
/// it here rather than by adding a <c>[TaskDependency]</c> from each consumer is deliberate: consumers would each
/// need a project reference to this assembly, and every future one would have to remember. A prerequisite should
/// not be opt-in.
/// </para>
/// </remarks>
[Order(-100)]
public sealed class JsonPayloadConvertersInitializingStartupTask(
    IInlineEventPublisher eventPublisher,
    JsonPayloadConverterRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var @event = new JsonPayloadConvertersInitializing();
        await eventPublisher.Publish(@event, cancellationToken);
        registry.RegisterAll(@event.Converters);
    }
}
