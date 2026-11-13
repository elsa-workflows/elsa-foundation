using System.Text.Json;
using Elsa.Events;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Studio.Dashboard.QueryTests;

public sealed class DashboardQueryTests
{
    private static readonly DashboardRequestScope Caller = new("tenant-a", "reader-a", "studio");

    [Fact]
    public async Task Native_execution_dispatches_module_contracts_and_never_reuses_a_result()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var first = await host.Service.ExecuteAsync(Caller, Question());
        var second = await host.Service.ExecuteAsync(Caller with { SubjectId = "reader-b" }, Question());
        Assert.Equal("succeeded", first.Outcome);
        Assert.Equal("unknown", first.Coverage.Status);
        Assert.Equal(2, source.ExecutionCalls);
        Assert.NotEqual(first.QueryFingerprint, second.QueryFingerprint);
        Assert.Equal("reader-a", source.Executed[0].Scope.SubjectId);
        Assert.Equal("reader-b", source.Executed[1].Scope.SubjectId);
    }

    [Fact]
    public async Task Duplicate_hidden_owner_fails_before_any_discovery_or_query()
    {
        var first = new ExampleSource { Hidden = true };
        var second = new ExampleSource { Hidden = true };
        using var host = new Host(first, second);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.DiscoverAsync(Caller).AsTask());
        Assert.Equal("DATASET_OWNER_CONFLICT", failure.Code);
        Assert.Equal(0, first.DiscoveryCalls);
        Assert.Equal(0, second.DiscoveryCalls);
    }

    [Fact]
    public async Task Declaration_cannot_impersonate_another_registered_owner()
    {
        using var host = new Host(new ExampleSource { Declaration = Dataset() with { OwnerModuleId = "other-module" } });
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.DiscoverAsync(Caller).AsTask());
        Assert.Equal("DATASET_OWNER_CONFLICT", failure.Code);
    }

    [Fact]
    public async Task Absent_dataset_is_unavailable_not_a_successful_empty_result()
    {
        var source = new ExampleSource { Hidden = true };
        using var host = new Host(source);
        var validation = await host.Service.ValidateAsync(Caller, Question());
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "DATASET_UNAVAILABLE");
        Assert.Equal(0, source.ExecutionCalls);
    }

    [Fact]
    public async Task Protected_filter_field_is_rejected_even_when_not_projected()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var question = Question();
        question = question with { Query = question.Query with { Filters = new() { Field = "secret", Operator = "eq", Value = new("string", "hidden") } } };
        var validation = await host.Service.ValidateAsync(Caller, question);
        Assert.False(validation.IsValid);
        Assert.Equal(0, source.ValidationCalls);
        Assert.Equal(0, source.ExecutionCalls);
    }

    [Fact]
    public async Task Parameters_are_typed_and_explicit_defaults_reach_the_owner()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var question = Question();
        question = question with { Query = question.Query with
        {
            Parameters = [new("minimum", "decimal", true, Default: new("decimal", "12.50"))],
            Filters = new() { Field = "amount", Operator = "gte", Value = new("decimal", Parameter: "minimum") }
        } };
        await host.Service.ExecuteAsync(Caller, question);
        Assert.Equal(new DashboardValue("decimal", "12.50"), source.Executed.Single().Parameters["minimum"]);
        var invalid = question with { ParameterValues = new Dictionary<string, DashboardValue> { ["minimum"] = new("string", "twelve") } };
        Assert.False((await host.Service.ValidateAsync(Caller, invalid)).IsValid);
        Assert.Equal(1, source.ExecutionCalls);
    }

    [Fact]
    public async Task An_optional_parameter_cannot_silently_disappear_when_referenced()
    {
        using var host = new Host(new ExampleSource());
        var question = Question();
        question = question with { Query = question.Query with
        {
            Parameters = [new("minimum", "decimal")],
            Filters = new() { Field = "amount", Operator = "gte", Value = new("decimal", Parameter: "minimum") }
        } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Fact]
    public async Task Nullable_parameter_cannot_bypass_nonnullable_field_semantics()
    {
        using var host = new Host(new ExampleSource());
        var question = Question();
        question = question with { Query = question.Query with
        {
            Parameters = [new("minimum", "decimal", Nullable: true, Default: new("null"))],
            Filters = new() { Field = "amount", Operator = "gte", Value = new("decimal", Parameter: "minimum") }
        } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("regex")]
    [InlineData("javascript")]
    public async Task Unsupported_predicate_operator_never_reaches_the_module(string operation)
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var question = Question();
        question = question with { Query = question.Query with { Filters = new() { Field = "amount", Operator = operation, Value = new("decimal", "1") } } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
        Assert.Equal(0, source.DiscoveryCalls);
    }

    [Fact]
    public async Task Deep_predicate_and_output_bounds_fail_before_discovery()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        DashboardPredicate predicate = new() { Field = "amount", Operator = "gt", Value = new("decimal", "1") };
        for (var i = 0; i < 4; i++) predicate = new() { All = [predicate] };
        var question = Question();
        question = question with { Query = question.Query with { Filters = predicate, Limit = 201 } };
        var validation = await host.Service.ValidateAsync(Caller, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "QUERY_LIMIT");
        Assert.Equal(0, source.DiscoveryCalls);
    }

    [Fact]
    public async Task Having_baseline_requires_a_declared_comparison()
    {
        using var host = new Host(new ExampleSource());
        var question = Question();
        question = question with { Query = question.Query with { Having = new() { Metric = "delta.total", Operator = "gt", Value = new("decimal", "0") } } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Fact]
    public async Task Visualizations_cannot_bind_unreturned_or_nonnumeric_columns()
    {
        using var host = new Host(new ExampleSource());
        var question = Question() with { Visualization = new() { Kind = "metric", Y = ["secret"] } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_plain_and_current_metric_visualization_bindings_are_rejected_before_module_validation(bool acrossAxes)
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var question = Question() with { Visualization = new() { Kind = "bar",
            X = acrossAxes ? "total" : "current.total",
            Y = acrossAxes ? ["current.total"] : ["total", "current.total"] } };

        var validation = await host.Service.ValidateAsync(Caller, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "UNSUPPORTED_QUERY" && problem.Path == "visualization");
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.ExecuteAsync(Caller, question).AsTask());
        Assert.Equal("UNSUPPORTED_QUERY", failure.Code);
        Assert.Equal(0, source.ValidationCalls);
        Assert.Equal(0, source.ExecutionCalls);
    }

    [Fact]
    public async Task Distinct_metric_aliases_can_choose_different_current_spellings()
    {
        using var host = new Host(new ExampleSource());
        var question = Question();
        question = question with { Query = question.Query with { Metrics = [new("total", "amount-total", "v1"), new("other", "amount-total", "v1")] },
            Visualization = new() { Kind = "bar", X = "total", Y = ["current.other"] } };
        Assert.True((await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Fact]
    public async Task Time_periods_are_resolved_once_and_exposed_with_their_meaning()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var result = await host.Service.ExecuteAsync(Caller, WeeklyQuestion());
        Assert.Equal(2, result.Periods.Count);
        var current = result.Periods.Single(period => period.Role == "current");
        var baseline = result.Periods.Single(period => period.Role == "baseline");
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 0, 0, TimeSpan.Zero), current.From);
        Assert.Equal(current.To - current.From, baseline.To - baseline.From);
        Assert.Equal(host.Clock.GetUtcNow(), source.Executed.Single().EvaluatedAt);
    }

    [Fact]
    public async Task Dst_week_overflow_requires_an_explicit_alternative()
    {
        using var host = new Host(new DateTimeOffset(2026, 10, 25, 22, 30, 0, TimeSpan.Zero), new ExampleSource());
        var validation = await host.Service.ValidateAsync(Caller, WeeklyQuestion());
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "INVALID_TIME");
    }

    [Fact]
    public async Task Caller_cancellation_reaches_the_module_and_does_not_poison_admission()
    {
        var source = new ExampleSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Execute = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        using var host = new Host(source);
        using var cancellation = new CancellationTokenSource();
        var pending = host.Service.ExecuteAsync(Caller, Question(), cancellation.Token).AsTask();
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        source.Execute = null;
        Assert.Equal("succeeded", (await host.Service.ExecuteAsync(Caller, Question())).Outcome);
    }

    [Fact]
    public async Task Execution_deadline_is_distinct_from_caller_cancellation()
    {
        var source = new ExampleSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Execute = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        using var host = new Host(source);
        var pending = host.Service.ExecuteAsync(Caller, Question()).AsTask();
        await started.Task;
        host.Clock.Advance(TimeSpan.FromSeconds(11));
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => pending);
        Assert.Equal("QUERY_TIMEOUT", failure.Code);
    }

    [Fact]
    public async Task Concurrency_is_bounded_per_viewer_and_released_after_completion()
    {
        var source = new ExampleSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Execute = async (context, token) => { await release.Task.WaitAsync(token); context.Results.Add(Result()); };
        using var host = new Host(source);
        var first = host.Service.ExecuteAsync(Caller, Question()).AsTask();
        var second = host.Service.ExecuteAsync(Caller, Question()).AsTask();
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.ExecuteAsync(Caller, Question()).AsTask());
        Assert.Equal("QUERY_CONCURRENCY_LIMIT", failure.Code);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal("succeeded", (await host.Service.ExecuteAsync(Caller, Question())).Outcome);
    }

    [Fact]
    public async Task Infrastructure_failure_is_wrapped_without_public_source_detail()
    {
        var source = new ExampleSource { Execute = (_, _) => ValueTask.FromException(new IOException("protected database endpoint")) };
        using var host = new Host(source);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.ExecuteAsync(Caller, Question()).AsTask());
        Assert.Equal("QUERY_SOURCE_FAILED", failure.Code);
        Assert.DoesNotContain("protected", failure.Message);
        Assert.IsType<IOException>(failure.InnerException);
    }

    [Fact]
    public async Task Failed_source_result_cannot_smuggle_partial_aggregate_values()
    {
        var source = new ExampleSource { Execute = (context, _) => { context.Results.Add(Result() with { Outcome = "limitExceeded" }); return ValueTask.CompletedTask; } };
        using var host = new Host(source);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.ExecuteAsync(Caller, Question()).AsTask());
        Assert.Equal("INVALID_SOURCE_RESULT", failure.Code);
    }

    [Fact]
    public async Task Drill_is_bound_to_original_periods_viewer_and_current_semantic_validation()
    {
        var source = new ExampleSource();
        using var host = new Host(source);
        var question = WeeklyQuestion();
        await host.Service.ExecuteAsync(Caller, question);
        var context = source.Executed.Single();
        var descriptor = new DashboardDrillDescriptor(context.Dataset.Id, context.Dataset.SchemaRevision, context.QueryFingerprint,
            "current", new Dictionary<string, DashboardValue>(), context.Periods, context.EvaluatedAt);
        host.Clock.Advance(TimeSpan.FromHours(1));
        var result = await host.Service.DrillAsync(Caller, new(question, descriptor));
        Assert.Equal(context.Periods, result.Periods);
        Assert.Equal(1, source.DrillCalls);
        var changedScope = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Service.DrillAsync(Caller with { SubjectId = "other" }, new(question, descriptor)).AsTask());
        Assert.Equal("INVALID_DRILL", changedScope.Code);
        Assert.Equal(1, source.DrillCalls);
    }

    [Fact]
    public void Native_wire_roundtrips_typed_defaults_and_predicate_values()
    {
        var question = Question() with { ParameterValues = new Dictionary<string, DashboardValue> { ["threshold"] = new("decimal", "123.450") } };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var text = JsonSerializer.Serialize(question, options);
        var roundtrip = JsonSerializer.Deserialize<DashboardQueryRequest>(text, options)!;
        Assert.Equal("123.450", roundtrip.ParameterValues["threshold"].Literal);
        Assert.Contains("\"schemaVersion\":1", text);
        Assert.Contains("\"parameterValues\"", text);
    }

    [Theory]
    [InlineData("current.total")]
    [InlineData("baseline.total")]
    [InlineData("delta.total")]
    public async Task Group_output_cannot_collide_with_a_period_metric(string field)
    {
        var source = new ExampleSource { Declaration = Dataset() with { Fields = [.. Dataset().Fields,
            new(field, "Period-shaped category", "string", ["eq"], Groupable: true)] } };
        using var host = new Host(source);
        var question = WeeklyQuestion();
        question = question with { Query = question.Query with { GroupBy = [field] },
            Visualization = new() { Kind = "table", Y = ["total"] } };
        Assert.False((await host.Service.ValidateAsync(Caller, question)).IsValid);
        Assert.Equal(0, source.ValidationCalls);
    }

    [Theory]
    [InlineData("string", "min")]
    [InlineData("string", "max")]
    [InlineData("instant", "min")]
    [InlineData("instant", "max")]
    [InlineData("boolean", "min")]
    [InlineData("boolean", "max")]
    [InlineData("decimal", "min")]
    [InlineData("integer", "max")]
    public async Task Primitive_min_max_requires_numeric_fields(string type, string aggregate)
    {
        var source = new ExampleSource { Declaration = Dataset() with { Fields = [
            new("value", "Value", type, [], Aggregates: [aggregate])] } };
        using var host = new Host(source);
        var question = Question();
        question = question with { Query = question.Query with { Metrics = [new("total", Aggregate: aggregate, Field: "value")] } };
        Assert.Equal(type is "integer" or "decimal", (await host.Service.ValidateAsync(Caller, question)).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Noncooperative_validation_cannot_succeed_after_cancellation_or_deadline(bool deadline)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ExampleSource { Validate = async (context, _) =>
        { started.SetResult(); await release.Task; context.Results.Add(new(true, [])); } };
        using var host = new Host(source);
        using var cancellation = new CancellationTokenSource();
        var pending = host.Service.ValidateAsync(Caller, Question(), cancellation.Token).AsTask();
        await started.Task;
        if (deadline) host.Clock.Advance(TimeSpan.FromSeconds(11)); else cancellation.Cancel();
        release.SetResult();
        if (deadline)
            Assert.Equal("QUERY_TIMEOUT", (await Assert.ThrowsAsync<DashboardQueryException>(() => pending)).Code);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        source.Validate = null;
        Assert.True((await host.Service.ValidateAsync(Caller, Question())).IsValid);
    }

    private static DashboardQueryRequest Question() => new()
    {
        Query = new() { Dataset = new("example.records", "v1"), Metrics = [new("total", "amount-total", "v1")] },
        Visualization = new() { Kind = "metric", Y = ["total"] }
    };

    private static DashboardQueryRequest WeeklyQuestion()
    {
        var question = Question();
        return question with { Query = question.Query with
        {
            Time = new() { Field = "startedAt", Window = "calendarWeekToDate", TimeZone = "Europe/Amsterdam" },
            Comparison = new("previousCalendarWeekSameElapsed"),
            Having = new() { Metric = "delta.total", Operator = "gt", Value = new("decimal", "0") }
        } };
    }

    private static DashboardDatasetDeclaration Dataset() => new()
    {
        Id = "example.records", OwnerModuleId = "Example", SchemaRevision = "v1", Title = "Example records",
        Fields = [new("amount", "Amount", "decimal", ["eq", "gte", "gt"], Sortable: true, Aggregates: ["sum", "average"]),
            new("category", "Category", "string", ["eq"], Groupable: true, Sortable: true, BusinessDimension: true),
            new("startedAt", "Started", "instant", ["gte"], Groupable: true, Sortable: true)],
        Metrics = [new("amount-total", "v1", "Total amount", "USD", "Sum of admitted amounts.", true, ["amount"])],
        Time = new(["startedAt"], ["absolute", "calendarWeekToDate", "last7Days", "last30Days"], ["hour", "day"], ["absolute", "previousCalendarWeekSameElapsed"]),
        SupportsDrill = true
    };

    private static DashboardQueryResult Result() => new()
    {
        Columns = [new("total", "Total amount", "decimal", "USD")],
        Rows = [new("one", new Dictionary<string, DashboardResultCell> { ["total"] = new("decimal", "42") })]
    };

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        public FakeTimeProvider Clock { get; }
        public IDashboardQueryService Service { get; }

        public Host(params ExampleSource[] sources) : this(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), sources) { }

        public Host(DateTimeOffset now, params ExampleSource[] sources)
        {
            Clock = new FakeTimeProvider(now);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            new EventsFeature().ConfigureServices(services);
            new DashboardManagementFeature().ConfigureServices(services);
            foreach (var source in sources)
            {
                services.AddSingleton<IDashboardDatasetExtension>(source);
                services.AddSingleton<IDashboardQueryValidationExtension>(source);
                services.AddSingleton<IDashboardQueryExecutionExtension>(source);
                services.AddSingleton<IDashboardQueryDrillExtension>(source);
            }
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            Service = _scope.ServiceProvider.GetRequiredService<IDashboardQueryService>();
        }

        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }

    private sealed class ExampleSource : IDashboardDatasetExtension, IDashboardQueryValidationExtension,
        IDashboardQueryExecutionExtension, IDashboardQueryDrillExtension
    {
        public IReadOnlyCollection<DashboardDatasetOwner> Ownership => [new("example.records", "Example")];
        public DashboardDatasetDeclaration Declaration { get; init; } = Dataset();
        public bool Hidden { get; init; }
        public int DiscoveryCalls { get; private set; }
        public int ValidationCalls { get; private set; }
        public int ExecutionCalls { get; private set; }
        public int DrillCalls { get; private set; }
        public List<DashboardQueryContext> Executed { get; } = [];
        public Func<DashboardQueryValidating, CancellationToken, ValueTask>? Validate { get; set; }
        public Func<DashboardQueryExecuting, CancellationToken, ValueTask>? Execute { get; set; }
        public ValueTask CollectAsync(DashboardDatasetsCollecting context, CancellationToken cancellationToken)
        { DiscoveryCalls++; if (!Hidden) context.Datasets.Add(Declaration); return ValueTask.CompletedTask; }
        public ValueTask ValidateAsync(DashboardQueryValidating context, CancellationToken cancellationToken)
        { ValidationCalls++; if (Validate is not null) return Validate(context, cancellationToken); context.Results.Add(new(true, [], "Sum of admitted amounts.")); return ValueTask.CompletedTask; }
        public ValueTask ExecuteAsync(DashboardQueryExecuting context, CancellationToken cancellationToken)
        {
            ExecutionCalls++; Executed.Add(context.Query);
            if (Execute is not null) return Execute(context, cancellationToken);
            context.Results.Add(Result()); return ValueTask.CompletedTask;
        }
        public ValueTask DrillAsync(DashboardQueryDrilling context, CancellationToken cancellationToken)
        { DrillCalls++; context.Results.Add(Result()); return ValueTask.CompletedTask; }
    }
}
