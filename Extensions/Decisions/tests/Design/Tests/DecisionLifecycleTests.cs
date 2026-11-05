using System.Text.Json;
using Elsa.Decisions;
using Elsa.Decisions.Core;
using Elsa.Decisions.Design;
using Elsa.Decisions.Persistence.Core;
using Elsa.Decisions.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Decisions.Design.Tests;

public sealed class DecisionLifecycleTests
{
    [Fact]
    public async Task Publish_freezes_content_and_direct_evaluation_ignores_later_draft()
    {
        await using var fixture = await Fixture.Create();
        var service = fixture.Service;
        var asset = await service.SaveAsync(fixture.Caller, null, new("create", "Approval", Fixture.Xml));
        var version = await service.PublishAsync(fixture.Caller, asset.Id, new("publish", asset.Revision));
        var afterPublish = await service.GetAsync(fixture.Caller, asset.Id);
        _ = await service.SaveAsync(fixture.Caller, asset.Id, new("edit", "Approval", Fixture.Xml.Replace("1000", "900"), ExpectedRevision: afterPublish.Revision));
        var replayed = await service.PublishAsync(fixture.Caller, asset.Id, new("publish", asset.Revision));
        Assert.Equal(version.Id, replayed.Id);
        var result = await service.EvaluateAsync(fixture.Caller, asset.Id, new(version.Id, Fixture.Input("950")));
        Assert.Equal("Success", result.Status);
        Assert.Equal("Automatic", result.Result.GetProperty("value").GetString());
        Assert.Equal(Fixture.Xml, await service.ExportAsync(fixture.Caller, asset.Id, version.Id));
    }

