using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class AgentCurrentToolAdmissionTests
{
    private const string Module = "current-admission.module";
    private const string SourceId = "current-admission.source";
    private const string ReadName = "current_admission_read";
    private const string WriteName = "current_admission_write";
    private const string ReadScope = "current-admission/read";
    private const string EditScope = "current-admission/edit";
    private const string ForbiddenOutcome = "MUTATION_OUTCOME_MUST_NOT_EXIST_AFTER_ADMISSION_CHANGES";
    private static readonly AgentResourceTarget Original = new(Module, "owned-resource", "original-resource", "revision-1");
    private static readonly AgentResourceTarget Other = new(Module, "owned-resource", "other-resource", "revision-2");

    [Theory]
    [InlineData("copilot")]
    [InlineData("host-steps")]
    [InlineData("host-batch")]
    public async Task A_later_call_cannot_use_an_edit_scope_removed_by_the_post_read_projection(string path)
    {
        using var fixture = new Fixture(path, retainEdit: false, changeTarget: false);

        var events = await fixture.Run();

        Assert.Equal(1, fixture.Read.Executions);
        Assert.True(fixture.Source.PostReadProjections > 0);
        Assert.Equal(0, fixture.Write.Preparations);
        Assert.Equal(0, fixture.Write.Executions);
        Assert.Empty(fixture.Write.PreparedTargets);
        Assert.Equal("original-value", fixture.Resource.Values[Original.ResourceId!]);
        Assert.Equal("other-original-value", fixture.Resource.Values[Other.ResourceId!]);
        AssertNoMutationResult(events);
        fixture.AssertSecondCallDenied(events, "agent.tool.not_admitted");
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("host-steps")]
    [InlineData("host-batch")]
    public async Task A_later_call_cannot_silently_follow_a_changed_resource_target_or_revision(string path)
    {
        using var fixture = new Fixture(path, retainEdit: true, changeTarget: true);

        var events = await fixture.Run();

        Assert.Equal(1, fixture.Read.Executions);
        Assert.True(fixture.Source.PostReadProjections > 0);
        Assert.Equal(0, fixture.Write.Preparations);
        Assert.Equal(0, fixture.Write.Executions);
        Assert.Empty(fixture.Write.PreparedTargets);
        Assert.Equal("original-value", fixture.Resource.Values[Original.ResourceId!]);
        Assert.Equal("other-original-value", fixture.Resource.Values[Other.ResourceId!]);
        AssertNoMutationResult(events);
        fixture.AssertSecondCallDenied(events, "agent.tool.context_changed");
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("host-steps")]
    [InlineData("host-batch")]
    public async Task Retaining_the_edit_scope_and_original_target_reaches_the_second_operation_once(string path)
    {
        using var fixture = new Fixture(path, retainEdit: true, changeTarget: false);

        var events = await fixture.Run();

        Assert.Equal(1, fixture.Read.Executions);
        Assert.True(fixture.Source.PostReadProjections > 0);
        Assert.Equal(1, fixture.Write.Preparations);
        Assert.Equal(1, fixture.Write.Executions);
        Assert.Equal(Original, Assert.Single(fixture.Write.PreparedTargets));
        Assert.Equal("changed-value", fixture.Resource.Values[Original.ResourceId!]);
        Assert.Equal("other-original-value", fixture.Resource.Values[Other.ResourceId!]);
        Assert.Contains(events, item => item.Payload is AgentToolResult { ToolName: WriteName, Succeeded: true, Receipt: not null });
        Assert.DoesNotContain(events, item => item.Error is not null);
        fixture.AssertSecondCallAttempted();
    }

    private static void AssertNoMutationResult(IEnumerable<AgentStreamEvent> events)
    {
        Assert.DoesNotContain(events, item => item.Payload is AgentToolResult { ToolName: WriteName, Succeeded: true });
        Assert.DoesNotContain(events, item => item.Payload is AgentToolResult { Receipt: not null });
        Assert.DoesNotContain(events, item => item.Payload is AgentActionProposal);
        Assert.DoesNotContain(ForbiddenOutcome, JsonSerializer.Serialize(events));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        private readonly bool copilot;
        internal Resource Resource { get; } = new();
        internal ReadTool Read { get; }
        internal WriteTool Write { get; }
        internal ProjectionSource Source { get; }
        internal SequenceProvider Provider { get; }
        internal SdkSession Sdk { get; } = new();

        internal Fixture(string path, bool retainEdit, bool changeTarget)
        {
            copilot = path == "copilot";
            Read = new(Resource);
            Write = new(Resource);
            Source = new(Resource, retainEdit, changeTarget);
            Provider = new(path == "host-batch");
            root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentTool>(Read);
                services.AddSingleton<IAgentTool>(Write);
                services.AddScoped<IAgentContextProvider>(_ => Source);
                // Permissions deliberately remain allowed for both tools and both resources.
                // A current scope/target admission check must be the cause of refusal.
                services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => new PermissiveAuthority()));
                if (copilot)
                    services.AddScoped<IAgentProvider>(provider => new GitHubCopilotAgentProvider(new SdkFactory(Sdk),
                        Options.Create(new GitHubCopilotAgentOptions { Enabled = true, RuntimeUrl = "http://127.0.0.1:1",
                            UseLoggedInUser = false, GitHubTokenEnvironmentVariable = string.Empty }),
                        provider.GetRequiredService<IAgentToolInvoker>(), NullLogger<GitHubCopilotAgentProvider>.Instance));
                else services.AddSingleton<IAgentProvider>(Provider);
            });
            scope = root.CreateScope();
        }

        internal async Task<List<AgentStreamEvent>> Run()
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
            var policy = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };
            var session = await sessions.CreateAsync(new("tenant", "actor", "conversation",
                copilot ? GitHubCopilotAgentProvider.Id : Provider.ProviderId, "build", null, policy, new Dictionary<string, string>()));
            var context = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(policy,
                new(session.Id, "*", new Dictionary<string, string>()));
            Assert.True(context.Succeeded, context.Error?.Message);
            var attachments = context.Value!.Attachments;
            await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "Read, then change the originally selected resource.",
                AgentMessageStatus.Pending, null, attachments.Select(item => item.Id).ToArray(), attachments));
            using var cancellation = new CancellationTokenSource();
            try { return await Collect(session.Id, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { cancellation.Cancel(); }
        }

        private async Task<List<AgentStreamEvent>> Collect(string sessionId, CancellationToken token)
        {
            var events = new List<AgentStreamEvent>();
            await foreach (var item in scope.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId, token))
                events.Add(item);
            return events;
        }

        internal void AssertSecondCallAttempted()
        {
            if (copilot)
            {
                Assert.Equal(new[] { ReadName, WriteName }, Sdk.Attempted);
                Assert.Contains(WriteName, Sdk.CapturedTools);
            }
            else Assert.Equal(new[] { ReadName, WriteName }, Provider.Attempted);
        }

        internal void AssertSecondCallDenied(IEnumerable<AgentStreamEvent> events, string code)
        {
            AssertSecondCallAttempted();
            if (copilot)
            {
                var returned = JsonSerializer.SerializeToElement(Sdk.WriteReturn, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.Equal("denied", returned.GetProperty("status").GetString());
                Assert.False(returned.GetProperty("executed").GetBoolean());
                Assert.Equal(code, returned.GetProperty("error").GetProperty("code").GetString());
            }
            else
            {
                var denial = Assert.IsType<AgentToolResult>(Assert.Single(events,
                    item => item.Kind == AgentStreamEventKind.ContributedResult && item.Payload is AgentToolResult { ToolName: WriteName }).Payload);
                Assert.False(denial.Succeeded);
                if (code == "agent.tool.not_admitted") Assert.Contains("not admitted", denial.Content, StringComparison.OrdinalIgnoreCase);
                else
                {
                    Assert.Contains("context", denial.Content, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("changed", denial.Content, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class Resource
    {
        internal int Reads;
        internal Dictionary<string, string> Values { get; } = new()
        { [Original.ResourceId!] = "original-value", [Other.ResourceId!] = "other-original-value" };
    }

    private sealed class ProjectionSource(Resource resource, bool retainEdit, bool changeTarget) : IAgentContextProvider
    {
        internal int PostReadProjections;
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["scope.resource"], ["1"], ["owned-resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment(Original, [ReadScope, EditScope])], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (request.IsHistorical) return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
            if (request.IsReceiptContinuation) return ValueTask.FromResult(new AgentContextContribution([], []));
            var afterRead = resource.Reads > 0;
            if (afterRead) PostReadProjections++;
            IReadOnlyCollection<string> scopes = afterRead && !retainEdit ? [ReadScope] : [ReadScope, EditScope];
            return ValueTask.FromResult(new AgentContextContribution([Attachment(afterRead && changeTarget ? Other : Original, scopes)], []));
        }
        private static AgentContextAttachment Attachment(AgentResourceTarget target, IReadOnlyCollection<string> scopes)
            => new("current-resource", Module, target.ResourceId, "Selected resource", "scope.resource", AgentContextSensitivity.Internal,
                "selection", "Bounded selected-resource data", new { target.ResourceId, target.Revision }, new Dictionary<string, string>())
            { ModuleId = Module, Target = target, AgentScopes = scopes };
    }

    private sealed class PermissiveAuthority : IAgentToolAuthorizer
    {
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
            AgentResourceTarget? target, CancellationToken cancellationToken = default) => Task.FromResult(new AgentPolicyDecision(true, []));
    }

    private sealed class ReadTool(Resource resource) : IAgentTool
    {
        internal int Executions;
        public AgentToolDescriptor Descriptor { get; } = DescriptorFor(ReadName, AgentToolMutability.ReadOnly, ReadScope);
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = Original }));
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++; resource.Reads++;
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, ReadName, true, "Read completed without changing either resource.",
                new("scope.result", "1", JsonSerializer.SerializeToElement(new { value = resource.Values[Original.ResourceId!] })))));
        }
    }

    private sealed class WriteTool(Resource resource) : IAgentTool
    {
        internal int Preparations, Executions;
        internal List<AgentResourceTarget> PreparedTargets { get; } = [];
        public AgentToolDescriptor Descriptor { get; } = DescriptorFor(WriteName, AgentToolMutability.Mutating, EditScope);
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            var target = Assert.Single(invocation.Context, item => item.ModuleId == Module && item.Target is not null).Target!;
            PreparedTargets.Add(target);
            return Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with { Target = target }));
        }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            var target = invocation.Target!;
            resource.Values[target.ResourceId!] = "changed-value";
            var payload = new AgentPayload("scope.result", "1", JsonSerializer.SerializeToElement(new { value = "changed-value" }));
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, WriteName, true, ForbiddenOutcome, payload)
            { Receipt = new(invocation.InvocationId, target, target with { Revision = "effect-revision" }, AgentEffectScope.ServerPersisted, payload) }));
        }
    }

    private static AgentToolDescriptor DescriptorFor(string name, AgentToolMutability mutability, string scope)
        => new(name, name, "Exercise current module tool admission.", mutability,
            mutability == AgentToolMutability.ReadOnly ? AgentRisk.ReadOnly : AgentRisk.ReviewRequired, "{\"type\":\"object\"}", [], [scope])
        { ModuleId = Module, DisclosureSourceId = SourceId, ResultType = "scope.result", ResultSchemaVersion = "1" };

    private sealed class SequenceProvider(bool batch) : IAgentProvider
    {
        private readonly DeterministicAgentProvider inner = new();
        internal List<string> Attempted { get; } = [];
        public string ProviderId => inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (context.StepIndex == 0)
            {
                Attempted.Add(ReadName); yield return Call("host-read", ReadName, AgentRisk.ReadOnly);
                if (batch) { Attempted.Add(WriteName); yield return Call("host-write", WriteName, AgentRisk.ReviewRequired); }
            }
            else if (!batch && context.StepIndex == 1)
            {
                Attempted.Add(WriteName); yield return Call("host-write", WriteName, AgentRisk.ReviewRequired);
            }
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
        private static AgentStreamEvent Call(string id, string name, AgentRisk risk)
            => new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, id, null, DateTimeOffset.UtcNow,
                Payload: new AgentToolCallRequest(id, name, "{}", risk, false));
    }

    private sealed class SdkFactory(SdkSession session) : IGitHubCopilotClientFactory
    { public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new SdkClient(session); }
    private sealed class SdkClient(SdkSession session) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        { session.Tools = request.CustomTools ?? []; return Task.FromResult<IGitHubCopilotSession>(session); }
        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) => CreateSessionAsync(request, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class SdkSession : IGitHubCopilotSession
    {
        internal IReadOnlyCollection<AIFunction> Tools = [];
        internal List<string> Attempted { get; } = [];
        internal List<string> CapturedTools { get; } = [];
        internal object? WriteReturn;
        public string SessionId => "current-admission-sdk-session";
        public string? WorkspacePath => null;
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var captured = Tools.ToArray();
            CapturedTools.AddRange(captured.Select(tool => tool.Name));
            yield return new(GitHubCopilotStreamEventKind.Started);
            Attempted.Add(ReadName);
            await Assert.Single(captured, tool => tool.Name == ReadName).InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken);
            Attempted.Add(WriteName);
            WriteReturn = await Assert.Single(captured, tool => tool.Name == WriteName).InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken);
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }
        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
