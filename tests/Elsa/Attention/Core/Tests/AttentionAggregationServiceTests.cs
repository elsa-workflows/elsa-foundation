using System.Security.Claims;
using System.Text.Json;
using Elsa.Attention.Core;

namespace Elsa.Attention.Core.Tests;

public sealed class AttentionAggregationServiceTests
{
    [Fact]
    public async Task Unfiltered_discovery_omits_forbidden_contributors_but_explicit_selection_reports_forbidden()
    {
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("public.source", requiredPermission: null),
            new StubContributor("private.source", requiredPermission: "private.read")
        ]);
        var service = new AttentionAggregationService(registry, new DenyAllPermissionEvaluator(), new AttentionOptions());
        var context = new AttentionQueryContext(new ClaimsPrincipal(), "tenant-1");

        var discovered = await service.AggregateAsync(new AttentionQuery(context));
        var explicitlySelected = await service.AggregateAsync(new AttentionQuery(context, ["private.source"]));

        Assert.Equal(["public.source"], discovered.Contributors.Select(x => x.ContributorId));
        var forbidden = Assert.Single(explicitlySelected.Contributors);
        Assert.Equal(AttentionContributorStatus.Forbidden, forbidden.Status);
        Assert.Empty(forbidden.Items);
    }

    [Fact]
    public async Task Contributor_failures_are_isolated_and_successful_items_are_bounded()
    {
        var items = Enumerable.Range(1, 7).Select(x => Item($"item-{x}")).ToArray();
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("healthy.source", (_, _) => ValueTask.FromResult(AttentionContribution.Ready(items))),
            new StubContributor("broken.source", (_, _) => throw new InvalidOperationException("sensitive failure"))
        ]);
        var service = CreateService(registry);

        var result = await service.AggregateAsync(Query());

        var healthy = Assert.Single(result.Contributors, x => x.ContributorId == "healthy.source");
        Assert.Equal(AttentionContributorStatus.Ready, healthy.Status);
        Assert.Equal(5, healthy.Items.Count);
        Assert.Equal(7, healthy.TotalCount);
        Assert.True(healthy.IsTruncated);
        var failed = Assert.Single(result.Contributors, x => x.ContributorId == "broken.source");
        Assert.Equal(AttentionContributorStatus.Failed, failed.Status);
        Assert.Equal("CONTRIBUTOR_FAILED", failed.ErrorCode);
        Assert.DoesNotContain("sensitive", failed.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Slow_contributor_times_out_without_blocking_a_ready_contributor()
    {
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("slow.source", async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AttentionContribution.Ready([]);
            }),
            new StubContributor("ready.source", (_, _) => ValueTask.FromResult(AttentionContribution.Ready([Item("ready")])))
        ]);
        var service = CreateService(registry, new AttentionOptions { ContributorTimeout = TimeSpan.FromMilliseconds(20) });

        var result = await service.AggregateAsync(Query());

        Assert.Equal(AttentionContributorStatus.TimedOut, Assert.Single(result.Contributors, x => x.ContributorId == "slow.source").Status);
        Assert.Equal(AttentionContributorStatus.Ready, Assert.Single(result.Contributors, x => x.ContributorId == "ready.source").Status);
    }

    [Fact]
    public async Task Caller_cancellation_cancels_the_whole_aggregation()
    {
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("slow.source", async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AttentionContribution.Ready([]);
            })
        ]);
        var service = CreateService(registry);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AggregateAsync(Query(), cancellation.Token));
    }

    [Fact]
    public async Task Exceeding_the_downstream_call_budget_returns_a_safe_failure()
    {
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("busy.source", (context, _) =>
            {
                context.Budget.ConsumeDownstreamCall();
                context.Budget.ConsumeDownstreamCall();
                context.Budget.ConsumeDownstreamCall();
                return ValueTask.FromResult(AttentionContribution.Ready([]));
            })
        ]);
        var service = CreateService(registry);

        var result = await service.AggregateAsync(Query());

        var failed = Assert.Single(result.Contributors);
        Assert.Equal(AttentionContributorStatus.Failed, failed.Status);
        Assert.Equal("CONTRIBUTOR_BUDGET_EXCEEDED", failed.ErrorCode);
    }

    [Fact]
    public async Task Unregistering_a_contributor_cancels_its_in_flight_evaluation_and_removes_it_from_discovery()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contributor = new StubContributor("dynamic.source", async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AttentionContribution.Ready([]);
        });
        var registry = new AttentionContributorRegistry([]);
        var registration = registry.Register(contributor);
        var service = CreateService(registry);
        var aggregation = service.AggregateAsync(Query());
        await started.Task;

        registration.Dispose();
        var result = await aggregation;
        var next = await service.AggregateAsync(Query());

        Assert.Equal(AttentionContributorStatus.Unavailable, Assert.Single(result.Contributors).Status);
        Assert.Empty(next.Contributors);
    }

    [Fact]
    public async Task Invalid_item_contract_fails_only_its_contributor()
    {
        var invalid = Item("invalid") with { Destination = new AttentionDestination("https://outside.example", "Leave") };
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("invalid.source", (_, _) => ValueTask.FromResult(AttentionContribution.Ready([invalid]))),
            new StubContributor("valid.source", (_, _) => ValueTask.FromResult(AttentionContribution.Ready([Item("valid")])))
        ]);
        var service = CreateService(registry);

        var result = await service.AggregateAsync(Query());

        Assert.Equal(AttentionContributorStatus.Failed, Assert.Single(result.Contributors, x => x.ContributorId == "invalid.source").Status);
        Assert.Equal(AttentionContributorStatus.Ready, Assert.Single(result.Contributors, x => x.ContributorId == "valid.source").Status);
    }

    [Fact]
    public async Task Invalid_runtime_options_fail_the_request_before_contributors_run()
    {
        var called = false;
        var registry = new AttentionContributorRegistry(
        [
            new StubContributor("source", (_, _) =>
            {
                called = true;
                return ValueTask.FromResult(AttentionContribution.Ready([]));
            })
        ]);
        var service = CreateService(registry, new AttentionOptions { MaxItemsPerContributor = 0 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AggregateAsync(Query()));

        Assert.False(called);
    }

    [Fact]
    public void Wire_enums_serialize_with_contract_lower_camel_values()
    {
        var item = Item("wire");
        var result = new AttentionContributorResult(
            "source",
            "Source",
            AttentionContributorStatus.Ready,
            item.LastObservedAt,
            1,
            false,
            [item]);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":\"ready\"", json);
        Assert.Contains("\"severity\":\"warning\"", json);
        Assert.Contains("\"sensitivity\":\"metadata\"", json);
    }

    [Fact]
    public async Task Host_threshold_overrides_are_passed_to_contributors_and_restricted_summaries_are_suppressed_by_default()
    {
        string? horizon = null;
        var contributor = new StubContributor("secrets", (context, _) =>
        {
            horizon = context.Thresholds["expiryHorizon"];
            return ValueTask.FromResult(AttentionContribution.Ready(
            [
                Item("expiring") with
                {
                    Summary = "restricted detail",
                    Sensitivity = AttentionSensitivity.Restricted
                }
            ]));
        }, thresholds:
        [
            new AttentionThresholdDescriptor("expiryHorizon", "Expiry horizon", "Days before expiry.", "30")
        ]);
        var registry = new AttentionContributorRegistry([contributor]);
        var options = new AttentionOptions
        {
            ContributorThresholdOverrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["secrets"] = new(StringComparer.OrdinalIgnoreCase) { ["expiryHorizon"] = "7" }
            }
        };

        var result = await CreateService(registry, options).AggregateAsync(Query());

        Assert.Equal("7", horizon);
        Assert.Null(Assert.Single(Assert.Single(result.Contributors).Items).Summary);
    }

    private static AttentionAggregationService CreateService(
        AttentionContributorRegistry registry,
        AttentionOptions? options = null) =>
        new(registry, new DenyAllPermissionEvaluator(), options ?? new AttentionOptions());

    private static AttentionQuery Query() =>
        new(new AttentionQueryContext(new ClaimsPrincipal(), "tenant-1"));

    private static AttentionItem Item(string id)
    {
        var now = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        return new(
            id,
            "1",
            AttentionSeverity.Warning,
            $"Attention {id}",
            null,
            now,
            now,
            1,
            new AttentionDestination($"/attention/{id}", "Inspect"),
            [],
            AttentionSensitivity.Metadata);
    }

    private sealed class StubContributor : IAttentionContributor
    {
        private readonly Func<AttentionContributorContext, CancellationToken, ValueTask<AttentionContribution>> _evaluate;

        public StubContributor(string id, string? requiredPermission) : this(
            id,
            (_, _) => ValueTask.FromResult(AttentionContribution.Ready([])),
            requiredPermission)
        {
        }

        public StubContributor(
            string id,
            Func<AttentionContributorContext, CancellationToken, ValueTask<AttentionContribution>> evaluate,
            string? requiredPermission = null,
            IReadOnlyCollection<AttentionThresholdDescriptor>? thresholds = null)
        {
            Descriptor = new AttentionContributorDescriptor(id, id, requiredPermission, thresholds);
            _evaluate = evaluate;
        }

        public AttentionContributorDescriptor Descriptor { get; }

        public ValueTask<AttentionContribution> EvaluateAsync(AttentionContributorContext context, CancellationToken cancellationToken = default) =>
            _evaluate(context, cancellationToken);
    }

    private sealed class DenyAllPermissionEvaluator : IAttentionPermissionEvaluator
    {
        public ValueTask<bool> IsAllowedAsync(ClaimsPrincipal principal, string permission, string? tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