    [Fact]
    public async Task Saved_cases_detect_regression_and_summary_replay_precedes_changed_cases()
    {
        await using var fixture = await Fixture.Create();
        var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        var savedCase = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "boundary", Fixture.Input("950"), "Success", Fixture.Expected("Automatic")));
        var request = new TestDecisionRequest("test-1", asset.SourceHash, [new(savedCase.Id, savedCase.Revision)], asset.Revision);
        var run = await service.TestAsync(caller, asset.Id, request);
        Assert.True(run.Passed); Assert.True(run.HasDetailedPayload);
        var history = await service.GetRunAsync(caller, asset.Id, run.Id);
        Assert.False(history.HasDetailedPayload); Assert.Null(history.CaseInputs); Assert.Null(history.Comparisons[0].Expected); Assert.Null(history.Comparisons[0].Actual);
        _ = await service.SaveCaseAsync(caller, asset.Id, savedCase.Id, new("case-edit", "changed", Fixture.Input("1100"), "Success", Fixture.Expected("Manager"), savedCase.Revision));
        var replay = await service.TestAsync(caller, asset.Id, request);
        Assert.Equal(run.Id, replay.Id); Assert.True(replay.Passed); Assert.False(replay.HasDetailedPayload);
        var edited = await service.SaveAsync(caller, asset.Id, new("regress", "Approval", Fixture.Xml.Replace("1000", "900"), ExpectedRevision: asset.Revision));
        var frozenCase = (await fixture.Reader.ListCasesAsync(caller.TenantId, asset.Id))[0];
        _ = await service.SaveCaseAsync(caller, asset.Id, frozenCase.Id, new("restorecase", "boundary", Fixture.Input("950"), "Success", Fixture.Expected("Automatic"), frozenCase.Revision));
        var currentCase = (await fixture.Reader.ListCasesAsync(caller.TenantId, asset.Id))[0];
        var failed = await service.TestAsync(caller, asset.Id, new("regression", edited.SourceHash, [new(currentCase.Id, currentCase.Revision)], edited.Revision));
        Assert.False(failed.Passed); Assert.Equal("$.value", failed.Comparisons[0].FailurePath);
    }

    [Fact]
    public async Task Interrupted_unrecorded_request_cannot_choose_a_new_case_revision()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        var request = new TestDecisionRequest("not-recorded", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision);
        _ = await service.SaveCaseAsync(caller, asset.Id, item.Id, new("editcase", "one", Fixture.Input("2000"), "Success", Fixture.Expected("Manager"), item.Revision));
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.TestAsync(caller, asset.Id, request));
        Assert.Equal("conflict", error.Code); Assert.Empty(await fixture.Reader.ListRunsAsync(caller.TenantId, asset.Id));
    }

    [Fact]
    public async Task Changed_request_under_completed_operation_conflicts()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        var request = new TestDecisionRequest("same", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision);
        _ = await service.TestAsync(caller, asset.Id, request);
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.TestAsync(caller, asset.Id, request with { CandidateHash = new string('0', 64) }));
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Tenant_reads_never_find_another_tenants_version_case_or_receipt()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        var version = await service.PublishAsync(caller, asset.Id, new("publish", asset.Revision));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        Assert.Null(await fixture.Reader.FindAssetAsync("tenant-b", asset.Id));
        Assert.Null(await fixture.Reader.FindVersionAsync("tenant-b", asset.Id, version.Id));
        Assert.Null(await fixture.Reader.FindCaseAsync("tenant-b", asset.Id, item.Id));
        Assert.Empty(await fixture.Reader.ListAssetsAsync("tenant-b"));
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.EvaluateAsync(new("tenant-b", "user"), asset.Id, new(version.Id, Fixture.Input("1"))));
        Assert.Equal("not-found", error.Code);
    }

    [Fact]
    public async Task Authoritative_revision_compare_and_swap_has_one_concurrent_winner()
    {
        await using var fixture = await Fixture.Create(); var caller = fixture.Caller;
        var asset = await fixture.Service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        async Task<bool> Save(string operation)
        {
            try { _ = await fixture.Service.SaveAsync(caller, asset.Id, new(operation, operation, Fixture.Xml, ExpectedRevision: asset.Revision)); return true; }
            catch (DecisionDesignException ex) when (ex.Code == "conflict") { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Save("left")), Task.Run(() => Save("right")));
        Assert.Single(outcomes, x => x);
        Assert.NotEqual(asset.Revision, (await fixture.Reader.FindAssetAsync(caller.TenantId, asset.Id))!.Revision);
    }

    [Fact]
    public async Task Raced_publication_creates_no_orphan_immutable_version()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        _ = await service.SaveAsync(caller, asset.Id, new("edit", "Approval", Fixture.Xml, ExpectedRevision: asset.Revision));
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.PublishAsync(caller, asset.Id, new("publish", asset.Revision)));
        Assert.Equal("conflict", error.Code); Assert.Empty(await fixture.Reader.ListVersionsAsync(caller.TenantId, asset.Id));
    }

    [Fact]
    public async Task Unsupported_document_retains_original_xml_but_cannot_publish()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var xml = Fixture.Xml.Replace("<decisionTable", "<decisionTable aggregation=\"SUM\"");
        var asset = await service.SaveAsync(caller, null, new("create", "Unsupported", xml));
        Assert.Equal(xml, await service.ExportAsync(caller, asset.Id, null));
        var validation = await service.ValidateAsync(caller, asset.Id, new(asset.Revision));
        Assert.False(validation.Success);
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.PublishAsync(caller, asset.Id, new("publish", asset.Revision)));
        Assert.Equal("unsupported", error.Code);
    }

    [Theory]
    [InlineData("<!DOCTYPE definitions [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><definitions>&e;</definitions>")]
    [InlineData("<broken>")]
    public async Task Unsafe_xml_never_enters_design_storage(string xml)
    {
        await using var fixture = await Fixture.Create();
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Bad", xml)));
        Assert.Equal("invalid", error.Code); Assert.Empty(await fixture.Reader.ListAssetsAsync(fixture.Caller.TenantId));
    }

    [Fact]
    public async Task Revocation_source_and_sensitive_values_are_independent()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Protected", Fixture.Xml, IsSensitive: true));
        var version = await service.PublishAsync(caller, asset.Id, new("publish", asset.Revision));
        fixture.Policy.Denied.Add("decisions:sensitive-values");
        Assert.Null((await service.GetAsync(caller, asset.Id)).Xml);
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.EvaluateAsync(caller, asset.Id, new(version.Id, Fixture.Input("1"))));
        Assert.Equal("forbidden", error.Code);
        fixture.Policy.Denied.Clear(); fixture.Policy.Denied.Add("decisions:source");
        Assert.Null((await service.GetAsync(caller, asset.Id)).Xml);
        Assert.Equal("Success", (await service.EvaluateAsync(caller, asset.Id, new(version.Id, Fixture.Input("1")))).Status);
    }

    [Fact]
    public async Task Deleted_completed_receipt_cannot_reconstruct_a_detailed_test_result()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        var request = new TestDecisionRequest("run", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision);
        var run = await service.TestAsync(caller, asset.Id, request);
        await service.DeleteRunAsync(caller, asset.Id, run.Id, "delete");
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.TestAsync(caller, asset.Id, request));
        Assert.Equal("not-found", error.Code); Assert.Empty(await fixture.Reader.ListRunsAsync(caller.TenantId, asset.Id));
    }

    [Fact]
    public async Task Explicit_opt_in_retains_detailed_history_with_policy_checks()
    {
        await using var fixture = await Fixture.Create(true); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Approval", Fixture.Xml, IsSensitive: true));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        var run = await service.TestAsync(caller, asset.Id, new("run", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        var retained = await service.GetRunAsync(caller, asset.Id, run.Id);
        Assert.True(retained.HasDetailedPayload); Assert.Equal("1", retained.CaseInputs![item.Id]["amount"].Value);
        fixture.Policy.Denied.Add("decisions:sensitive-values");
        var hidden = await service.GetRunAsync(caller, asset.Id, run.Id);
        Assert.False(hidden.HasDetailedPayload); Assert.Null(hidden.CaseInputs); Assert.Null(hidden.Comparisons[0].Expected); Assert.Empty(hidden.Comparisons[0].MatchedRules);
    }

    [Fact]
    public async Task Default_design_authorization_is_unavailable_not_allow_all()
    {
        await using var fixture = await Fixture.Create();
        var services = new ServiceCollection(); services.AddDecisionsDesign();
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IDecisionAccessPolicy>();
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await policy.IsAllowedAsync(fixture.Caller, "decisions:read"));
        Assert.Equal("unavailable", error.Code);
    }

    [Fact]
    public async Task First_hit_policy_comparisons_retain_actual_matched_selected_and_cell_trace()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var xml = Fixture.Xml.Replace("hitPolicy=\"UNIQUE\"", "hitPolicy=\"FIRST\"").Replace("&gt; 1000", "-");
        var asset = await service.SaveAsync(caller, null, new("create", "First", xml));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "both match", Fixture.Input("950"), "Success", Fixture.Expected("Automatic")));
        var run = await service.TestAsync(caller, asset.Id, new("run", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        Assert.True(run.Passed);
        Assert.Equal(new[] { "automatic", "manager" }, run.Comparisons[0].MatchedRules);
        Assert.Equal(new[] { "automatic" }, run.Comparisons[0].SelectedRules);
        Assert.All(run.Comparisons[0].Trace, row => Assert.Equal("true", row.Cells[0].Truth));
        Assert.False(run.Comparisons[0].Trace[1].Selected);
    }

    [Fact]
    public async Task Exact_numeric_expectations_survive_storage_and_ignore_insignificant_decimal_zeroes()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var xml = Fixture.Xml.Replace("typeRef=\"string\"", "typeRef=\"number\"").Replace("\"Automatic\"", "9007199254740993").Replace("\"Manager\"", "1234567890123456789012345678901234");
        var asset = await service.SaveAsync(caller, null, new("create", "Exact", xml));
        var expected = JsonSerializer.SerializeToElement(new DecisionValue("number", "9007199254740993.0"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "exact", Fixture.Input("950"), "Success", expected));
        var stored = await fixture.Reader.FindCaseAsync(caller.TenantId, asset.Id, item.Id);
        Assert.Equal("9007199254740993.0", stored!.ExpectedResult.GetProperty("value").GetString());
        var run = await service.TestAsync(caller, asset.Id, new("run", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        Assert.True(run.Passed); Assert.Equal("9007199254740993", run.Comparisons[0].Actual!.Value.GetProperty("value").GetString());
    }

    [Fact]
    public void Competing_authorization_replacements_are_diagnosed()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDecisionAccessPolicy, MutablePolicy>(); services.AddScoped<IDecisionAccessPolicy, MutablePolicy>();
        Assert.Throws<InvalidOperationException>(() => services.AddDecisionsDesign());
    }

    [Fact]
    public async Task Unsupported_original_source_bytes_survive_metadata_only_edit()
    {
        await using var fixture = await Fixture.Create(); var service = fixture.Service; var caller = fixture.Caller;
        var xml = Fixture.Xml.Replace("<decision id=", "<!-- preserve foreign presentation -->\n<decision id=");
        var asset = await service.SaveAsync(caller, null, new("create", "Original", xml));
        _ = await service.SaveAsync(caller, asset.Id, new("rename", "Renamed", xml, ExpectedRevision: asset.Revision));
        Assert.Equal(xml, await service.ExportAsync(caller, asset.Id, null));
    }

    [Fact]
    public async Task Raw_history_requires_sensitive_grant_even_when_case_values_are_public()
    {
        await using var fixture = await Fixture.Create(true); var service = fixture.Service; var caller = fixture.Caller;
        var asset = await service.SaveAsync(caller, null, new("create", "Public", Fixture.Xml));
        var item = await service.SaveCaseAsync(caller, asset.Id, null, new("case", "one", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        fixture.Policy.Denied.Add("decisions:sensitive-values");
        var transient = await service.TestAsync(caller, asset.Id, new("unretained", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        Assert.True(transient.HasDetailedPayload);
        Assert.False((await fixture.Reader.FindRunAsync(caller.TenantId, asset.Id, transient.Id))!.HasDetailedPayload);
        fixture.Policy.Denied.Clear();
        var retained = await service.TestAsync(caller, asset.Id, new("retained", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        Assert.True((await fixture.Reader.FindRunAsync(caller.TenantId, asset.Id, retained.Id))!.HasDetailedPayload);
        fixture.Policy.Denied.Add("decisions:sensitive-values");
        var redacted = await service.GetRunAsync(caller, asset.Id, retained.Id);
        Assert.False(redacted.HasDetailedPayload); Assert.Null(redacted.CaseInputs); Assert.Null(redacted.Comparisons[0].Actual);
        Assert.NotEmpty(redacted.Comparisons[0].Trace);
    }

    [Fact]
    public async Task Persistence_rejects_source_identity_mismatch_without_committing_an_operation()
    {
        await using var fixture = await Fixture.Create();
        var writer = fixture.Writer;
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await writer.SaveAssetAsync(new("tenant-a", "user-a", "mismatch"),
            new(null, null, "Invalid identity", null, Fixture.Xml, new string('0', 64), false)));
        Assert.Equal("invalid", error.Code); Assert.Empty(await fixture.Reader.ListAssetsAsync("tenant-a"));
    }

    [Fact]
    public async Task Optional_host_without_a_store_composes_but_returns_explicit_unavailability()
    {
        var services = new ServiceCollection(); var engine = new DecisionEngine();
        services.AddSingleton<IDecisionCompiler>(engine); services.AddSingleton<IDecisionEvaluator>(engine);
        services.AddSingleton<IDecisionAccessPolicy>(new MutablePolicy()); services.AddDecisionsDesign();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<DecisionDesignService>();
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await service.ListAsync(new("tenant-a", "user-a")));
        Assert.Equal("unavailable", error.Code);
    }

    [Fact]
    public async Task Future_schema_stamp_is_refused_before_json_interpretation_or_mutation()
    {
        await using var fixture = await Fixture.Create();
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Approval", Fixture.Xml));
        await using (var database = fixture.Factory.CreateDbContext())
        {
            var row = await database.Set<DecisionRecord>().SingleAsync(x => x.Kind == "asset");
            row.SchemaVersion = "99.0.0"; row.Json = "future incompatible content";
            await database.SaveChangesAsync();
        }
        var read = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Reader.FindAssetAsync(fixture.Caller.TenantId, asset.Id));
        Assert.Equal("unavailable", read.Code);
        Assert.IsType<Elsa.Persistence.EntityFramework.EfSchemaVersionSkewException>(read.InnerException);
        var write = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Writer.SaveAssetAsync(
            new(fixture.Caller.TenantId, fixture.Caller.SubjectId, "old-host-write"),
            new(asset.Id, asset.Revision, "Overwrite", null, Fixture.Xml, asset.SourceHash, false)));
        Assert.Equal("unavailable", write.Code);
        await using var verification = fixture.Factory.CreateDbContext();
        var retained = await verification.Set<DecisionRecord>().SingleAsync(x => x.Kind == "asset");
        Assert.Equal("99.0.0", retained.SchemaVersion); Assert.Equal("future incompatible content", retained.Json);
        Assert.Null(await fixture.Reader.FindOperationAsync(new(fixture.Caller.TenantId, fixture.Caller.SubjectId, "old-host-write")));
    }

    [Fact]
    public async Task Consumer_owned_context_mapping_preserves_unrelated_model()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Factory.CreateDbContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(Unrelated)));
        Assert.Null(context.Model.FindEntityType(typeof(Unrelated))!.FindProperty(nameof(Unrelated.Text))!.FindAnnotation("Relational:Collation"));
    }
}

