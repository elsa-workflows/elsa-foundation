using System.Text.Json;
using System.Runtime.CompilerServices;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentModuleCodeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("native")]
    [InlineData("agent.reserved")]
    [InlineData("Agent.Reserved")]
    [InlineData("native..invalid")]
    [InlineData(".native")]
    [InlineData("native.")]
    [InlineData("native.é")]
    [InlineData("native.invalid value")]
    public void Malformed_declarations_fail_without_echoing_the_rejected_value(string code)
    {
        var error = Assert.Throws<AgentContributionException>(() => AgentModuleCodes.Validate([code]));
        if (code.Length > 0) Assert.DoesNotContain(code, error.Message);
    }

    [Fact]
    public void Declaration_membership_is_ordinal_and_duplicates_are_rejected_without_echo()
    {
        AgentModuleCodes.Validate([]);
        AgentModuleCodes.Validate(null);
        AgentModuleCodes.Validate(["native.ok", "Native.ok"]);
        Assert.Throws<AgentContributionException>(() => AgentModuleCodes.Validate(["native.ok", "native.ok"]));
        var code = "native." + Guid.NewGuid().ToString("N") + new string('x', 128);
        Assert.DoesNotContain(code, Assert.Throws<AgentContributionException>(() => AgentModuleCodes.Validate([code])).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_tool_catalog_admission_validates_both_tool_and_disclosure_owner_code_declarations(bool contextSource)
    {
        var canary = Guid.NewGuid().ToString("N");
        using var root = AgentTestServices.Create(services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            if (contextSource) services.AddScoped<IAgentContextProvider>(_ => new CodeSource(new(CodeSource.Id, "native.ok", "Safe.", false))
                { Codes = ["agent." + canary] });
            else services.AddScoped<IAgentTool>(_ => new CodeTool { Codes = ["agent." + canary] });
        });
        using var scope = root.CreateScope();
        var error = await Assert.ThrowsAsync<AgentContributionException>(() => scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>().ListAsync());
        Assert.DoesNotContain(canary, error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classification_precedes_code_replacement_and_preserves_other_error_metadata(bool unavailable)
    {
        var canary = Guid.NewGuid().ToString("N");
        var original = new AgentError("agent." + canary, "Private cause " + canary, 503) { OwnerUnavailable = unavailable, Limit = "bytes" };
        var normalized = AgentModuleCodes.Error(original, ["native.ok"], ownerOrigin: true);
        Assert.Equal(unavailable ? AgentModuleCodes.UnavailableCode : AgentModuleCodes.UnsupportedCode, normalized.Code);
        Assert.Equal(unavailable, normalized.OwnerUnavailable);
        Assert.Equal(503, normalized.StatusCode);
        Assert.Equal("bytes", normalized.Limit);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(normalized));
    }

    [Fact]
    public void The_fallback_spelling_itself_does_not_authorize_an_unsafe_message()
    {
        var canary = Guid.NewGuid().ToString("N");
        var error = AgentModuleCodes.Error(new(AgentModuleCodes.UnsupportedCode, canary), [], ownerOrigin: true);
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, error.Message);
        var target = new AgentResourceTarget("test.module", "test.resource", "resource", "1");
        var payload = new AgentPayload("test.ran", "1", JsonSerializer.SerializeToElement(new { ran = true }));
        var value = new AgentToolResult("call", "tool", true, canary, payload)
            { Receipt = new("invocation", target, target, AgentEffectScope.NoPersistedEffect, payload) { ModuleCode = AgentModuleCodes.UnsupportedCode } };
        var result = AgentModuleCodes.Result(value, []);
        Assert.True(result.Replaced);
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, result.Value.Content);
        Assert.Equal(value.Receipt.Evidence, result.Value.Receipt!.Evidence);
    }

    [Fact]
    public void Undeclared_codes_and_valid_declared_messages_keep_their_compatibility_and_identity()
    {
        var legacy = new AgentError("legacy native code", "Legacy owner text.", 409) { Limit = "bytes" };
        Assert.Same(legacy, AgentModuleCodes.Error(legacy, null, ownerOrigin: true));
        Assert.Equal(AgentModuleCodes.UnsupportedCode, AgentModuleCodes.Error(legacy, [], ownerOrigin: true).Code);
        var valid = new AgentError("native.ok", "A safe mapped explanation.", 409);
        Assert.Same(valid, AgentModuleCodes.Error(valid, ["native.ok"], ownerOrigin: true));
        Assert.Equal(AgentModuleCodes.UnsupportedCode, AgentModuleCodes.Error(valid, ["Native.ok"], ownerOrigin: true).Code);
    }

    [Theory]
    [InlineData("prepare", false)]
    [InlineData("prepare", true)]
    [InlineData("authority", false)]
    [InlineData("authority", true)]
    [InlineData("execute", false)]
    [InlineData("execute", true)]
    [InlineData("read", false)]
    [InlineData("read", true)]
    public async Task Actual_owner_errors_are_safe_before_forwarding_and_retention(string phase, bool unavailable)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { ErrorPhase = phase == "read" ? "execute" : phase, ReadOnly = phase == "read",
            RawCode = "outside." + canary, RawMessage = canary, Unavailable = unavailable };
        using var fixture = await CreateAsync(tool);
        AgentError? error;
        if (phase == "execute")
        {
            var proposal = await fixture.ProposeAsync();
            await fixture.ApproveAsync(proposal);
            error = (await fixture.ExecuteAsync(proposal)).Error;
            Assert.DoesNotContain(canary, JsonSerializer.Serialize(await fixture.Services.GetRequiredService<IAgentSessionRecordStore>().LoadAsync(fixture.Session.Id)));
        }
        else error = (await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy)).Error;
        Assert.NotNull(error);
        Assert.Equal(unavailable ? AgentModuleCodes.UnavailableCode : phase == "authority" ? "agent.tool.forbidden" : AgentModuleCodes.UnsupportedCode, error.Code);
        Assert.Equal(unavailable, error.OwnerUnavailable);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(error));
        if (phase != "authority") Assert.Equal("bytes", error.Limit);
        Assert.Equal(0, tool.Inner.ExecuteCalls);
    }

    [Fact]
    public async Task Normalized_execution_error_metadata_crosses_the_actual_source_generated_HTTP_response()
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { ErrorPhase = "execute", RawCode = "agent." + canary, RawMessage = canary, Unavailable = true };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            services.AddRouting().AddElsaEndpoints();
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var response = await AgentApiExecutionOutcomeTests.ExecuteAsync(fixture, proposal, proposal.BaseRevision);
        Assert.Equal(503, response.Status);
        var error = response.Body.GetProperty("error");
        Assert.Equal(AgentModuleCodes.UnavailableCode, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("ownerUnavailable").GetBoolean());
        Assert.Equal("bytes", error.GetProperty("limit").GetString());
        Assert.DoesNotContain(canary, response.Body.GetRawText());
        Assert.Equal(1, tool.ExecuteRequests);
        Assert.Equal(0, tool.Inner.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_turn_provider_journal_audit_and_SSE_receive_only_normalized_typed_fields(bool unavailable)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { ReadOnly = true, ErrorPhase = "execute", RawCode = "outside." + canary, RawMessage = canary, Unavailable = unavailable };
        var provider = new CallingProvider(tool.Descriptor.Name);
        using var root = AgentTestServices.Create(services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            services.AddSingleton<IAgentProvider>(provider);
            services.AddScoped<IAgentTool>(_ => tool);
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant", "actor", "conversation", provider.ProviderId, "explain", null,
            AgentPolicy.Default, new Dictionary<string, string>()));
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "Read the resource.", AgentMessageStatus.Pending, null, [], []));
        var events = new List<AgentStreamEvent>();
        await foreach (var item in services.GetRequiredService<IAgentStreamingService>().StreamAsync(session.Id)) events.Add(item);
        Assert.Equal(1, tool.ExecuteRequests);
        Assert.Equal(0, tool.Inner.ExecuteCalls);
        Assert.NotEmpty(provider.Inputs);
        foreach (var item in events)
            Assert.DoesNotContain(canary, JsonSerializer.Serialize(item, AgentSseJsonContext.Default.AgentStreamEvent));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(provider.Inputs));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(await services.GetRequiredService<IAgentSessionRecordStore>().LoadAsync(session.Id)));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(await services.GetRequiredService<IAgentAuditReader>().ListAsync(session.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_receipt_code_changes_typed_text_without_discarding_a_genuine_effect(bool custom)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { RawCode = "outside." + canary, RawMessage = canary };
        using var fixture = await CreateAsync(tool, custom);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var applied = await fixture.ExecuteAsync(proposal);
        Assert.True(applied.Value?.Executed);
        Assert.Equal("executed", applied.Value?.Status);
        Assert.Equal(AgentModuleCodes.UnsupportedCode, applied.Value!.ToolResult!.Receipt!.ModuleCode);
        Assert.Equal(AgentEffectScope.ServerPersisted, applied.Value.ToolResult.Receipt.Scope);
        Assert.Equal("new", tool.Inner.Value);
        Assert.Equal(1, tool.Inner.ExecuteCalls);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(applied));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(await fixture.Services.GetRequiredService<IAgentSessionRecordStore>().LoadAsync(fixture.Session.Id)));
        Assert.Same(applied.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(1, tool.Inner.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_custom_executor_cannot_borrow_fresh_owner_unavailable_authority(bool declared)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { Codes = declared ? ["native.unavailable"] : null };
        var genuine = AgentModuleCodes.Error(new("native.unavailable", canary, 503) { OwnerUnavailable = true }, tool.Codes, ownerOrigin: true);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            services.Replace(ServiceDescriptor.Scoped<IAgentActionProposalExecutor>(_ => new BorrowedErrorExecutor(genuine)));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var result = await fixture.ExecuteAsync(proposal);
        Assert.False(result.Error!.OwnerUnavailable);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
        Assert.Equal(0, tool.Inner.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replaced_custom_errors_sanitize_failed_result_text_in_records_duplicates_and_HTTP(bool forgedAuthority)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new CodeTool { Codes = ["native.unavailable"] };
        var forged = new AgentError(forgedAuthority ? "native.unavailable" : "outside." + canary, canary, 503)
            { OwnerUnavailable = forgedAuthority, Limit = "bytes" };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            services.AddRouting().AddElsaEndpoints();
            services.Replace(ServiceDescriptor.Scoped<IAgentActionProposalExecutor>(_ => new BorrowedErrorExecutor(forged, true)));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var result = await fixture.ExecuteAsync(proposal);
        Assert.False(result.Error!.OwnerUnavailable);
        Assert.Equal("bytes", result.Error.Limit);
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, result.Value?.Summary);
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, result.Value?.ToolResult?.Content);
        Assert.False(result.Value!.ToolResult!.Succeeded);
        Assert.Null(result.Value.ToolResult.Receipt);
        Assert.True(result.Value.ToolResult.Payload!.Data.GetProperty("refused").GetBoolean());
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(await fixture.Services.GetRequiredService<IAgentSessionRecordStore>().LoadAsync(fixture.Session.Id)));
        var duplicate = await fixture.ExecuteAsync(proposal);
        Assert.Same(result.Value, duplicate.Value);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(duplicate));
        var response = await AgentApiExecutionOutcomeTests.ExecuteAsync(fixture, proposal, proposal.BaseRevision);
        Assert.Equal(503, response.Status);
        Assert.DoesNotContain(canary, response.Body.GetRawText());
        var data = response.Body.GetProperty("data");
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, data.GetProperty("summary").GetString());
        Assert.Equal(AgentModuleCodes.UnsupportedMessage, data.GetProperty("toolResult").GetProperty("content").GetString());
        Assert.True(data.GetProperty("toolResult").GetProperty("payload").GetProperty("data").GetProperty("refused").GetBoolean());
        Assert.Equal(0, tool.Inner.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Fresh_and_retained_contexts_keep_genuine_unavailability_after_code_replacement(bool retained, bool unavailable)
    {
        var canary = Guid.NewGuid().ToString("N");
        var source = new CodeSource(new(CodeSource.Id, "agent." + canary, canary, true) { OwnerUnavailable = unavailable });
        using var root = AgentTestServices.Create(services =>
        {
            new FoundationAgentEffectsFeature().ConfigureServices(services);
            services.AddScoped<IAgentContextProvider>(_ => source);
        });
        using var scope = root.CreateScope();
        var request = new AgentContextRequest("session", "*", new Dictionary<string, string>())
            { Disclosure = retained ? new("session", [CodeSource.Attachment()], [], false) : null };
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default, request);
        Assert.Equal(unavailable ? 503 : 400, result.Error?.StatusCode);
        Assert.Equal(unavailable ? AgentModuleCodes.UnavailableCode : "agent.context.incomplete", result.Error?.Code);
        Assert.Equal(unavailable, result.Error?.OwnerUnavailable);
        Assert.Equal(unavailable ? AgentModuleCodes.UnavailableCode : AgentModuleCodes.UnsupportedCode, Assert.Single(result.Value!.Diagnostics).Code);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Another_event_handler_cannot_borrow_a_normalized_diagnostic_from_a_previous_collection()
    {
        var normalized = AgentModuleCodes.Diagnostic(new(CodeSource.Id, "agent.invalid", Guid.NewGuid().ToString("N"), true)
            { OwnerUnavailable = true }, ["native.ok"], ownerOrigin: true);
        var previous = new AgentContextCollecting(new("previous", "*", new Dictionary<string, string>()));
        AgentContextOwnerMarkers.Record(previous, normalized, true);
        using var root = AgentTestServices.Create(services =>
        {
            services.AddSingleton(normalized);
            services.TryAddEventHandler<AgentContextCollecting, BorrowedDiagnosticHandler>();
        });
        using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>()));
        Assert.Equal("agent.context.incomplete", result.Error?.Code);
        Assert.False(Assert.Single(result.Value!.Diagnostics).OwnerUnavailable);
    }

    private static Task<ProposalFixture> CreateAsync(CodeTool tool, bool custom = false) => ProposalFixture.CreateAsync(tool, configure: services =>
    {
        new FoundationAgentEffectsFeature().ConfigureServices(services);
        if (custom) services.Replace(ServiceDescriptor.Scoped<IAgentActionProposalExecutor>(_ => new CodeExecutor(tool)));
    });

    private sealed class CodeTool : IAgentTool, IAgentToolAuthorization
    {
        internal ProposalMutationTool Inner { get; } = new();
        internal IReadOnlyCollection<string>? Codes { get; init; } = ["native.ok"];
        internal string? ErrorPhase { get; init; }
        internal string RawCode { get; init; } = "native.ok";
        internal string RawMessage { get; init; } = "A safe owner explanation.";
        internal bool Unavailable { get; init; }
        internal bool ReadOnly { get; init; }
        internal int ExecuteRequests { get; private set; }
        public AgentToolDescriptor Descriptor => Inner.Descriptor with { ModuleCodes = Codes,
            Mutability = ReadOnly ? AgentToolMutability.ReadOnly : AgentToolMutability.Mutating, Risk = ReadOnly ? AgentRisk.ReadOnly : Inner.CurrentRisk };
        private AgentError Error => new(RawCode, RawMessage, Unavailable ? 503 : 409) { OwnerUnavailable = Unavailable, Limit = "bytes" };
        public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            if (ErrorPhase == "prepare") return new(null, Error);
            var preparation = await Inner.PrepareAsync(invocation, cancellationToken);
            return ReadOnly ? preparation with { Value = preparation.Value! with { Risk = AgentRisk.ReadOnly } } : preparation;
        }
        public async Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            ExecuteRequests++;
            if (ErrorPhase == "execute") return new(null, Error);
            var value = (await Inner.ExecuteAsync(invocation, cancellationToken)).Value!;
            return AgentResult<AgentToolResult>.Success(value with { Content = RawMessage, Receipt = value.Receipt! with { ModuleCode = RawCode } });
        }
        public ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ErrorPhase == "authority"
                ? new AgentPolicyDecision(false, [new(RawCode, RawMessage) { OwnerUnavailable = Unavailable }])
                : new AgentPolicyDecision(true, []));
    }
    private sealed class CodeExecutor(CodeTool tool) : IAgentActionProposalExecutor
    {
        public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
        {
            var result = (await tool.ExecuteAsync(proposal.PreparedInvocation!, cancellationToken)).Value!;
            return AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Result(proposal, true, result.Content, "executed") with { ToolResult = result });
        }
        public Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal, AgentClientAcknowledgement acknowledgement,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class BorrowedErrorExecutor(AgentError error, bool includeValue = false) : IAgentActionProposalExecutor
    {
        public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResult<AgentProposalExecutionResult>(includeValue
                ? DefaultAgentActionProposalExecutor.Result(proposal, false, error.Message, "refused") with
                {
                    ToolResult = new(proposal.PreparedInvocation!.ToolCallId, proposal.PreparedInvocation.ToolName, false, error.Message,
                        new("test.change", "1", JsonSerializer.SerializeToElement(new { refused = true })))
                } : null, error));
        public Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal, AgentClientAcknowledgement acknowledgement,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class CodeSource(AgentContributionDiagnostic diagnostic) : IAgentContextProvider
    {
        internal const string Id = "codes.source";
        internal IReadOnlyCollection<string>? Codes { get; init; } = ["native.ok"];
        public AgentContextProviderDescriptor Descriptor => new(Id, "codes.module", "*", ["codes.context"], ["1"], ["codes.resource"])
            { ModuleCodes = Codes };
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([], [diagnostic]));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
            => CollectAsync(new(request.SessionId, "*", new Dictionary<string, string>()), cancellationToken);
        internal static AgentContextAttachment Attachment() => new("context", "codes.module", "resource", "Resource", "codes.context",
            AgentContextSensitivity.Internal, "selection", "Safe context", null, new Dictionary<string, string>())
            { ModuleId = "codes.module", ContributorId = Id, Target = new("codes.module", "codes.resource", "resource", "1") };
    }
    private sealed class BorrowedDiagnosticHandler(AgentContributionDiagnostic diagnostic) : IEventHandler<AgentContextCollecting>
    {
        public Task Handle(AgentContextCollecting domainEvent, CancellationToken cancellationToken)
        { domainEvent.Diagnostics.Add(diagnostic); return Task.CompletedTask; }
    }
    private sealed class CallingProvider(string toolName) : IAgentProvider
    {
        private readonly DeterministicAgentProvider _inner = new();
        internal List<AgentTurnContext> Inputs { get; } = [];
        public string ProviderId => _inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => _inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => _inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Inputs.Add(context);
            if (context.StepIndex == 0)
                yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, "code-call", null, DateTimeOffset.UtcNow,
                    Payload: new AgentToolCallRequest("code-call", toolName, "{}", AgentRisk.ReadOnly, false));
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
    }
}
