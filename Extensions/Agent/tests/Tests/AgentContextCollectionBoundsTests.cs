using System.Text.Json;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class AgentContextCollectionBoundsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_uncooperative_source_deadline_has_its_declared_required_outcome_and_cannot_return_late_evidence(bool required)
    {
        var clock = new TriggeredTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentContextContribution>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken sourceToken = default;
        var blocked = new Source("blocked", (_, token) =>
        {
            sourceToken = token;
            entered.SetResult();
            return new(release.Task);
        }, required);
        var healthy = new Source("healthy", (_, _) => ValueTask.FromResult(new AgentContextContribution([Attachment("healthy")], [])));
        using var root = Create(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)), blocked, healthy);
        using var scope = root.CreateScope();
        var collection = Collector(scope).CollectAsync(AgentPolicy.Default, Request());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            clock.FireNext();
            var result = await collection.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(sourceToken.IsCancellationRequested);
            Assert.Equal(!required, result.Succeeded);
            var contribution = Assert.IsType<AgentContextContribution>(result.Value);
            Assert.Equal("healthy", Assert.Single(contribution.Attachments).Id);
            var diagnostic = Assert.Single(contribution.Diagnostics);
            Assert.Equal("blocked", diagnostic.ContributorId);
            Assert.Equal("agent.context.source_timed_out", diagnostic.Code);
            Assert.Equal(required, diagnostic.Required);
            Assert.Equal(TimeSpan.FromSeconds(10), clock.DueTimes[0]);

            release.SetResult(new([Attachment("late")], [new("blocked", "late", "Late evidence cannot rejoin admission.", true)]));
            await release.Task;
            Assert.Equal("healthy", Assert.Single(contribution.Attachments).Id);
            Assert.Single(contribution.Diagnostics);
        }
        finally
        {
            release.TrySetResult(new([], []));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_faults_are_optional_diagnostics_or_required_domain_failures(bool required)
    {
        var failure = new JsonException("An infrastructure message must not cross the contribution boundary.");
        var broken = new Source("broken", (_, _) => throw failure, required);
        var healthy = new Source("healthy", (_, _) => ValueTask.FromResult(new AgentContextContribution([Attachment("healthy")], [])));
        using var root = Create(broken, healthy);
        using var scope = root.CreateScope();

        if (required)
        {
            var error = await Assert.ThrowsAsync<AgentContributionException>(() => Collector(scope).CollectAsync(AgentPolicy.Default, Request()));
            Assert.Same(failure, error.InnerException);
            Assert.Equal(0, healthy.Calls);
        }
        else
        {
            var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request());
            Assert.True(result.Succeeded, result.Error?.Message);
            var contribution = Assert.IsType<AgentContextContribution>(result.Value);
            Assert.Equal("healthy", Assert.Single(contribution.Attachments).Id);
            var diagnostic = Assert.Single(contribution.Diagnostics);
            Assert.Equal("agent.context.source_failed", diagnostic.Code);
            Assert.False(diagnostic.Required);
            Assert.DoesNotContain(failure.Message, diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967295)]
    public void Source_timeout_configuration_rejects_nonpositive_or_unsupported_timer_bounds(long milliseconds)
    {
        using var root = Create(services => services.Configure<AgentContextOptions>(options => options.SourceTimeout = TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Throws<OptionsValidationException>(() => root.GetRequiredService<IOptions<AgentContextOptions>>().Value);
    }

    [Fact]
    public async Task Cancellation_ends_collection_even_when_a_source_ignores_its_token()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentContextContribution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new Source("blocked", (_, _) =>
        {
            entered.SetResult();
            return new(release.Task);
        });
        using var root = Create(blocked);
        using var scope = root.CreateScope();
        var collection = Collector(scope).CollectAsync(AgentPolicy.Default, Request(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            cancellation.Cancel();
            var error = await Record.ExceptionAsync(() => collection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal(cancellation.Token, ((OperationCanceledException)error).CancellationToken);
        }
        finally
        {
            release.TrySetResult(new([Attachment("late")], []));
            _ = await Record.ExceptionAsync(() => collection.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task Cancellation_during_an_uncooperative_source_does_not_start_a_later_source()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentContextContribution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Source("first", (_, _) =>
        {
            entered.SetResult();
            return new(release.Task);
        });
        var later = new Source("later", (_, _) => ValueTask.FromResult(new AgentContextContribution([Attachment("later")], [])));
        using var root = Create(first, later);
        using var scope = root.CreateScope();
        var collection = Collector(scope).CollectAsync(AgentPolicy.Default, Request(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();
        release.SetResult(new([Attachment("first")], []));
        var error = await Record.ExceptionAsync(() => collection.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(0, later.Calls);
    }

    [Theory]
    [InlineData("count", false)]
    [InlineData("count", true)]
    [InlineData("summary", false)]
    [InlineData("summary", true)]
    [InlineData("content", false)]
    [InlineData("content", true)]
    public async Task Context_volume_boundaries_are_explicit_required_outcomes(string boundary, bool exceeds)
    {
        var count = boundary == "count" ? 32 + (exceeds ? 1 : 0) : 1;
        var attachments = Enumerable.Range(0, count).Select(index => Attachment("bounded-" + index) with
        {
            Summary = boundary == "summary" ? new string('s', 4096 + (exceeds ? 1 : 0)) : "A bounded summary.",
            Content = boundary == "content" ? new string('c', 32766 + (exceeds ? 1 : 0)) : "A bounded body."
        }).ToList();
        var source = new Source("bounded", (_, _) => ValueTask.FromResult(new AgentContextContribution(attachments, [])));
        using var root = Create(source);
        using var scope = root.CreateScope();

        var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request());

        Assert.Equal(!exceeds, result.Succeeded);
        var admitted = Assert.IsType<AgentContextContribution>(result.Value);
        Assert.Equal(count, admitted.Attachments.Count);
        if (exceeds)
        {
            Assert.Equal("agent.context.incomplete", result.Error?.Code);
            var diagnostic = Assert.Single(admitted.Diagnostics);
            Assert.Equal("agent.context.budget_exceeded", diagnostic.Code);
            Assert.True(diagnostic.Required);
        }
        else
            Assert.Empty(admitted.Diagnostics);
    }

    [Fact]
    public async Task A_completed_source_cannot_change_its_evidence_while_a_later_source_is_pending()
    {
        var body = new Dictionary<string, string> { ["value"] = "accepted" };
        var first = new Source("first", (_, _) => ValueTask.FromResult(new AgentContextContribution([Attachment("first") with { Content = body }], [])));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentContextContribution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var later = new Source("later", (_, _) =>
        {
            entered.SetResult();
            return new(release.Task);
        });
        using var root = Create(first, later);
        using var scope = root.CreateScope();
        var collection = Collector(scope).CollectAsync(AgentPolicy.Default, Request());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        body["value"] = "late mutation";
        release.SetResult(new([], []));
        var result = await collection.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Succeeded, result.Error?.Message);
        var snapshot = Assert.Single(Assert.IsType<AgentContextContribution>(result.Value).Attachments);
        Assert.Equal("accepted", Assert.IsType<JsonElement>(snapshot.Content).GetProperty("value").GetString());
    }

    [Fact]
    public async Task A_source_cannot_change_admitted_transport_values_after_collection_completes()
    {
        var body = new Dictionary<string, string> { ["value"] = "accepted" };
        var references = new Dictionary<string, string> { ["resource"] = "original" };
        var attachments = new List<AgentContextAttachment>
        {
            Attachment("snapshot") with { Content = body, References = references }
        };
        var source = new Source("snapshot", (_, _) => ValueTask.FromResult(new AgentContextContribution(attachments, [])));
        using var root = Create(source);
        using var scope = root.CreateScope();

        var result = await Collector(scope).CollectAsync(AgentPolicy.Default, Request());
        Assert.True(result.Succeeded, result.Error?.Message);
        var accepted = Assert.IsType<AgentContextContribution>(result.Value);

        body["value"] = "late";
        references["resource"] = "retargeted";
        attachments.Clear();

        var snapshot = Assert.Single(accepted.Attachments);
        Assert.Equal("accepted", Assert.IsType<JsonElement>(snapshot.Content).GetProperty("value").GetString());
        Assert.Equal("original", snapshot.References["resource"]);
    }

    private static AgentContextAttachment Attachment(string id) => new(id, "module.bounds", null, "Bounded context", "bounds.context",
        AgentContextSensitivity.Internal, "bounds", "A bounded summary.", "A bounded body.", new Dictionary<string, string>())
    { ModuleId = "module.bounds", SchemaVersion = "1", Origin = AgentContextOrigin.ResourceData };

    private static AgentContextRequest Request() => new("bounds-session", "unbound", new Dictionary<string, string>());

    private static ServiceProvider Create(params Source[] sources) => Create(null, sources);

    private static ServiceProvider Create(Action<IServiceCollection>? configure, params Source[] sources) => AgentTestServices.Create(services =>
    {
        configure?.Invoke(services);
        foreach (var source in sources)
            services.AddScoped<IAgentContextProvider>(_ => source);
    });

    private static IAgentContextCollector Collector(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAgentContextCollector>();

    private sealed class Source(string id, Func<AgentContextRequest, CancellationToken, ValueTask<AgentContextContribution>> collect, bool required = true) : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new(id, "module.bounds", "*", ["bounds.context"], ["1"], ["bounds.resource"]) { Required = required };
        public int Calls { get; private set; }

        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return collect(request, cancellationToken);
        }
    }

    private sealed class TriggeredTimeProvider : TimeProvider
    {
        private readonly Queue<TriggeredTimer> timers = new();
        public List<TimeSpan> DueTimes { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTimes.Add(dueTime);
            var timer = new TriggeredTimer(callback, state);
            timers.Enqueue(timer);
            return timer;
        }

        public void FireNext() => timers.Dequeue().Fire();

        private sealed class TriggeredTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;

            public void Fire()
            {
                if (!disposed)
                    callback(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
