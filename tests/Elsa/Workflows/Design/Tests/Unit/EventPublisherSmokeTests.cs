using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Events.Services;
using Elsa.Events.Strategies;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Smoke test for the production event pipeline (handler-invoker middleware, no shielding)
/// against handlers registered via <c>AddEventHandlersFrom</c> and a default Sequential
/// publishing strategy.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch contract (see <c>Elsa.Events/EXTENSION_POINTS.md</c>): handlers are registered under both
/// the closed generic <see cref="IEventHandler{TEvent}"/> and the non-generic <see cref="IEventHandler"/>
/// marker, and the <c>EventHandlerInvokerMiddleware</c> resolves them exclusively through the closed
/// generic. These tests guard both directions of the original bug: a handler registered through the
/// supported paths must dispatch, and a handler registered only under the bare marker must not.
/// </para>
/// <para>
/// Branch coverage of the strategy / invoker middleware is the deferred <c>Elsa.Events.Tests</c>
/// follow-on; this is a smoke check that lives with the consumer that surfaced the original bug.
/// </para>
/// </remarks>
public sealed class EventPublisherSmokeTests
{
    [Fact]
    public async Task Real_pipeline_dispatches_to_handlers_registered_via_AddEventHandlersFrom()
    {
        await using var provider = BuildProvider(services =>
        {
            // RequiredInputOutputValidator's CatalogVersionResolver depends on IActivityDefinitionLookup.
            services.AddSingleton<IActivityDefinitionLookup>(new BaselineValidatorTests.StubActivityCatalog());
            // AddEventHandlersFrom discovers the single ExecuteValidations handler in the
            // Validations assembly; the feature registers its IDraftValidator impls separately,
            // so register the one this smoke check asserts against.
            services.AddEventHandlersFrom(typeof(WorkflowDesignValidationsFeature).Assembly);
            services.AddScoped<IDraftValidator, StartActivityValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        // ExecuteValidations runs the StartActivityValidator against an empty-State Draft and
        // contributes a "missing root activity" error. If the invoker can't find the handler,
        // Errors is empty.
        Assert.NotEmpty(evt.Errors);
        Assert.Contains(evt.Errors, e => e.Type == "RootActivity/Missing");
    }

    [Fact]
    public async Task Real_pipeline_dispatches_to_handler_registered_via_AddEventHandler_generic()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddEventHandler<OnDraftValidating, StubValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        var error = Assert.Single(evt.Errors);
        Assert.Equal("Stub/SmokeCheck", error.Type);
    }

    [Fact]
    public async Task TryAddEventHandler_registers_a_dispatchable_handler()
    {
        // The path the EF Core persistence base uses for its aggregators.
        await using var provider = BuildProvider(services =>
        {
            services.TryAddEventHandler<OnDraftValidating, StubValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        var error = Assert.Single(evt.Errors);
        Assert.Equal("Stub/SmokeCheck", error.Type);
    }

    [Fact]
    public async Task Registering_the_same_handler_more_than_once_dispatches_it_once()
    {
        // Registration is idempotent per (event, handler) pair, so a handler reached by both an
        // explicit call and an aggregator/idempotent call still runs exactly once — the fix does not
        // rely on the middleware's defensive DistinctBy.
        await using var provider = BuildProvider(services =>
        {
            services.AddEventHandler<OnDraftValidating, StubValidator>();
            services.TryAddEventHandler<OnDraftValidating, StubValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        Assert.Single(evt.Errors);
    }

    [Fact]
    public async Task Handler_registered_only_under_the_bare_marker_does_not_dispatch()
    {
        // Locks the dispatch contract: the pipeline resolves the closed generic only, so a
        // marker-only registration silently never fires. This is the exact regression that caused
        // the EF Core saving/loading handlers to stop running.
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped<IEventHandler, StubValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        Assert.Empty(evt.Errors);
    }

    [Fact]
    public async Task Real_pipeline_with_no_handlers_completes_cleanly()
    {
        await using var provider = BuildProvider(_ => { });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        Assert.Empty(evt.Errors);
    }

    [Fact]
    public async Task Real_pipeline_dispatches_every_registered_handler_for_the_same_event()
    {
        // Two handlers registered against the same event — both must run (framework §2.6.1
        // completeness rule, enforced by the invoker).
        await using var provider = BuildProvider(services =>
        {
            services.AddEventHandler<OnDraftValidating, StubValidator>();
            services.AddEventHandler<OnDraftValidating, SecondStubValidator>();
        });

        var publisher = provider.GetRequiredService<IEventPublisher>();
        var evt = new OnDraftValidating(EmptyDraft());

        await publisher.Publish(evt, cancellationToken: CancellationToken.None);

        Assert.Equal(2, evt.Errors.Count);
        Assert.Contains(evt.Errors, e => e.Type == "Stub/SmokeCheck");
        Assert.Contains(evt.Errors, e => e.Type == "Stub/SecondCheck");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IEventPublisher, EventPublisher>();
        services.AddSingleton<IEventPipeline, EventPipeline>();
        services.AddSingleton<IEventPublishingStrategy>(EventPublishingStrategy.Sequential);

        configure(services);

        return services.BuildServiceProvider();
    }

    private static IWorkflowDefinitionDraft EmptyDraft() => new StubDraft();

    private sealed class StubDraft : IWorkflowDefinitionDraft
    {
        public string Id => "draft-smoke";
        public string WorkflowDefinitionId => "wf-smoke";
        public WorkflowDefinitionState State { get; } = new(
            Variables: [],
            RootActivity: null,
            Inputs: [],
            Outputs: [],
            StrategyOptions: null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    private sealed class StubValidator : IEventHandler<OnDraftValidating>
    {
        public Task Handle(OnDraftValidating @event, CancellationToken cancellationToken)
        {
            @event.Errors.Add(new ValidationError("$workflow", "Stub/SmokeCheck", "smoke"));
            return Task.CompletedTask;
        }
    }

    private sealed class SecondStubValidator : IEventHandler<OnDraftValidating>
    {
        public Task Handle(OnDraftValidating @event, CancellationToken cancellationToken)
        {
            @event.Errors.Add(new ValidationError("$workflow", "Stub/SecondCheck", "smoke"));
            return Task.CompletedTask;
        }
    }

}
