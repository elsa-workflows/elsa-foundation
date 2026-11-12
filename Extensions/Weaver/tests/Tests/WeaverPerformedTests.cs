using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverPerformedTests
{
    [Fact]
    public async Task Mapped_HTTP_and_retained_SSE_models_keep_performed_owner_evidence_without_a_second_execution()
    {
        var tool = new PerformingTool();
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.AddRouting().AddElsaEndpoints();
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        var response = await WeaverApiExecutionOutcomeTests.ExecuteAsync(fixture, proposal, proposal.BaseRevision);

        Assert.Equal(StatusCodes.Status200OK, response.Status);
        var data = response.Body.GetProperty("data");
        Assert.Equal("performed", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("executed").GetBoolean());
        var receipt = data.GetProperty("toolResult").GetProperty("receipt");
        Assert.Equal("noPersistedEffect", receipt.GetProperty("scope").GetString());
        Assert.Equal("revision-1", receipt.GetProperty("before").GetProperty("revision").GetString());
        Assert.Equal("revision-1", receipt.GetProperty("after").GetProperty("revision").GetString());
        Assert.Equal(1, receipt.GetProperty("evidence").GetProperty("data").GetProperty("ran").GetInt32());
        Assert.False(receipt.GetProperty("evidence").GetProperty("data").GetProperty("verdict").GetBoolean());

        var duplicate = await WeaverApiExecutionOutcomeTests.ExecuteAsync(fixture, proposal, proposal.BaseRevision);
        Assert.Equal(StatusCodes.Status200OK, duplicate.Status);
        Assert.Equal(data.GetRawText(), duplicate.Body.GetProperty("data").GetRawText());
        var retained = (await fixture.Proposals.FindAsync(proposal.Id))!;
        using var sseProposal = JsonDocument.Parse(JsonSerializer.Serialize(retained, WeaverSseJsonContext.Default.WeaverActionProposal));
        Assert.Equal("performed", sseProposal.RootElement.GetProperty("status").GetString());
        var retainedExecution = await fixture.ExecuteAsync(proposal);
        using var sseResult = JsonDocument.Parse(JsonSerializer.Serialize(retainedExecution.Value!.ToolResult!, WeaverSseJsonContext.Default.WeaverToolResult));
        Assert.Equal("noPersistedEffect", sseResult.RootElement.GetProperty("receipt").GetProperty("scope").GetString());
        Assert.Equal(1, sseResult.RootElement.GetProperty("receipt").GetProperty("evidence").GetProperty("data").GetProperty("ran").GetInt32());
        Assert.Equal(1, tool.RanCalls);
        Assert.Equal("initial", tool.NativeValue);
        Assert.Equal("revision-1", tool.NativeRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Performed_retains_owner_ran_evidence_and_releases_the_claim_without_changing_native_state(bool custom)
    {
        var tool = new PerformingTool();
        using var fixture = await CreateAsync(tool, custom);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.True(execution.Value?.Executed);
        Assert.Equal("performed", execution.Value?.Status);
        Assert.Equal(WeaverEffectScope.NoPersistedEffect, execution.Value?.ToolResult?.Receipt?.Scope);
        Assert.False(execution.Value!.ToolResult!.Receipt!.Evidence.Data.GetProperty("verdict").GetBoolean());
        Assert.Equal(1, tool.RanCalls);
        Assert.Equal("initial", tool.NativeValue);
        Assert.Equal("revision-1", tool.NativeRevision);
        Assert.Equal(WeaverActionProposalStatus.Performed, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.True(InMemoryWeaverProposalService.Settled((await fixture.Proposals.FindAsync(proposal.Id))!));
        Assert.True((await fixture.ApproveAsync(proposal)).Succeeded);
        Assert.False((await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Already ran.")).Succeeded);
        Assert.Same(execution.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, tool.RanCalls);
        var audit = Assert.Single(await fixture.Audit.ListAsync(fixture.Session.Id), row => row.Kind == WeaverAuditEventKind.ProposalExecuted);
        Assert.Equal("performed", audit.Metadata["outcome"]);
        Assert.Equal(nameof(WeaverEffectScope.NoPersistedEffect), audit.Metadata["scope"]);
        Assert.Contains("without a persisted resource change", audit.Summary);
        var policy = fixture.Services.GetRequiredService<IWeaverSessionPolicyService>();
        var current = await policy.GetAsync(fixture.Session.Id, ProposalFixture.Actor, fixture.Session.TenantId);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, (await policy.ChangeAutonomyAsync(new(fixture.Session.Id, ProposalFixture.Actor,
            fixture.Session.TenantId, current.State!.Policy.AutonomyMode, current.State.Revision))).Outcome);
    }

    [Fact]
    public async Task The_direct_default_executor_also_validates_and_preserves_performed_evidence()
    {
        var tool = new PerformingTool();
        using var fixture = await CreateAsync(tool);
        var proposal = (await fixture.ApproveAsync(await fixture.ProposeAsync())).Value!;
        var result = await fixture.Services.GetRequiredService<IWeaverActionProposalExecutor>().ExecuteAsync(proposal);
        Assert.True(result.Value?.Executed);
        Assert.Equal("performed", result.Value?.Status);
        Assert.Equal(1, tool.RanCalls);
        Assert.Equal("revision-1", tool.NativeRevision);
    }

    public static IEnumerable<object[]> InvalidReceipts() => new[]
    {
        "declaration", "client-scope", "after-revision", "after-id", "after-type", "after-module", "before",
        "invocation", "call", "tool", "payload", "evidence", "undefined-evidence", "unsuccessful", "null-after", "null-result", "error-with-receipt"
    }.SelectMany(fault => new[] { new object[] { fault, false }, new object[] { fault, true } });

    public static IEnumerable<object[]> DirectInvalidReceipts() => InvalidReceipts().Where(row => !(bool)row[1])
        .Select(row => new[] { row[0] }).Concat(new[] { new object[] { "throw" }, new object[] { "cancel" } });

    [Theory]
    [MemberData(nameof(DirectInvalidReceipts))]
    public async Task The_direct_default_boundary_rejects_wrong_success_or_interruption_after_an_action_ran(string fault)
    {
        var tool = new PerformingTool { DeclaresPerformed = fault != "declaration", Fault = fault };
        using var fixture = await CreateAsync(tool);
        var proposal = (await fixture.ApproveAsync(await fixture.ProposeAsync())).Value!;
        var execution = await fixture.Services.GetRequiredService<IWeaverActionProposalExecutor>().ExecuteAsync(proposal);
        Assert.Equal("uncertain", execution.Value?.Status);
        Assert.Null(execution.Error);
        Assert.DoesNotContain("No effect occurred", execution.Value!.Summary);
        Assert.Equal(1, tool.RanCalls);
    }

    [Fact]
    public async Task A_recorded_performed_operation_rehydrates_as_terminal_without_a_new_busy_claim_or_replay()
    {
        var records = new InMemoryWeaverSessionRecordStore();
        var tool = new PerformingTool();
        WeaverActionProposal proposal;
        WeaverSession session;
        using (var first = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.SelectWeaverSessionRecordStore(_ => records, typeof(WeaverEffectsFeature));
        }))
        {
            proposal = await first.ProposeAsync();
            await first.ApproveAsync(proposal);
            Assert.Equal("performed", (await first.ExecuteAsync(proposal)).Value?.Status);
            session = first.Session;
            await records.ReleaseOwnershipAsync(session.Id, (await records.LoadAsync(session.Id))!.Owner!);
        }
        using var root = WeaverTestServices.Create(services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.SelectWeaverSessionRecordStore(_ => records, typeof(WeaverEffectsFeature));
            services.AddScoped<IWeaverTool>(_ => tool);
        });
        using var scope = root.CreateScope();
        var proposals = scope.ServiceProvider.GetRequiredService<IWeaverProposalService>();
        var restored = (await proposals.FindAsync(proposal.Id))!;
        Assert.Equal(WeaverActionProposalStatus.Performed, restored.Status);
        var retained = await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.Equal("performed", retained.Value?.Status);
        Assert.Equal(WeaverEffectScope.NoPersistedEffect, retained.Value?.ToolResult?.Receipt?.Scope);
        Assert.Equal(1, tool.RanCalls);
        var policy = scope.ServiceProvider.GetRequiredService<IWeaverSessionPolicyService>();
        var current = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, (await policy.ChangeAutonomyAsync(new(session.Id, session.ActorId,
            session.TenantId, current.State!.Policy.AutonomyMode, current.State.Revision))).Outcome);
    }

    [Theory]
    [MemberData(nameof(InvalidReceipts))]
    public async Task Wrong_success_after_dispatch_is_uncertain_once_at_both_boundaries(string fault, bool custom)
    {
        var tool = new PerformingTool { DeclaresPerformed = fault != "declaration", Fault = fault };
        using var fixture = await CreateAsync(tool, custom);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", execution.Value?.Status);
        Assert.False(execution.Value?.Executed);
        Assert.Null(execution.Error);
        Assert.DoesNotContain("No effect occurred", execution.Value!.Summary);
        Assert.Equal(WeaverActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Same(execution.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, tool.RanCalls);
        Assert.Equal("initial", tool.NativeValue);
        Assert.Equal("revision-1", tool.NativeRevision);
    }

    [Theory]
    [InlineData("executed")]
    [InlineData("unknown")]
    [InlineData("wrong-resource")]
    [InlineData("not-executed")]
    public async Task The_custom_boundary_independently_requires_outer_status_and_target_to_agree(string fault)
    {
        var tool = new PerformingTool();
        using var fixture = await CreateAsync(tool, true, fault);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", execution.Value?.Status);
        Assert.Same(execution.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, tool.RanCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_genuine_in_flight_receipt_uses_the_admitted_descriptor_without_a_post_effect_lookup(bool custom)
    {
        var tool = new PerformingTool { HideDescriptorAfterRunning = true };
        using var fixture = await CreateAsync(tool, custom);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var execution = await fixture.ExecuteAsync(proposal);
        Assert.Equal("performed", execution.Value?.Status);
        Assert.Same(execution.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, tool.RanCalls);
    }

    [Fact]
    public async Task Custom_executor_handoff_requires_the_actual_current_descriptors_effects_admission()
    {
        var tool = new PerformingTool { DeclaresPerformed = false };
        var executor = new PerformingExecutor(tool);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IWeaverActionProposalExecutor>(_ => executor)));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        tool.DeclaresPerformed = true;
        Assert.Equal("weaver.effects.not_enabled", (await fixture.ExecuteAsync(proposal)).Error?.Code);
        Assert.Equal(0, executor.Calls);
        Assert.Equal(0, tool.RanCalls);
        Assert.Contains("No effect occurred", (await fixture.Proposals.FindResultAsync(proposal.Id))!.Content);
    }

    private static Task<ProposalFixture> CreateAsync(PerformingTool tool, bool custom = false, string? outerFault = null)
        => ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            if (custom) services.Replace(ServiceDescriptor.Scoped<IWeaverActionProposalExecutor>(_ => new PerformingExecutor(tool, outerFault)));
        });

    private sealed class PerformingExecutor(PerformingTool tool, string? fault = null) : IWeaverActionProposalExecutor
    {
        internal int Calls { get; private set; }
        public async Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
        {
            Calls++;
            var observed = await tool.ExecuteAsync(proposal.PreparedInvocation!, cancellationToken);
            if (observed.Value is null) return WeaverResult<WeaverProposalExecutionResult>.Success(null!);
            var result = DefaultWeaverActionProposalExecutor.Result(proposal, fault != "not-executed", observed.Value!.Content,
                fault is "executed" or "unknown" ? fault : "performed") with { ToolResult = observed.Value };
            if (fault == "wrong-resource") result = result with { ResourceId = "another-resource" };
            return new(result, observed.Error);
        }
        public Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal,
            WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PerformingTool : IWeaverTool
    {
        private const string Name = "test_perform";
        internal bool DeclaresPerformed { get; set; } = true;
        internal bool HideDescriptorAfterRunning { get; init; }
        internal string? Fault { get; init; }
        internal int RanCalls { get; private set; }
        internal string NativeValue => "initial";
        internal string NativeRevision => "revision-1";
        public WeaverToolDescriptor Descriptor => HideDescriptorAfterRunning && RanCalls > 0
            ? throw new InvalidOperationException("No descriptor observation is allowed after the action ran.")
            : new(Name, "Test resource", "Runs an owner test without changing native resource state.", WeaverToolMutability.Mutating,
                WeaverRisk.ReviewRequired, "{}", [], [])
            {
                ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId,
                CanPerformWithoutPersistence = DeclaresPerformed, ResultType = "test.ran", ResultSchemaVersion = "1"
            };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(new(new("test.module", "test.resource", "resource-1", NativeRevision),
                JsonSerializer.SerializeToElement(invocation.Arguments), WeaverRisk.ReviewRequired,
                new("test.preview", "1", JsonSerializer.SerializeToElement(new { NativeValue, NativeRevision })), false)));
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RanCalls++;
            if (Fault == "throw") throw new InvalidOperationException("An action ran before interruption.");
            if (Fault == "cancel") throw new OperationCanceledException("An action ran before cancellation.");
            if (Fault == "null-result") return Task.FromResult(WeaverResult<WeaverToolResult>.Success(null!));
            var evidence = new WeaverPayload("test.ran", "1", JsonSerializer.SerializeToElement(new
                { ran = RanCalls, verdict = NativeValue == "expected", nativeRevision = NativeRevision }));
            var receipt = new WeaverEffectReceipt(invocation.InvocationId, invocation.Target!, invocation.Target!, WeaverEffectScope.NoPersistedEffect, evidence);
            var result = new WeaverToolResult(invocation.ToolCallId, Name, true, "The test ran; its verdict was negative.", evidence) { Receipt = receipt };
            result = Fault switch
            {
                "client-scope" => result with { Receipt = receipt with { Scope = WeaverEffectScope.ClientWorkingState } },
                "after-revision" => result with { Receipt = receipt with { After = receipt.After with { Revision = "revision-2" } } },
                "after-id" => result with { Receipt = receipt with { After = receipt.After with { ResourceId = "another" } } },
                "after-type" => result with { Receipt = receipt with { After = receipt.After with { ResourceType = "other.type" } } },
                "after-module" => result with { Receipt = receipt with { After = receipt.After with { ModuleId = "other.module" } } },
                "before" => result with { Receipt = receipt with { Before = receipt.Before with { Revision = "another" } } },
                "invocation" => result with { Receipt = receipt with { InvocationId = "another" } },
                "call" => result with { ToolCallId = "another" },
                "tool" => result with { ToolName = "another" },
                "payload" => result with { Payload = null },
                "evidence" => result with { Receipt = receipt with { Evidence = null! } },
                "undefined-evidence" => result with { Receipt = receipt with { Evidence = evidence with { Data = default } } },
                "unsuccessful" => result with { Succeeded = false },
                "null-after" => result with { Receipt = receipt with { After = null! } },
                _ => result
            };
            return Task.FromResult(Fault == "error-with-receipt"
                ? new WeaverResult<WeaverToolResult>(result, new("test.partial", "The owner could not verify its final outcome.", 409))
                : WeaverResult<WeaverToolResult>.Success(result));
        }
    }
}
