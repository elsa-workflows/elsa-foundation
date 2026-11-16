using System.Text.Json;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    [Theory]
    [InlineData("BudgetExceeded")]
    [InlineData("Cancelled")]
    public async Task Saved_cases_cannot_expect_whole_request_abort_outcomes(string status)
    {
        await using var fixture = await Fixture.Create();
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Bounded cases", Fixture.Xml));
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null,
            new("rejected-case", status, Fixture.Input("1"), status, JsonSerializer.SerializeToElement<object?>(null))));
        Assert.Equal("invalid", error.Code); Assert.Empty(await fixture.Reader.ListCasesAsync(fixture.Caller.TenantId, asset.Id));
        Assert.Null(await fixture.Reader.FindOperationAsync(new(fixture.Caller.TenantId, fixture.Caller.SubjectId, "rejected-case")));
    }

    [Theory]
    [InlineData("HitPolicyViolation")]
    [InlineData("NoMatch")]
    public async Task Saved_case_expectations_match_error_raw_null_and_no_match_tagged_null(string status)
    {
        await using var fixture = await Fixture.Create();
        var xml = status == "HitPolicyViolation" ? Fixture.Xml.Replace("&gt; 1000", "-") :
            Fixture.Xml.Replace("&lt;= 1000", "&lt; 0").Replace("&gt; 1000", "&lt; 0");
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Status cases", xml));
        var expected = status == "NoMatch" ? JsonSerializer.SerializeToElement(new DecisionValue("null", null), new JsonSerializerOptions(JsonSerializerDefaults.Web)) : JsonSerializer.SerializeToElement<object?>(null);
        var item = await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null, new("case", status, Fixture.Input("1"), status, expected));
        var run = await fixture.Service.TestAsync(fixture.Caller, asset.Id, new("test", asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision));
        Assert.True(run.Passed); Assert.Equal(status, run.Comparisons[0].Status); Assert.Null(run.Comparisons[0].FailurePath);
        foreach (var invalidStatus in new[] { "Success", "NoMatch" })
        {
            var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null,
                new("invalid-" + invalidStatus, "Invalid raw null", Fixture.Input("1"), invalidStatus, JsonSerializer.SerializeToElement<object?>(null))));
            Assert.Equal("invalid", error.Code);
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("other")]
    public async Task Multi_output_kind_names_are_records_in_saved_cases_and_comparisons(string secondName)
    {
        await using var fixture = await Fixture.Create();
        var xml = $"""
            <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Named outputs" namespace="urn:test">
              <decision id="named"><decisionTable id="table"><input id="input"><inputExpression id="expression" typeRef="number"><text>amount</text></inputExpression></input>
                <output id="kind-column" name="kind" typeRef="number"/><output id="second-column" name="{secondName}" typeRef="string"/>
                <rule id="rule"><inputEntry id="test"><text>-</text></inputEntry><outputEntry id="kind-result"><text>1</text></outputEntry><outputEntry id="second-result"><text>"Automatic"</text></outputEntry></rule>
              </decisionTable></decision>
            </definitions>
            """;
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Named outputs", xml));
        JsonElement Expected(string number) => JsonSerializer.SerializeToElement(new Dictionary<string, DecisionValue>
            { ["kind"] = new("number", number), [secondName] = new("string", "Automatic") }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var passed = await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null, new("case-pass", "Pass", Fixture.Input("1"), "Success", Expected("1.00")));
        var failed = await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null, new("case-fail", "Fail", Fixture.Input("1"), "Success", Expected("2")));
        var run = await fixture.Service.TestAsync(fixture.Caller, asset.Id, new("test", asset.SourceHash, [new(passed.Id, passed.Revision), new(failed.Id, failed.Revision)], asset.Revision));
        Assert.False(run.Passed); Assert.Null(run.Comparisons[0].FailurePath); Assert.Equal("$.kind", run.Comparisons[1].FailurePath);
    }

    [Fact]
    public async Task Completed_history_cap_preserves_reads_replay_and_explicit_delete_then_retry()
    {
        await using var fixture = await Fixture.Create();
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "History cap", Fixture.Xml));
        var other = await fixture.Service.SaveAsync(fixture.Caller, null, new("create-other", "Other history", Fixture.Xml));
        var item = await fixture.Service.SaveCaseAsync(fixture.Caller, asset.Id, null, new("case", "One", Fixture.Input("1"), "Success", Fixture.Expected("Automatic")));
        var now = DateTimeOffset.UtcNow; var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using (var database = fixture.Factory.CreateDbContext())
        {
            var tenantKey = (await database.Set<DecisionRecord>().FirstAsync(row => row.Kind == "asset")).TenantKey;
            for (var index = 0; index < 1000; index++)
            {
                var owner = index == 999 ? other.Id : asset.Id; var id = "seed-run-" + index.ToString("D4");
                var receipt = new DecisionTestReceipt(id, owner, fixture.Caller.SubjectId, "seed", asset.Revision, asset.SourceHash, [], [], true, false, false, now, now);
                database.Add(new DecisionRecord { TenantKey = tenantKey, Kind = "run", Id = id, OwnerId = owner, Revision = "1", Json = JsonSerializer.Serialize(receipt, json) });
            }
            await database.SaveChangesAsync();
        }
        async Task<(TestDecisionRequest Request, DecisionTestReceipt? Receipt, DecisionDesignException? Error)> Attempt(string operation)
        {
            var candidate = new TestDecisionRequest(operation, asset.SourceHash, [new(item.Id, item.Revision)], asset.Revision);
            try { return (candidate, await fixture.Service.TestAsync(fixture.Caller, asset.Id, candidate), null); }
            catch (DecisionDesignException error) { return (candidate, null, error); }
        }
        var attempts = await Task.WhenAll(Task.Run(() => Attempt("at-cap")), Task.Run(() => Attempt("competing-last-slot")));
        var winner = Assert.Single(attempts, attempt => attempt.Receipt is not null);
        Assert.Equal("limit", Assert.Single(attempts, attempt => attempt.Error is not null).Error!.Code);
        var request = winner.Request; var last = winner.Receipt!;
        Assert.Equal(1000, (await fixture.Service.RunsAsync(fixture.Caller, asset.Id)).Count);
        Assert.Equal(last.Id, (await fixture.Service.TestAsync(fixture.Caller, asset.Id, request)).Id);
        var retry = request with { OperationId = "over-cap" };
        var refusal = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Service.TestAsync(fixture.Caller, asset.Id, retry));
        Assert.Equal("limit", refusal.Code); Assert.Contains("Delete a retained run", refusal.Message);
        Assert.Null(await fixture.Reader.FindOperationAsync(new(fixture.Caller.TenantId, fixture.Caller.SubjectId, retry.OperationId)));
        Assert.Equal(1000, (await fixture.Reader.ListRunsAsync(fixture.Caller.TenantId, asset.Id)).Count);
        await fixture.Service.DeleteRunAsync(fixture.Caller, asset.Id, "seed-run-0000", "delete-retained");
        var afterDelete = await fixture.Service.TestAsync(fixture.Caller, asset.Id, retry);
        Assert.True(afterDelete.Passed); Assert.Equal(1000, (await fixture.Service.RunsAsync(fixture.Caller, asset.Id)).Count);
        Assert.Equal(afterDelete.Id, (await fixture.Service.TestAsync(fixture.Caller, asset.Id, retry)).Id);
        Assert.Null(await fixture.Reader.FindRunAsync(fixture.Caller.TenantId, asset.Id, "seed-run-0000"));
        Assert.Single(await fixture.Reader.ListRunsAsync(fixture.Caller.TenantId, other.Id));
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("reader")]
    [InlineData("writer")]
    public void Early_competing_design_replacements_are_rejected(string contract)
    {
        var services = new ServiceCollection(); AddReplacement(services, contract); AddReplacement(services, contract);
        Assert.Throws<InvalidOperationException>(() => services.AddDecisionsDesign());
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("writer")]
    public void Early_competing_persistence_replacements_are_rejected(string contract)
    {
        var services = new ServiceCollection(); AddReplacement(services, contract); AddReplacement(services, contract);
        Assert.Throws<InvalidOperationException>(() => services.AddDecisionsEntityFrameworkCore<TestContext>());
    }

    [Theory]
    [InlineData("policy", false)]
    [InlineData("policy", true)]
    [InlineData("reader", false)]
    [InlineData("writer", false)]
    public async Task Design_startup_rechecks_late_conflicts_and_required_policy_removal(string contract, bool remove)
    {
        var services = new ServiceCollection(); services.AddDecisionsDesign();
        if (remove) services.RemoveAll<IDecisionAccessPolicy>();
        else { AddReplacement(services, contract); if (contract != "policy") AddReplacement(services, contract); }
        using var provider = services.BuildServiceProvider();
        var guard = Assert.Single(provider.GetServices<IShellInitializer>(), task => task is DecisionDesignCompositionCheck);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.InitializeAsync());
        Assert.Contains(contract == "policy" ? nameof(IDecisionAccessPolicy) : contract == "reader" ? nameof(IDecisionReader) : nameof(IDecisionWriter), error.Message);
    }

    [Theory]
    [InlineData("reader", false)]
    [InlineData("reader", true)]
    [InlineData("writer", false)]
    [InlineData("writer", true)]
    public async Task Persistence_startup_rechecks_late_conflicts_and_removal(string contract, bool remove)
    {
        var services = new ServiceCollection(); services.AddDecisionsEntityFrameworkCore<TestContext>();
        if (remove) services.RemoveAll(contract == "reader" ? typeof(IDecisionReader) : typeof(IDecisionWriter));
        else AddReplacement(services, contract);
        using var provider = services.BuildServiceProvider();
        var guard = Assert.Single(provider.GetServices<IShellInitializer>(), task => task is DecisionPersistenceCompositionCheck);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.InitializeAsync());
        Assert.Contains(contract == "reader" ? nameof(IDecisionReader) : nameof(IDecisionWriter), error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exactly_one_host_replacement_before_or_after_defaults_survives_startup(bool replaceLater)
    {
        await using var fixture = await Fixture.Create(); var policy = new MutablePolicy(); var services = new ServiceCollection();
        void Replace()
        {
            services.Replace(ServiceDescriptor.Singleton<IDecisionAccessPolicy>(policy));
            services.Replace(ServiceDescriptor.Singleton<IDecisionReader>(fixture.Reader));
            services.Replace(ServiceDescriptor.Singleton<IDecisionWriter>(fixture.Writer));
        }
        if (!replaceLater) Replace();
        services.AddDecisionsDesign(); services.AddDecisionsEntityFrameworkCore<TestContext>();
        if (replaceLater) Replace();
        services.AddDecisionsDesign(); services.AddDecisionsEntityFrameworkCore<TestContext>();
        using var provider = services.BuildServiceProvider();
        var guards = provider.GetServices<IShellInitializer>().ToArray(); Assert.Equal(2, guards.Length);
        foreach (var guard in guards) await guard.InitializeAsync();
        Assert.Same(policy, provider.GetRequiredService<IDecisionAccessPolicy>());
        Assert.Same(fixture.Reader, provider.GetRequiredService<IDecisionReader>());
        Assert.Same(fixture.Writer, provider.GetRequiredService<IDecisionWriter>());
    }

    private static void AddReplacement(IServiceCollection services, string contract)
    {
        switch (contract)
        {
            case "policy": services.AddScoped<IDecisionAccessPolicy, MutablePolicy>(); break;
            case "reader": services.AddScoped<IDecisionReader>(_ => throw new InvalidOperationException("Startup must count descriptors without resolving a reader.")); break;
            case "writer": services.AddScoped<IDecisionWriter>(_ => throw new InvalidOperationException("Startup must count descriptors without resolving a writer.")); break;
        }
    }

    [Fact]
    public async Task One_leading_xml_bom_roundtrips_exact_source_hash_while_double_bom_is_refused()
    {
        await using var fixture = await Fixture.Create();
        var xml = "\uFEFF" + Fixture.Xml;
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml)));
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("bom", "BOM import", xml));
        Assert.Equal(xml, asset.Xml); Assert.Equal(expectedHash, asset.SourceHash);
        var version = await fixture.Service.PublishAsync(fixture.Caller, asset.Id, new("publish", asset.Revision));
        Assert.Equal(expectedHash, version.SourceHash);
        Assert.Equal(xml, await fixture.Service.ExportAsync(fixture.Caller, asset.Id, version.Id));
        var result = await fixture.Service.EvaluateAsync(fixture.Caller, asset.Id, new(version.Id, Fixture.Input("950")));
        Assert.Equal("Success", result.Status); Assert.Equal("Automatic", result.Result.GetProperty("value").GetString());
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Service.SaveAsync(fixture.Caller, null, new("double-bom", "Malformed import", "\uFEFF" + xml)));
        Assert.Equal("invalid", error.Code); Assert.Single(await fixture.Reader.ListAssetsAsync(fixture.Caller.TenantId));
    }

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
        foreach (var guard in provider.GetServices<IShellInitializer>()) await guard.InitializeAsync();
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
    public async Task Stored_payload_identity_cannot_alias_another_asset()
    {
        await using var fixture = await Fixture.Create();
        var asset = await fixture.Service.SaveAsync(fixture.Caller, null, new("create", "Approval", Fixture.Xml));
        await using (var database = fixture.Factory.CreateDbContext())
        {
            var row = await database.Set<DecisionRecord>().SingleAsync(x => x.Kind == "asset");
            row.Json = row.Json.Replace(asset.Id, "decision-different"); await database.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<DecisionDesignException>(async () => await fixture.Reader.FindAssetAsync(fixture.Caller.TenantId, asset.Id));
        Assert.Equal("corrupt", error.Code);
    }

    [Fact]
    public async Task Built_in_factory_discovers_module_and_runs_its_own_additive_migration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-decisions-module-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var binding = Elsa.Persistence.EntityFramework.EfModuleBinding.For(typeof(DecisionsDbContext));
            Assert.Equal(DecisionsEfModule.HistoryTableName, binding.HistoryTableName);
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            new DecisionsEntityFrameworkCoreFeature { Provider = "Sqlite", ConnectionString = "Data Source=" + Path.Combine(directory, "database.db") + ";Pooling=False" }.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DecisionsDbContext>>();
            await using var database = await factory.CreateDbContextAsync();
            Assert.Single(database.Database.GetMigrations());
            await database.Database.MigrateAsync();
            Assert.Single(await database.Database.GetAppliedMigrationsAsync());
            var engine = new DecisionEngine(); var artifact = engine.Compile(Fixture.Xml).Artifact!;
            var writer = scope.ServiceProvider.GetRequiredService<IDecisionWriter>();
            var confirmation = await writer.SaveAssetAsync(new("tenant-a", "user-a", "create"), new(null, null, "Normal module", null, Fixture.Xml, artifact.SourceHash, false));
            var reader = scope.ServiceProvider.GetRequiredService<IDecisionReader>();
            Assert.Equal("Normal module", (await reader.FindAssetAsync("tenant-a", confirmation.Id))!.Name);
            Assert.Equal(DecisionsEfModule.SchemaVersion, (await database.Set<DecisionRecord>().SingleAsync(x => x.Kind == "asset")).SchemaVersion);
        }
        finally { Directory.Delete(directory, true); }
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
