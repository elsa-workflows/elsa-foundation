using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Acme.Processing.Core;
using Elsa.Events;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Acme.Processing.Tests;

public sealed class ProcessingQuestionTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Numeric_having_rejects_null_parameter_values_before_resource_admission(bool supplied)
    {
        using var host = new Host();
        await host.Create("retail", 60);
        var question = Question([new("count", "processing.record-count", "v1")]);
        question = question with { Query = question.Query with {
            Parameters = [new("minimum", "decimal", Nullable: true, Default: supplied ? new("decimal", "0") : new("null"))],
            Having = new() { Metric = "current.count", Operator = "gt", Value = new("decimal", Parameter: "minimum") }
        }, ParameterValues = supplied ? new Dictionary<string, DashboardValue> { ["minimum"] = new("null") } : new Dictionary<string, DashboardValue>() };

        var validation = await host.Query.ValidateAsync(host.Scope, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "INVALID_VALUE" && problem.Path == "query.having");
        var admission = await Assert.ThrowsAsync<DashboardResourceException>(() =>
            host.Validator.ValidateAsync(new("tenant-a", "alice", new HashSet<string>(), new HashSet<string>()), question).AsTask());
        Assert.Equal("validation", admission.Code);
        var execution = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.ExecuteAsync(host.Scope, question).AsTask());
        Assert.Equal("INVALID_VALUE", execution.Code);
    }

    [Theory]
    [InlineData("isNull", 0)]
    [InlineData("isNotNull", 1)]
    public async Task Having_null_tests_keep_their_operand_free_meaning(string operation, int expectedRows)
    {
        using var host = new Host();
        var question = Question([new("count", "processing.record-count", "v1")]);
        question = question with { Query = question.Query with {
            Having = new() { Metric = "current.count", Operator = operation } } };
        Assert.True((await host.Query.ValidateAsync(host.Scope, question)).IsValid);
        Assert.Equal(expectedRows, (await host.Query.ExecuteAsync(host.Scope, question)).Rows.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_current_metric_visualization_bindings_are_rejected_by_the_native_query_path(bool acrossAxes)
    {
        using var host = new Host();
        await host.Create("retail", 60);
        var question = Question([new("count", "processing.record-count", "v1")]);
        question = question with { Query = question.Query with { GroupBy = ["customerCategory"] },
            Visualization = new() { Kind = "bar", X = acrossAxes ? "count" : "customerCategory",
                Y = acrossAxes ? ["current.count"] : ["count", "current.count"] } };

        var validation = await host.Query.ValidateAsync(host.Scope, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "UNSUPPORTED_QUERY" && problem.Path == "visualization");
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.ExecuteAsync(host.Scope, question).AsTask());
        Assert.Equal("UNSUPPORTED_QUERY", failure.Code);
    }

    [Theory]
    [InlineData("count", false)]
    [InlineData("current.count", false)]
    [InlineData("count", true)]
    [InlineData("current.count", true)]
    public async Task One_current_metric_spelling_returns_bound_processing_columns_and_values(string binding, bool metricOnBothAxes)
    {
        using var host = new Host();
        await host.Create("retail", 60);
        await host.Create("retail", 120);
        var question = Question([new("count", "processing.record-count", "v1")]);
        question = question with { Query = question.Query with { GroupBy = ["customerCategory"] },
            Visualization = new() { Kind = "bar", X = metricOnBothAxes ? binding : "customerCategory", Y = [binding] } };

        Assert.True((await host.Query.ValidateAsync(host.Scope, question)).IsValid);
        var result = await host.Query.ExecuteAsync(host.Scope, question);
        var row = Assert.Single(result.Rows);
        Assert.Equal(2m, Number(row, binding));
        Assert.Contains(result.Columns, column => column.Id == binding);
        Assert.DoesNotContain(result.Columns, column => column.Id == (binding == "count" ? "current.count" : "count"));
        Assert.True(row.Values.ContainsKey(question.Visualization.X!));
        Assert.All(question.Visualization.Y, reference => Assert.True(row.Values.ContainsKey(reference)));
    }

    [Fact]
    public async Task Explicit_business_dimension_has_independently_checked_values_for_each_actor_and_tenant()
    {
        using var host = new Host();
        var retailShared = await host.Create("retail", 60, readers: ["bob"]);
        var retailPrivate = await host.Create("retail", 180);
        await host.Create("enterprise", 300, readers: ["bob"]);
        host.Actor.Set("tenant-b", "bob");
        await host.Create("retail", 900);
        host.Actor.Set("tenant-a", "alice");
        var full = await host.Query.ExecuteAsync(host.Scope, ByCategory());
        Assert.Equal(120000m, Number(Find(full, "retail"), "mean"));
        Assert.Equal(2m, Number(Find(full, "retail"), "count"));
        Assert.Equal(300000m, Number(Find(full, "enterprise"), "mean"));
        Assert.Equal("unknown", full.Coverage.Status);
        host.Actor.Set("tenant-a", "bob");
        var restricted = await host.Query.ExecuteAsync(host.Scope, ByCategory());
        Assert.Equal(60000m, Number(Find(restricted, "retail"), "mean"));
        Assert.Equal(1m, Number(Find(restricted, "retail"), "count"));
        Assert.Null(await host.Records.FindAsync(retailPrivate));
        var drill = await host.Query.DrillAsync(host.Scope, new(ByCategory(), Find(restricted, "retail").Drill!));
        Assert.Equal(retailShared, Assert.Single(drill.Rows).Values["recordId"].Value);
        host.Actor.Set("tenant-b", "bob");
        var otherTenant = await host.Query.ExecuteAsync(host.Scope, ByCategory());
        Assert.Equal(900000m, Number(Find(otherTenant, "retail"), "mean"));
        Assert.Single(otherTenant.Rows);
    }

    [Fact]
    public async Task Category_permission_is_separate_from_record_and_aggregate_grants()
    {
        using var host = new Host();
        var id = await host.Create("protected-gold", 60);
        host.Permission.Denied.Add(ProcessingPermissions.CategoryRead);
        var view = await host.Records.FindAsync(id);
        Assert.NotNull(view);
        Assert.False(view.CategoryAvailable);
        Assert.Null(view.CustomerCategory);
        var declaration = Assert.Single(await host.Query.DiscoverAsync(host.Scope));
        Assert.DoesNotContain(declaration.Fields, field => field.Id == "customerCategory");
        Assert.False((await host.Query.ValidateAsync(host.Scope, ByCategory())).IsValid);
        var count = Question([new("count", "processing.record-count", "v1")]);
        Assert.Equal(1m, Number(Assert.Single((await host.Query.ExecuteAsync(host.Scope, count)).Rows), "count"));
    }

    [Fact]
    public async Task Per_record_category_policy_is_applied_before_grouping_and_counts()
    {
        using var host = new Host();
        await host.Create("retail", 60);
        var hidden = await host.Create("retail", 180);
        host.Permission.HiddenCategoryRecords.Add(hidden);
        var row = Find(await host.Query.ExecuteAsync(host.Scope, ByCategory()), "retail");
        Assert.Equal(60000m, Number(row, "mean"));
        Assert.Equal(1m, Number(row, "count"));
    }

    [Fact]
    public async Task Module_failure_metric_excludes_in_flight_work_from_its_own_denominator()
    {
        using var host = new Host();
        await host.Create("retail", 60, "failed");
        await host.Create("retail", 120);
        await host.Create("retail", null, "processing");
        var question = Question([new("rate", "processing.failure-rate", "finished-denominator-v1"),
            new("all", "processing.record-count", "v1"), new("finished", "processing.finished-count", "v1")]);
        var row = Assert.Single((await host.Query.ExecuteAsync(host.Scope, question)).Rows);
        Assert.Equal(50m, Number(row, "rate"));
        Assert.Equal(3m, Number(row, "all"));
        Assert.Equal(2m, Number(row, "finished"));
    }

    [Fact]
    public async Task Exact_period_comparison_having_and_baseline_drill_are_module_owned()
    {
        using var host = new Host();
        for (var i = 0; i < 6; i++) await host.Create("retail", 60, i < 2 ? "failed" : "completed");
        for (var i = 0; i < 3; i++) await host.Create("retail", 60, i < 1 ? "failed" : "completed", previous: true);
        await host.Create("enterprise", 60, "failed");
        await host.Create("enterprise", 60, "completed", previous: true);
        var question = Question([new("rate", "processing.failure-rate", "finished-denominator-v1")]);
        question = question with { Query = question.Query with
        {
            GroupBy = ["customerCategory"], Comparison = new("absolute", Started.AddDays(-8), Started.AddDays(-6)),
            Having = new() { Metric = "delta.rate", Operator = "gt", Value = new("decimal", "0") }
        } };
        var result = await host.Query.ExecuteAsync(host.Scope, question);
        var row = Assert.Single(result.Rows);
        Assert.Equal("enterprise", row.Values["customerCategory"].Value);
        Assert.Equal(100m, Number(row, "rate"));
        Assert.Equal(0m, Number(row, "baseline.rate"));
        Assert.Equal(100m, Number(row, "delta.rate"));
        var baseline = await host.Query.DrillAsync(host.Scope, new(question, row.Drill! with { Period = "baseline" }));
        Assert.Equal("completed", Assert.Single(baseline.Rows).Values["status"].Value);
    }

    [Fact]
    public async Task Missing_durations_empty_sets_and_observed_zero_remain_distinct()
    {
        using var host = new Host();
        var question = Question([new("mean", "processing.average-time-ms", "finished-valid-pairs-v1")]);
        var empty = Assert.Single((await host.Query.ExecuteAsync(host.Scope, question)).Rows);
        Assert.Null(empty.Values["mean"].Value);
        Assert.Equal("noEligibleValues", empty.Values["mean"].NullReason);
        await host.Create("retail", null);
        var missing = Assert.Single((await host.Query.ExecuteAsync(host.Scope, question)).Rows);
        Assert.Null(missing.Values["mean"].Value);
        await host.Create("retail", 0);
        Assert.Equal(0m, Number(Assert.Single((await host.Query.ExecuteAsync(host.Scope, question)).Rows), "mean"));
        var bad = await Assert.ThrowsAsync<ProcessingException>(() => host.Create("retail", -1));
        Assert.Equal("validation", bad.Code);
    }

    [Theory]
    [InlineData("sum", 240000)]
    [InlineData("average", 120000)]
    [InlineData("min", 60000)]
    [InlineData("max", 180000)]
    [InlineData("count", 2)]
    public async Task New_questions_can_compose_exposed_field_aggregates_without_a_widget_catalog(string aggregate, int expected)
    {
        using var host = new Host();
        await host.Create("retail", 60);
        await host.Create("retail", 180);
        var result = await host.Query.ExecuteAsync(host.Scope, Question([new("value", Aggregate: aggregate, Field: "processingTimeMs")]));
        Assert.Equal(expected, Number(Assert.Single(result.Rows), "value"));
    }

    [Fact]
    public async Task Source_aggregate_grant_is_independent_of_normal_processing_read()
    {
        using var host = new Host();
        var id = await host.Create("retail", 60);
        host.Permission.Denied.Add(ProcessingPermissions.Query);
        Assert.NotNull(await host.Records.FindAsync(id));
        Assert.Empty(await host.Query.DiscoverAsync(host.Scope));
    }

    [Fact]
    public async Task Ordinary_processing_reads_use_current_actor_scope_and_safe_cursors()
    {
        using var host = new Host();
        var shared = new List<string>();
        var hidden = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            shared.Add(await host.Create("retail", 60, readers: ["bob"]));
            hidden.Add(await host.Create("secret", 600));
        }
        host.Actor.Set("tenant-a", "bob");
        var received = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        do
        {
            var page = await host.Records.ReadAsync(1, cursor);
            received.AddRange(page.Items.Select(record => record.Id));
            cursor = page.NextCursor;
            firstCursor ??= cursor;
            if (cursor is not null)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                Assert.DoesNotContain(hidden, id => decoded.Contains(id, StringComparison.Ordinal));
            }
        } while (cursor is not null);
        Assert.Equal(shared.Order(StringComparer.Ordinal), received.Order(StringComparer.Ordinal));
        Assert.Equal(shared.Count, received.Distinct(StringComparer.Ordinal).Count());
        host.Actor.Set("tenant-b", "bob");
        var denied = await Assert.ThrowsAsync<ProcessingException>(() => host.Records.ReadAsync(1, firstCursor).AsTask());
        Assert.Equal("validation", denied.Code);
    }

    [Fact]
    public async Task Permission_change_invalidates_old_drill_membership_without_reusing_a_result_cache()
    {
        using var host = new Host();
        var id = await host.Create("retail", 60);
        var question = ByCategory();
        var result = await host.Query.ExecuteAsync(host.Scope, question);
        host.Permission.HiddenRecords.Add(id);
        Assert.Empty((await host.Query.ExecuteAsync(host.Scope, question)).Rows);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.DrillAsync(host.Scope, new(question, result.Rows[0].Drill!)).AsTask());
        Assert.Equal("INVALID_DRILL", failure.Code);
    }

    [Fact]
    public async Task Row_and_group_bounds_refuse_partial_business_aggregates()
    {
        using var host = new Host(new() { MaximumSourceRows = 1 });
        await host.Create("retail", 60); await host.Create("enterprise", 120);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.ExecuteAsync(host.Scope, ByCategory()).AsTask());
        Assert.Equal("QUERY_LIMIT", failure.Code);
    }

    [Fact]
    public async Task Native_module_create_read_and_unknown_owner_fields_use_the_ordinary_capability()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var actor = new ActorContext();
        var permission = new PermissionService();
        builder.Services.AddSingleton<IProcessingActorContext>(actor);
        builder.Services.AddSingleton<IPermissionAuthorizationService>(permission);
        var feature = new ProcessingFeature(); feature.ConfigureServices(builder.Services);
        await using var app = builder.Build();
        app.Use((context, next) => { context.User = Principal(actor); return next(context); });
        feature.MapEndpoints(app, app.Environment);
        await app.StartAsync();
        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/_acme/processing/records", new ProcessingCreate("retail", Started, Started.AddSeconds(60), "completed"));
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var created = await client.GetFromJsonAsync<ProcessingRecordView>("/_acme/processing/records/" + receipt!["id"]);
        Assert.Equal("retail", created!.CustomerCategory);
        var malicious = new StringContent(JsonSerializer.Serialize(new { customerCategory = "retail", startedAt = Started, completedAt = Started.AddSeconds(1), status = "completed", tenantId = "tenant-b", ownerId = "other" }), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/_acme/processing/records", malicious)).StatusCode);
    }

    [Fact]
    public void Contribution_package_depends_on_public_contracts_and_no_workflow_or_dashboard_implementation()
    {
        var references = typeof(ProcessingFeature).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        Assert.Contains("Elsa.Studio.Dashboard.Core", references);
        Assert.DoesNotContain(references, name => name!.StartsWith("Elsa.Workflows", StringComparison.Ordinal) || name is "Elsa.Studio.Dashboard.Management" or "Elsa.Studio.Dashboard.Api");
    }

    [Theory]
    [InlineData(typeof(IProcessingActorContext))]
    [InlineData(typeof(IProcessingRecordReader))]
    [InlineData(typeof(IProcessingRecordWriter))]
    [InlineData(typeof(IProcessingRecords))]
    public async Task Duplicate_replacement_registered_after_feature_prevents_host_start(Type contract)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IProcessingActorContext>(new ActorContext());
        builder.Services.AddSingleton<IPermissionAuthorizationService>(new PermissionService());
        new ProcessingFeature().ConfigureServices(builder.Services);
        builder.Services.Add(builder.Services.Single(descriptor => descriptor.ServiceType == contract));
        await using var app = builder.Build();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
        Assert.Contains(contract.Name, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("completedAt")]
    [InlineData("processingTimeMs")]
    public async Task Aggregate_grant_does_not_disclose_forbidden_drill_projection_fields(string field)
    {
        using var host = new Host(); await host.Create("retail", 60);
        var question = Question([new("count", "processing.record-count", "v1")]);
        var preview = await host.Query.ExecuteAsync(host.Scope, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        host.Permission.Predicate = context => context.Permission != ProcessingPermissions.Query ||
            context.Resource is not DashboardQueryDefinition query || query.Form != "records" || !query.Fields.Contains(field);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.DrillAsync(host.Scope, new(question, descriptor)).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", failure.Code);
        host.Permission.Predicate = _ => true;
        Assert.Single((await host.Query.DrillAsync(host.Scope, new(question, descriptor))).Rows);
    }

    [Fact]
    public async Task Baseline_drill_uses_its_actual_record_period_for_source_authorization()
    {
        using var host = new Host(); await host.Create("retail", 60); await host.Create("retail", 120, previous: true);
        var question = Question([new("count", "processing.record-count", "v1")]);
        question = question with { Query = question.Query with { Comparison = new("absolute", Started.AddDays(-8), Started.AddDays(-6)) } };
        var preview = await host.Query.ExecuteAsync(host.Scope, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        host.Permission.Predicate = context => context.Permission != ProcessingPermissions.Query ||
            context.Resource is not DashboardQueryDefinition query || query.Form != "records" || query.Time?.From >= Started.AddDays(-1);
        Assert.Single((await host.Query.DrillAsync(host.Scope, new(question, descriptor))).Rows);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Query.DrillAsync(host.Scope, new(question, descriptor with { Period = "baseline" })).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", failure.Code);
    }

    private static DashboardQueryRequest Question(IReadOnlyList<DashboardMetricSelection> metrics) => new()
    {
        Query = new()
        {
            Dataset = new("acme.processing.records", "processing-v1"), Metrics = metrics,
            Time = new() { Field = "startedAt", From = Started.AddDays(-1), To = Started.AddDays(1) }
        }
    };
    private static DashboardQueryRequest ByCategory()
    {
        var question = Question([new("mean", "processing.average-time-ms", "finished-valid-pairs-v1"), new("count", "processing.record-count", "v1")]);
        return question with { Query = question.Query with { GroupBy = ["customerCategory"] }, Visualization = new() { Kind = "bar", X = "customerCategory", Y = ["mean"] } };
    }
    private static DashboardResultRow Find(DashboardQueryResult result, string category) => result.Rows.Single(row => row.Values["customerCategory"].Value == category);
    private static decimal Number(DashboardResultRow row, string metric) => decimal.Parse(row.Values[metric].Value!, CultureInfo.InvariantCulture);
    private static ClaimsPrincipal Principal(ActorContext actor) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Current.SubjectId)], "test"));

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _provider; private readonly IServiceScope _scope;
        public ActorContext Actor { get; } = new(); public PermissionService Permission { get; } = new();
        public IProcessingRecords Records { get; } public IDashboardQueryService Query { get; }
        public IDashboardDefinitionValidator Validator { get; }
        public DashboardRequestScope Scope => new(Actor.Current.TenantId, Actor.Current.SubjectId, "studio");
        public Host(DashboardQueryLimits? limits = null)
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Started.AddDays(2)));
            services.AddSingleton<IProcessingActorContext>(Actor); services.AddSingleton<IPermissionAuthorizationService>(Permission);
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(Actor) } };
            Actor.Changed = () => http.HttpContext!.User = Principal(Actor);
            services.AddSingleton<IHttpContextAccessor>(http);
            if (limits is not null) services.AddSingleton(limits);
            new EventsFeature().ConfigureServices(services); new DashboardManagementFeature().ConfigureServices(services); new ProcessingFeature().ConfigureServices(services);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); _scope = _provider.CreateScope();
            Records = _scope.ServiceProvider.GetRequiredService<IProcessingRecords>(); Query = _scope.ServiceProvider.GetRequiredService<IDashboardQueryService>();
            Validator = _scope.ServiceProvider.GetRequiredService<IDashboardDefinitionValidator>();
        }
        public Task<string> Create(string category, int? seconds, string status = "completed", IReadOnlyList<string>? readers = null, bool previous = false)
        {
            var start = previous ? Started.AddDays(-7) : Started;
            return Records.CreateAsync(new(category, start, seconds is { } elapsed ? start.AddSeconds(elapsed) : null, status, readers)).AsTask();
        }
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
    private sealed class ActorContext : IProcessingActorContext
    {
        public ProcessingActor Current { get; private set; } = new("tenant-a", "alice"); public Action? Changed { get; set; }
        public void Set(string tenant, string subject) { Current = new(tenant, subject); Changed?.Invoke(); }
        public ValueTask<ProcessingActor> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);
    }
    private sealed class PermissionService : IPermissionAuthorizationService
    {
        public Func<PermissionEvaluationContext, bool> Predicate { get; set; } = _ => true;
        public HashSet<string> Denied { get; } = []; public HashSet<string> HiddenRecords { get; } = []; public HashSet<string> HiddenCategoryRecords { get; } = [];
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(!Predicate(context) || Denied.Contains(context.Permission) || context.Resource is ProcessingRecord record &&
                (context.Permission == ProcessingPermissions.Read && HiddenRecords.Contains(record.Id) || context.Permission == ProcessingPermissions.CategoryRead && HiddenCategoryRecords.Contains(record.Id))
                ? PermissionEvaluationResult.Denied() : PermissionEvaluationResult.Success);
    }
}
