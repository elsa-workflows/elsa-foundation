using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.DomainEvents;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Smoke test for the production <c>DomainEventPipeline</c> (Iterator + ExceptionShielding +
/// Invoker middleware) against handlers registered via <c>AddDomainEventHandlersFrom</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pre-fix history (2026-05-29 cascade): <c>AddDomainEventHandlersFrom</c> registers handlers
/// only against the non-generic <see cref="IDomainEventHandler"/> marker, but
/// <see cref="DomainEventHandlerIteratorMiddleware"/> previously queried
/// <c>GetServices(IDomainEventHandler&lt;TEvent&gt;)</c> (closed generic) — which returned
/// empty and silently swallowed every dispatch. The iterator now resolves
/// <c>IEnumerable&lt;IDomainEventHandler&gt;</c> + filters by <c>IsInstanceOfType</c>, matching
/// the request- and command-dispatcher pattern. This test prevents the regression.
/// </para>
/// <para>
/// Branch coverage of the iterator / shielding / invoker middleware is the deferred
/// <c>Elsa.Mediator.Tests</c> follow-on; this is a smoke check that lives with the consumer
/// that surfaced the bug.
/// </para>
/// </remarks>
public sealed class DomainEventDispatcherSmokeTests
{
    [Fact]
    public async Task Real_pipeline_dispatches_to_handlers_registered_via_AddDomainEventHandlersFrom()
    {
        await using var provider = BuildProvider(services =>
        {
            // RequiredInputOutputValidator depends on IActivityDefinitionLookup.
            services.AddSingleton<IActivityDefinitionLookup, EmptyActivityDefinitionLookup>();
            // The exact registration the Validations feature uses.
            services.AddDomainEventHandlersFrom(typeof(WorkflowDesignValidationsFeature).Assembly);
        });

        var sender = provider.GetRequiredService<IDomainEventSender>();
        var evt = new OnDraftValidating(EmptyDraft());

        await sender.Send(evt, CancellationToken.None);

        // The StartActivityValidator handler runs against an empty-State Draft and contributes
        // a "no start activity" error. If the iterator can't find the handler, Errors is empty.
        Assert.NotEmpty(evt.Errors);
        Assert.Contains(evt.Errors, e => e.Type == "Graph/StartActivity");
    }

    [Fact]
    public async Task Real_pipeline_dispatches_to_handler_registered_via_AddDomainEventHandler_generic()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddDomainEventHandler<OnDraftValidating, StubValidator>();
        });

        var sender = provider.GetRequiredService<IDomainEventSender>();
        var evt = new OnDraftValidating(EmptyDraft());

        await sender.Send(evt, CancellationToken.None);

        var error = Assert.Single(evt.Errors);
        Assert.Equal("Stub/SmokeCheck", error.Type);
    }

    [Fact]
    public async Task Real_pipeline_with_no_handlers_completes_cleanly()
    {
        await using var provider = BuildProvider(_ => { });

        var sender = provider.GetRequiredService<IDomainEventSender>();
        var evt = new OnDraftValidating(EmptyDraft());

        await sender.Send(evt, CancellationToken.None);

        Assert.Empty(evt.Errors);
    }

    [Fact]
    public async Task Real_pipeline_dispatches_every_registered_handler_for_the_same_event()
    {
        // Two handlers registered against the same event — both must run (framework §2.6.1
        // completeness rule, enforced by the iterator).
        await using var provider = BuildProvider(services =>
        {
            services.AddDomainEventHandler<OnDraftValidating, StubValidator>();
            services.AddDomainEventHandler<OnDraftValidating, SecondStubValidator>();
        });

        var sender = provider.GetRequiredService<IDomainEventSender>();
        var evt = new OnDraftValidating(EmptyDraft());

        await sender.Send(evt, CancellationToken.None);

        Assert.Equal(2, evt.Errors.Count);
        Assert.Contains(evt.Errors, e => e.Type == "Stub/SmokeCheck");
        Assert.Contains(evt.Errors, e => e.Type == "Stub/SecondCheck");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IDomainEventSender, DomainEventSender>();
        services.AddSingleton<IDomainEventPipeline, DomainEventPipeline>();

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

    private sealed class StubValidator : IDomainEventHandler<OnDraftValidating>
    {
        public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
        {
            domainEvent.AddValidationError(new ValidationError("$workflow", "Stub/SmokeCheck", "smoke"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondStubValidator : IDomainEventHandler<OnDraftValidating>
    {
        public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
        {
            domainEvent.AddValidationError(new ValidationError("$workflow", "Stub/SecondCheck", "smoke"));
            return ValueTask.CompletedTask;
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
