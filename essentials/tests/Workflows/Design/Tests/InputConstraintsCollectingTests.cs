using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Expressions.Core.Models;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class InputConstraintsCollectingTests
{
    [Fact]
    public async Task Real_inline_pipeline_collects_each_provider_once_for_the_exact_target_then_intersects_contributions()
    {
        var trace = new Trace();
        var catalog = Catalog();
        var version = await catalog.GetVersion("activity");
        using var cancellation = new CancellationTokenSource();
        var first = new Contribution("first", trace, new("first", ["Literal", "Variable"], "first-rule", "First restriction."));
        var second = new Contribution("second", trace, new("second", ["Literal"], "second-rule", "Second restriction."));
        var empty = new Contribution("empty", trace, null);
        using var provider = ActivityDataSelectionTestSupport.Provider(catalog, configure: services =>
        {
            // Repeated composition must not dispatch the collecting handler twice.
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            services.AddSingleton(trace);
            services.AddSingleton<IActivityInputSourceConstraintProvider>(first);
            services.AddSingleton<IActivityInputSourceConstraintProvider>(second);
            services.AddSingleton<IActivityInputSourceConstraintProvider>(empty);
        });
        provider.GetRequiredService<IEventPipeline>().Setup(builder => builder.UseMiddleware<TraceConstraints>());
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<InputConstraintsCollecting>>());
        var result = await scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>()
            .AnalyzeAsync(Document(), new("consumer", "value"), new() { IncludeSources = false }, cancellation.Token);

        Assert.Empty(result.Findings);
        Assert.Equal("Literal", Assert.Single(result.Target!.Constraint!.AllowedExpressionTypes));
        Assert.Equal<string>(new[] { "pipeline-before", "first-start", "first-end", "second-start", "second-end", "empty-start", "empty-end", "pipeline-after" }, trace.Entries);
        foreach (var contribution in new[] { first, second, empty })
        {
            Assert.Same(version, contribution.Version);
            Assert.Equal("value", contribution.InputKey);
            Assert.Equal(cancellation.Token, contribution.CancellationToken);
        }
    }

    [Fact]
    public async Task A_provider_failure_propagates_through_the_real_inline_pipeline()
    {
        var failure = new InvalidOperationException("Constraint authority failed.");
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(), configure: services =>
            services.AddSingleton<IActivityInputSourceConstraintProvider>(new ThrowingContribution(failure)));
        using var scope = provider.CreateScope();
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IActivityDataSelectionAnalyzer>()
            .AnalyzeAsync(Document(), new("consumer", "value"), new() { IncludeSources = false }).AsTask());
        Assert.Same(failure, observed);
    }

    [Fact]
    public void Missing_inline_publisher_cannot_silently_skip_source_constraints()
    {
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(), configure: services => services.RemoveAll<IInlineEventPublisher>());
        using var scope = provider.CreateScope();
        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>());
        Assert.Contains(nameof(IInlineEventPublisher), failure.Message, StringComparison.Ordinal);
    }

    private static StubActivityCatalog Catalog() => new StubActivityCatalog().Add("activity",
        [new InputDefinition("value", "Value", new("String"), null, "Value", null, true)]);
    private static ActivityDataSelectionDocument Document() => new("WorkflowDefinition", "draft", "candidate",
        new ActivityNode("consumer", "activity", [ArgumentState.Null("value")], []), [], [], []);

    private sealed class Trace
    {
        internal List<string> Entries { get; } = [];
    }

    private sealed class TraceConstraints(PipelineDelegate<IEventContext> next, Trace trace) : IEventMiddleware
    {
        public async ValueTask Invoke(IEventContext context)
        {
            Assert.IsType<InputConstraintsCollecting>(context.Event);
            trace.Entries.Add("pipeline-before");
            await next(context);
            trace.Entries.Add("pipeline-after");
        }
    }

    private sealed class Contribution(string name, Trace trace, ActivityDataInputConstraint? constraint) : IActivityInputSourceConstraintProvider
    {
        internal IActivityDefinitionVersion? Version { get; private set; }
        internal string? InputKey { get; private set; }
        internal CancellationToken CancellationToken { get; private set; }
        public async ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(IActivityDefinitionVersion activityVersion,
            string inputKey, CancellationToken cancellationToken = default)
        {
            Version = activityVersion;
            InputKey = inputKey;
            CancellationToken = cancellationToken;
            trace.Entries.Add(name + "-start");
            await Task.Yield();
            trace.Entries.Add(name + "-end");
            return constraint;
        }
    }

    private sealed class ThrowingContribution(Exception failure) : IActivityInputSourceConstraintProvider
    {
        public ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(IActivityDefinitionVersion activityVersion,
            string inputKey, CancellationToken cancellationToken = default) => ValueTask.FromException<ActivityDataInputConstraint?>(failure);
    }
}
