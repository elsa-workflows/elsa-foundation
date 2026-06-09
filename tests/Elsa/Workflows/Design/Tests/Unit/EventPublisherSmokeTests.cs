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
/// <c>AddEventHandlersFrom</c> registers handlers only against the non-generic
/// <see cref="IEventHandler"/> marker; the <see cref="EventHandlerInvokerMiddleware"/> resolves
/// <c>IEnumerable&lt;IEventHandler&gt;</c> and filters by <c>IsInstanceOfType</c> (matching the
/// request- and command-dispatcher pattern). This test guards against a regression where a
/// closed-generic lookup would return empty and silently swallow every dispatch.
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
            // RequiredInputOutputValidator depends on IActivityDefinitionLookup.
            services.AddSingleton<IActivityDefinitionLookup, EmptyActivityDefinitionLookup>();
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
        // contributes a "no start activity" error. If the invoker can't find the handler, Errors
        // is empty.
        Assert.NotEmpty(evt.Errors);
        Assert.Contains(evt.Errors, e => e.Type == "Graph/StartActivity");
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
            ActivityConnections: [],
            Activities: [],
            Inputs: [],
            Outputs: [],
            WorkflowActivityOptions: null,
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

    private sealed class EmptyActivityDefinitionLookup : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IActivityDefinitionVersion>(null!);

        public Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