internal sealed class Fixture : IAsyncDisposable
{
    private readonly string directory;
    private readonly ServiceProvider provider;
    public TestFactory Factory { get; }
    public MutablePolicy Policy { get; } = new();
    public DecisionCaller Caller { get; } = new("tenant-a", "user-a");
    public IDecisionReader Reader => provider.GetRequiredService<IDecisionReader>();
    public IDecisionWriter Writer => provider.GetRequiredService<IDecisionWriter>();
    public DecisionDesignService Service { get; }
    private Fixture(string directory, TestFactory factory, ServiceProvider provider, bool history)
    {
        this.directory = directory; Factory = factory; this.provider = provider;
        var engine = new DecisionEngine();
        Service = new(Reader, provider.GetRequiredService<IDecisionWriter>(), engine, engine, Policy, new() { RetainDetailedTestHistory = history });
    }
    public static async Task<Fixture> Create(bool history = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-decisions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var factory = new TestFactory(Path.Combine(directory, "decisions.db"));
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var services = new ServiceCollection(); services.AddSingleton<IDbContextFactory<TestContext>>(factory); services.AddDecisionsEntityFrameworkCore<TestContext>();
        return new(directory, factory, services.BuildServiceProvider(), history);
    }
    public async ValueTask DisposeAsync() { await provider.DisposeAsync(); Directory.Delete(directory, true); }
    public static IReadOnlyDictionary<string, DecisionValue> Input(string value) => new Dictionary<string, DecisionValue> { ["amount"] = new("number", value) };
    public static JsonElement Expected(string value) => JsonSerializer.SerializeToElement(new DecisionValue("string", value), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public const string Xml = """
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Approval" namespace="urn:elsa:decisions:test">
          <decision id="approval" name="Approval"><variable id="decision-variable" name="Approval" typeRef="string" />
            <decisionTable id="table" hitPolicy="UNIQUE">
              <input id="amount-column"><inputExpression id="amount-expression" typeRef="number"><text>amount</text></inputExpression></input>
              <output id="approval-column" name="approval" typeRef="string" />
              <rule id="automatic"><inputEntry id="automatic-input"><text>&lt;= 1000</text></inputEntry><outputEntry id="automatic-output"><text>"Automatic"</text></outputEntry></rule>
              <rule id="manager"><inputEntry id="manager-input"><text>&gt; 1000</text></inputEntry><outputEntry id="manager-output"><text>"Manager"</text></outputEntry></rule>
            </decisionTable>
          </decision>
        </definitions>
        """;
}
internal sealed class MutablePolicy : IDecisionAccessPolicy
{
    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
    public ValueTask<bool> IsAllowedAsync(DecisionCaller caller, string permission, object? resource = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(!Denied.Contains(permission));
}
internal sealed class TestFactory(string file) : IDbContextFactory<TestContext>
{
    public TestContext CreateDbContext() => new(new DbContextOptionsBuilder<TestContext>().UseSqlite("Data Source=" + file + ";Pooling=False;Default Timeout=5").Options);
}
internal sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) { builder.Entity<Unrelated>().HasKey(x => x.Id); builder.AddDecisionsModel(Database.ProviderName!); }
}
internal sealed class Unrelated { public int Id { get; set; } public string Text { get; set; } = ""; }
