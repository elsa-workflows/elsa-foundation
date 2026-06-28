using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Workflows.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class GitHubCopilotAgentProviderTests
{
    private readonly FakeCopilotClientFactory _factory = new();
    private readonly GitHubCopilotAgentOptions _options = new();
    private readonly GitHubCopilotAgentProvider _provider;

    public GitHubCopilotAgentProviderTests()
    {
        var audit = new InMemoryAgentAuditStore();
        var invoker = new DefaultAgentToolInvoker(new DefaultAgentToolRegistry([]), new InMemoryAgentProposalService(new NoopAgentActionProposalExecutor(), audit), audit);
        _provider = new(_factory, Options.Create(_options), invoker, NullLogger<GitHubCopilotAgentProvider>.Instance);
    }

    [Fact]
    public async Task Diagnostics_reports_disabled_by_default()
    {
        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("disabled", diagnostics.Metadata["statusCode"]);
        Assert.Equal("none", diagnostics.Metadata["authMode"]);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);
        Assert.DoesNotContain(AgentProviderOperation.ToolApproval, diagnostics.SupportedOperations);
    }

    [Fact]
    public async Task Diagnostics_reports_available_when_enabled_and_sdk_ping_succeeds()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.ModelIds.Add("gpt-5");

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.True(diagnostics.IsAvailable);
        Assert.Equal("configured", diagnostics.Metadata["statusCode"]);
        Assert.Equal("configured-token", diagnostics.Metadata["authMode"]);
        Assert.Equal("gpt-5", diagnostics.Metadata["models"]);
        Assert.True(_factory.Client.Started);
        Assert.True(_factory.Client.Pinged);
    }

    [Fact]
    public async Task Diagnostics_reports_missing_auth_when_enabled_without_backend_identity()
    {
        _options.Enabled = true;
        _options.GitHubTokenEnvironmentVariable = "ELSA_COPILOT_TEST_TOKEN_NOT_SET";
        Environment.SetEnvironmentVariable(_options.GitHubTokenEnvironmentVariable, null);

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("missing-auth", diagnostics.Metadata["statusCode"]);
    }

    [Fact]
    public async Task Diagnostics_reports_misconfigured_runtime_url()
    {
        _options.Enabled = true;
        _options.RuntimeUrl = "not-a-runtime-url";

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("misconfigured", diagnostics.Metadata["statusCode"]);
    }

    [Theory]
    [InlineData("8080")]
    [InlineData("localhost:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://localhost:8080")]
    public async Task Diagnostics_accepts_sdk_runtime_url_formats(string runtimeUrl)
    {
        _options.Enabled = true;
        _options.RuntimeUrl = runtimeUrl;

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.True(diagnostics.IsAvailable);
        Assert.Equal("external-runtime", diagnostics.Metadata["authMode"]);
    }

    [Fact]
    public async Task Diagnostics_reports_sdk_failure_without_exposing_token()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.PingException = new InvalidOperationException("not authenticated with test-token");

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("sdk-unavailable", diagnostics.Metadata["statusCode"]);
        Assert.DoesNotContain("test-token", diagnostics.Status);
        Assert.Contains("[redacted]", diagnostics.Status);
        Assert.DoesNotContain("test-token", diagnostics.Metadata.Values);
    }

    [Fact]
    public async Task Create_session_uses_elsa_session_id_for_sdk_session()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _options.Model = "gpt-5";
        var session = CreateSession("session-1");

        var providerSession = await _provider.CreateSessionAsync(session);

        Assert.Equal("session-1", providerSession.Id);
        Assert.Equal("session-1", _factory.Client.CreatedSessionRequest?.SessionId);
        Assert.Equal("gpt-5", _factory.Client.CreatedSessionRequest?.Model);
        Assert.Equal("session-1", providerSession.Metadata["sessionId"]);
    }

    [Fact]
    public async Task Create_session_does_not_send_auto_as_model_id()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _options.Model = "auto";
        var session = CreateSession("session-1");

        _ = await _provider.CreateSessionAsync(session);

        Assert.Null(_factory.Client.CreatedSessionRequest?.Model);
    }

    [Fact]
    public async Task Stream_maps_started_delta_completed_events()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Started));
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.MessageDelta, "Hello"));
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Completed));

        var events = await StreamAsync(new("session-1", "Explain", []));

        Assert.Collection(
            events,
            started => Assert.Equal(AgentStreamEventKind.Started, started.Kind),
            delta =>
            {
                Assert.Equal(AgentStreamEventKind.MessageDelta, delta.Kind);
                Assert.Equal("Hello", delta.Content);
            },
            completed => Assert.Equal(AgentStreamEventKind.Completed, completed.Kind));
    }

    [Fact]
    public async Task Stream_includes_sanitized_context_in_prompt()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Completed));
        var context = new AgentContextAttachment(
            "ctx-1",
            "workflow.definition",
            "wf-1",
            "Workflow",
            "application/json",
            AgentContextSensitivity.Internal,
            "selection",
            "Workflow summary.",
            new { Name = "Demo" },
            new Dictionary<string, string>());

        _ = await StreamAsync(new("session-1", "Explain", [context]));

        Assert.Contains("Elsa context attachments:", _factory.Client.Session.LastPrompt);
        Assert.Contains("Workflow summary.", _factory.Client.Session.LastPrompt);
        Assert.Contains("Demo", _factory.Client.Session.LastPrompt);
    }

    [Fact]
    public async Task Stream_includes_workflow_authoring_result_contract_in_prompt()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Completed));

        _ = await StreamAsync(new("session-1", "Create a workflow.", [CreateWorkflowContext()]));

        Assert.Contains("Workflow authoring response contract:", _factory.Client.Session.LastPrompt);
        Assert.Contains("workflowGraphOperationBatch", _factory.Client.Session.LastPrompt);
    }

    [Fact]
    public async Task Stream_maps_workflow_graph_operation_batch_envelope()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.MessageDelta, """
            {
              "resultKind": "workflowGraphOperationBatch",
              "message": "Prepared one workflow graph operation batch.",
              "workflowGraphOperationBatch": {
                "schemaVersion": "elsa.workflow-graph-operation-batch.v1",
                "workflowDefinitionId": "wf-1",
                "baseRevision": "rev-1",
                "operations": [
                  {
                    "id": "op-add-email",
                    "kind": "AddActivity",
                    "parameters": {
                      "activityId": "temp:activity:email-1",
                      "activityType": "Elsa.Email.SendEmail"
                    },
                    "temporaryReferences": ["temp:activity:email-1"],
                    "summary": "Add email activity."
                  }
                ],
                "metadata": {
                  "source": "github-copilot"
                }
              }
            }
            """));

        var events = await StreamAsync(new("session-1", "Create workflow graph operation batch.", [CreateWorkflowContext()]));

        var batchEvent = Assert.Single(events, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);
        Assert.Equal(AgentStreamEventKind.WorkflowGraphOperationBatchCreated, batchEvent.Kind);
        var batch = Assert.IsType<WorkflowGraphOperationBatch>(batchEvent.Payload);
        Assert.Equal(WorkflowGraphOperationBatchSchema.CurrentVersion, batch.SchemaVersion);
        Assert.Equal("wf-1", batch.WorkflowDefinitionId);
        Assert.Contains(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.AddActivity);
    }

    [Fact]
    public async Task Stream_maps_structured_clarification_envelope()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.MessageDelta, """
            {
              "resultKind": "clarification",
              "clarification": {
                "id": "clarify-target",
                "question": "Which workflow branch should Weaver update?",
                "options": [
                  {
                    "id": "active-draft",
                    "label": "Active draft",
                    "value": "active-draft",
                    "description": "Update the current designer draft."
                  }
                ],
                "continuationToken": "session-1:clarification:target",
                "metadata": {
                  "workflowDefinitionId": "wf-1"
                }
              }
            }
            """));

        var events = await StreamAsync(new("session-1", "Ask a clarification question.", [CreateWorkflowContext()]));

        var clarificationEvent = Assert.Single(events, x => x.ResultKind == AgentResultKind.Clarification);
        Assert.Equal(AgentStreamEventKind.ClarificationRequested, clarificationEvent.Kind);
        var clarification = Assert.IsType<WorkflowClarificationResult>(clarificationEvent.Payload);
        Assert.Equal("Which workflow branch should Weaver update?", clarification.Question);
        Assert.Contains(clarification.Options, x => x.Value == "active-draft");
    }

    [Fact]
    public async Task Stream_maps_structured_error_envelope_without_token_leakage()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.MessageDelta, """
            {
              "resultKind": "error",
              "errorCode": "copilot.workflow.failed",
              "errorMessage": "provider failed with test-token"
            }
            """));

        var events = await StreamAsync(new("session-1", "Create workflow.", [CreateWorkflowContext()]));

        var error = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.Error, error.Kind);
        Assert.Equal("copilot.workflow.failed", error.Error?.Code);
        Assert.Equal("provider failed with [redacted]", error.Error?.Message);
    }

    [Fact]
    public async Task Stream_maps_sdk_error_event()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Error, ErrorCode: "sdk.error", ErrorMessage: "SDK failed for test-token"));

        var events = await StreamAsync(new("session-1", "Explain", []));

        var error = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.Error, error.Kind);
        Assert.Equal("sdk.error", error.Error?.Code);
        Assert.Equal("SDK failed for [redacted]", error.Error?.Message);
    }

    [Fact]
    public async Task Stream_observes_cancellation()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.WaitForCancellation = true;
        using var cts = new CancellationTokenSource();

        var task = StreamAsync(new("session-1", "Explain", []), cts.Token);
        await Task.Yield();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(_factory.Client.Session.CancellationObserved);
    }

    [Fact]
    public async Task Tool_approval_is_not_exposed_directly()
    {
        var result = await _provider.ApproveToolAsync(new("session-1", "tool-1", true, null));

        Assert.False(result.Accepted);
        Assert.Contains("Elsa-owned proposal approval", result.Message);
    }

    private async Task<List<AgentStreamEvent>> StreamAsync(AgentProviderMessage message, CancellationToken cancellationToken = default)
    {
        var turn = AgentTurnContext.ForMessage(message.SessionId, message.Content, message.Context);
        var events = new List<AgentStreamEvent>();
        await foreach (var item in _provider.ContinueTurnAsync(turn, cancellationToken))
            events.Add(item);
        return events;
    }

    private static AgentSession CreateSession(string id) => new(
        id,
        "Test session",
        "tenant-1",
        "actor-1",
        "conversation-1",
        GitHubCopilotAgentProvider.Id,
        "explain",
        AgentPolicy.Default,
        AgentSessionStatus.Active,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        new Dictionary<string, string>());

    private static AgentContextAttachment CreateWorkflowContext()
        => new(
            "ctx-1",
            "workflow.definition",
            "wf-1",
            "Workflow",
            "workflow.definition",
            AgentContextSensitivity.Internal,
            "selection",
            "Workflow definition 'wf-1' at revision 'rev-1'.",
            null,
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = "wf-1",
                ["revision"] = "rev-1"
            });

    private sealed class FakeCopilotClientFactory : IGitHubCopilotClientFactory
    {
        public FakeCopilotClient Client { get; } = new();

        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request)
        {
            Client.LastClientRequest = request;
            return Client;
        }
    }

    private sealed class FakeCopilotClient : IGitHubCopilotClient
    {
        public FakeCopilotSession Session { get; } = new();

        public List<string> ModelIds { get; } = [];

        public GitHubCopilotClientRequest? LastClientRequest { get; set; }

        public GitHubCopilotSessionRequest? CreatedSessionRequest { get; private set; }

        public bool Started { get; private set; }

        public bool Pinged { get; private set; }

        public Exception? PingException { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task PingAsync(CancellationToken cancellationToken)
        {
            Pinged = true;
            return PingException is null ? Task.CompletedTask : Task.FromException(PingException);
        }

        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>(ModelIds);

        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            CreatedSessionRequest = request;
            Session.SessionId = request.SessionId;
            return Task.FromResult<IGitHubCopilotSession>(Session);
        }

        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            Session.SessionId = request.SessionId;
            return Task.FromResult<IGitHubCopilotSession>(Session);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCopilotSession : IGitHubCopilotSession
    {
        public List<GitHubCopilotStreamEvent> Events { get; } = [];

        public string SessionId { get; set; } = string.Empty;

        public string? WorkspacePath => null;

        public string LastPrompt { get; private set; } = string.Empty;

        public bool WaitForCancellation { get; set; }

        public bool CancellationObserved { get; private set; }

        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            foreach (var item in Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
