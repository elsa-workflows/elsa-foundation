using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

public sealed partial class AgentCumulativeDisclosureTests
{
    private const string Module = "cumulative.module";
    private const string SourceId = "cumulative.context";
    private const string DerivedText = "The answer used the authorized detail resource at revision one.";

    [Theory]
    [InlineData(false, "withdrawn")]
    [InlineData(true, "withdrawn")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "restored")]
    [InlineData(true, "restored")]
    public async Task A_context_only_answer_retains_its_added_resource_dependency(bool delegated, string authority)
    {
        using var fixture = new Fixture(delegated, Script.Answer);
        await fixture.Post("First request");
        var first = await fixture.Stream();
        Assert.Contains(first, item => item.Content == DerivedText);
        Assert.Single(fixture.Probe.Inputs);
        Assert.Contains(fixture.Probe.Inputs[0].Context, item => item.Target?.ResourceId == "detail-resource");
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);

        fixture.Source.ShowDetail = false;
        fixture.Source.SetAuthority(authority);
        await fixture.Post("A later request with only the original resource");
        var second = await fixture.Stream();

        if (authority == "withdrawn")
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(second);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.Contains(fixture.Probe.Inputs[1].History, item => item.Content.Contains(DerivedText, StringComparison.Ordinal));
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Omitting_a_disclosed_resource_does_not_authorize_a_later_callback_or_step(bool delegated, bool withdraw)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer);
        fixture.Probe.BeforeOperation = () =>
        {
            fixture.Source.ShowDetail = false;
            if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        };
        await fixture.Post("Read after discussing the detail");

        var events = await fixture.Stream();

        Assert.Contains(events, item => item.Content == DerivedText);
        if (withdraw)
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.False(fixture.Probe.AfterCallbackEntered);
            Assert.Equal(0, fixture.Read.Preparations);
            Assert.Equal(0, fixture.Read.Executions);
            AssertDisclosureDenied(events);
        }
        else
        {
            Assert.Equal(1, fixture.Read.Executions);
            Assert.True(delegated ? fixture.Probe.AfterCallbackEntered : fixture.Probe.Inputs.Count == 2);
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, "withdrawn")]
    [InlineData(true, "withdrawn")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "restored")]
    [InlineData(true, "restored")]
    public async Task Pause_preserves_earlier_disclosure_after_the_current_projection_omits_it(bool delegated, string authority)
    {
        using var fixture = new Fixture(delegated, Script.PauseThenAnswer);
        fixture.Probe.BeforeOperation = () => fixture.Source.ShowDetail = false;
        await fixture.Post("Make one reviewed change after discussing the detail");
        var first = await fixture.Stream();
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first,
            item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        Assert.Contains(first, item => item.Content == DerivedText);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
        var applied = await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision);
        var receipt = Assert.IsType<AgentEffectReceipt>(applied.Value?.ToolResult?.Receipt);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal("changed", fixture.Mutation.Value);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, receipt.InvocationId);
        Assert.Equal(proposal.PreparedInvocation.Target, receipt.Before);
        fixture.Source.SetAuthority(authority);

        var resumed = await fixture.Stream();

        if (authority == "withdrawn")
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(resumed);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            var continuation = fixture.Probe.Inputs[1];
            Assert.Empty(continuation.Tools);
            Assert.Equal(receipt, Assert.Single(continuation.Results).Receipt);
            Assert.DoesNotContain(resumed, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(receipt, (await proposals.FindResultAsync(proposal.Id))?.Receipt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_attachment_identity_at_a_new_revision_cannot_erase_the_original_snapshot(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer);
        fixture.Probe.BeforeOperation = () => fixture.Source.DetailRevision = "detail-v2";
        await fixture.Post("Read both revisions without changing the resource");
        var first = await fixture.Stream();
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.Contains(fixture.Probe.Inputs[0].Context, item => item.Id == "detail" && item.Target?.Revision == "detail-v1");
        var journal = await fixture.Services.GetRequiredService<IAgentSessionService>().ListMessagesAsync(fixture.Session!.Id);
        Assert.Contains(journal, message => message.Role == AgentRole.Tool
            && message.Context.Any(item => item.Id == "detail" && item.Target?.Revision == "detail-v2"));
        fixture.Source.ShowDetail = false;
        fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Only the current original resource is requested");
        var entries = fixture.Probe.Inputs.Count;

        var second = await fixture.Stream();

        Assert.Equal(entries, fixture.Probe.Inputs.Count);
        AssertDisclosureDenied(second);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, "completed", true)]
    [InlineData(true, "completed", true)]
    [InlineData(false, "error", true)]
    [InlineData(true, "error", true)]
    [InlineData(false, "cancelled", true)]
    [InlineData(true, "cancelled", true)]
    [InlineData(false, "completed", false)]
    [InlineData(true, "completed", false)]
    [InlineData(false, "error", false)]
    [InlineData(true, "error", false)]
    [InlineData(false, "cancelled", false)]
    [InlineData(true, "cancelled", false)]
    public async Task A_textless_terminal_turn_still_protects_the_provider_transcript(bool delegated, string terminal, bool withdraw)
    {
        using var fixture = new Fixture(delegated, Script.Answer);
        fixture.Probe.TextlessTerminal = terminal;
        fixture.Probe.Cancel = () => fixture.Services.GetRequiredService<IAgentTurnRegistry>().Cancel(fixture.Session!.Id, fixture.Probe.TurnId!);
        await fixture.Post("Receive the detail without producing an answer");
        var first = await fixture.Stream();
        Assert.Single(fixture.Probe.Inputs);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Contains(first, item => item.Kind == (terminal == "completed" ? AgentStreamEventKind.Completed
            : terminal == "error" ? AgentStreamEventKind.Error : AgentStreamEventKind.TurnCancelled));
        fixture.Probe.TextlessTerminal = null;
        fixture.Source.ShowDetail = false;
        if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Use only the original resource now");

        var second = await fixture.Stream();

        if (withdraw)
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(second);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(fixture.Probe.Inputs[1].History, message => message.Role == AgentRole.Assistant && message.Content.Length == 0);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public async Task A_custom_session_store_cannot_omit_or_change_the_assistant_disclosure_journal(string mutation)
    {
        using var fixture = new Fixture(true, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        await fixture.Post("Produce one answer using the detail");
        Assert.DoesNotContain(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Error);
        fixture.Services.GetRequiredService<SessionBoundary>().Mutation = mutation;
        fixture.Source.ShowDetail = false;
        await fixture.Post("Reuse the retained transcript");

        var second = await fixture.Stream();

        if (mutation == "unchanged")
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(second, item => item.Error?.Code == "agent.history.provenance_unavailable");
        }
    }

    [Fact]
    public async Task A_textless_disclosure_journal_is_retained_without_creating_an_empty_chat_message()
    {
        using var fixture = new Fixture(true, Script.Answer, services => services.AddRouting().AddElsaEndpoints());
        fixture.Probe.TextlessTerminal = "completed";
        await fixture.Post("Read the authorized detail without answering");
        Assert.DoesNotContain(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Error);
        var messages = await fixture.Services.GetRequiredService<IAgentSessionService>().ListMessagesAsync(fixture.Session!.Id);
        var metadata = messages.Where(message => message.Role == AgentRole.Assistant).ToArray();
        Assert.NotEmpty(metadata);
        Assert.All(metadata, message =>
        {
            Assert.Empty(message.Content);
            Assert.Contains(message.DisclosureHistory!, snapshot => snapshot.Context.Any(item => item.Target?.ResourceId == "detail-resource"));
        });
        var app = new TestEndpointRouteBuilder(fixture.Services);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsGetSession");
        var context = new DefaultHttpContext
        {
            RequestServices = fixture.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "actor-1"), new Claim("elsa.identity.tenant_id", "tenant-1")], "test"))
        };
        context.Request.RouteValues["sessionId"] = fixture.Session.Id;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        var visible = response.RootElement.GetProperty("data").GetProperty("messages").EnumerateArray().ToArray();
        Assert.Single(visible);
        Assert.Equal("user", visible[0].GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(visible[0].GetProperty("content").GetString()));
    }

    [Theory]
    [InlineData(false, "answer", true)]
    [InlineData(true, "answer", true)]
    [InlineData(false, "completed", true)]
    [InlineData(true, "completed", true)]
    [InlineData(false, "error", true)]
    [InlineData(true, "error", true)]
    [InlineData(false, "cancelled", true)]
    [InlineData(true, "cancelled", true)]
    [InlineData(false, "answer", false)]
    [InlineData(true, "answer", false)]
    [InlineData(false, "completed", false)]
    [InlineData(true, "completed", false)]
    [InlineData(false, "error", false)]
    [InlineData(true, "error", false)]
    [InlineData(false, "cancelled", false)]
    [InlineData(true, "cancelled", false)]
    public async Task An_insert_failure_after_provider_input_cannot_leave_an_unrecorded_resource_disclosure(bool delegated, string terminal, bool withdraw)
    {
        using var fixture = new Fixture(delegated, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        fixture.CaptureDomainFailures = true;
        var failure = new IOException("Synthetic journal outage " + Guid.NewGuid().ToString("N"));
        fixture.Probe.TextlessTerminal = terminal == "answer" ? null : terminal;
        fixture.Probe.Cancel = () => fixture.Services.GetRequiredService<IAgentTurnRegistry>().Cancel(fixture.Session!.Id, fixture.Probe.TurnId!);
        fixture.Probe.AfterInput = () => { if (fixture.Probe.Inputs.Count == 1) store.ThrowBeforeAssistantInsert = failure; };
        await fixture.Post("Receive the detail before a storage failure");

        var first = await fixture.Stream();

        Assert.Single(fixture.Probe.Inputs);
        Assert.Contains(fixture.Probe.Inputs[0].Context, attachment => attachment.Target?.ResourceId == "detail-resource");
        Assert.Equal(1, store.FailedInserts);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.Completed);
        fixture.Probe.TextlessTerminal = null;
        fixture.Source.ShowDetail = false;
        if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Use only the original resource after storage recovers");

        var second = await fixture.Stream();

        if (withdraw)
        {
            Assert.Single(fixture.Probe.Inputs);
            AssertDisclosureDenied(second);
        }
        else
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_first_disclosure_insert_never_enters_the_provider_and_allows_fresh_authorized_recovery(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        fixture.CaptureDomainFailures = true;
        store.ThrowBeforeAssistantInsert = new IOException("Synthetic pre-disclosure outage " + Guid.NewGuid().ToString("N"));
        await fixture.Post("The detail must not reach an unjournaled provider");

        var first = await fixture.Stream();

        Assert.Equal(1, store.FailedInserts);
        Assert.Empty(fixture.Probe.Inputs);
        Assert.True(first.Any(item => item.Error?.Code == "agent.history.provenance_unavailable") || fixture.DomainFailures.Count > 0);
        fixture.Source.ShowDetail = false;
        fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Only the still-authorized original resource is needed");

        var second = await fixture.Stream();

        var recovered = Assert.Single(fixture.Probe.Inputs);
        Assert.DoesNotContain(recovered.Context, attachment => attachment.Target?.ResourceId == "detail-resource");
        Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("pause")]
    [InlineData("authorization")]
    [InlineData("unchanged")]
    public async Task Disclosure_infrastructure_failures_retain_their_server_cause_and_redact_the_transport(string boundary)
    {
        var failure = new IOException("Synthetic private infrastructure cause " + Guid.NewGuid().ToString("N"));
        using var fixture = new Fixture(true, boundary == "pause" ? Script.PauseThenAnswer : Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
            if (boundary == "pause")
            {
                services.AddSingleton(new PauseBoundary { ThrowOnSave = failure });
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
            }
            if (boundary == "authorization")
            {
                services.AddScoped<DefaultAgentContextCollector>();
                services.Replace(ServiceDescriptor.Scoped<IAgentContextCollector>(sp => new ThrowingDisclosureCollector(
                    sp.GetRequiredService<DefaultAgentContextCollector>(), failure)));
            }
        });
        fixture.CaptureDomainFailures = true;
        if (boundary == "assistant")
            fixture.Probe.AfterInput = () => fixture.Services.GetRequiredService<SessionBoundary>().ThrowBeforeAssistantInsert = failure;
        await fixture.Post("Use current authorized evidence");

        var events = await fixture.Stream();

        if (boundary == "unchanged")
        {
            Assert.Empty(fixture.DomainFailures);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        }
        else
        {
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
            var domain = Assert.Single(fixture.DomainFailures);
            Assert.Same(failure, domain.InnerException);
            Assert.Contains(fixture.Session!.Id, domain.Message, StringComparison.Ordinal);
            Assert.Contains("disclosure", domain.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(failure.Message, domain.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Message, JsonSerializer.Serialize(events), StringComparison.Ordinal);
        }
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("pause")]
    [InlineData("authorization")]
    [InlineData("unchanged")]
    public async Task Native_SSE_caller_logs_the_domain_cause_and_sends_only_a_redacted_disclosure_failure(string boundary)
    {
        var failure = new IOException("Synthetic private API infrastructure cause " + Guid.NewGuid().ToString("N"));
        var logger = new RecordingLogger();
        using var fixture = new Fixture(true, boundary == "pause" ? Script.PauseThenAnswer : Script.Answer, services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.AddLogging(builder => builder.AddProvider(new CaptureLoggerProvider(logger)));
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
            if (boundary == "pause")
            {
                services.AddSingleton(new PauseBoundary { ThrowOnSave = failure });
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
            }
            if (boundary == "authorization")
            {
                services.AddScoped<DefaultAgentContextCollector>();
                services.Replace(ServiceDescriptor.Scoped<IAgentContextCollector>(sp => new ThrowingDisclosureCollector(
                    sp.GetRequiredService<DefaultAgentContextCollector>(), failure)));
            }
        });
        if (boundary == "assistant")
            fixture.Probe.AfterInput = () => fixture.Services.GetRequiredService<SessionBoundary>().ThrowBeforeAssistantInsert = failure;
        await fixture.Post("Use the current authorized resources through the native stream");

        var wire = await fixture.StreamViaApi();

        var frames = wire.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToArray();
        if (boundary == "unchanged")
        {
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
            Assert.Contains(frames, frame => frame.GetProperty("kind").GetString() == "completed");
        }
        else
        {
            var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Error);
            var domain = Assert.IsType<AgentContributionException>(entry.Exception);
            Assert.Same(failure, domain.InnerException);
            Assert.Equal(fixture.Session!.Id, entry.Fields["SessionId"]);
            Assert.Equal("stream", entry.Fields["Operation"]);
            Assert.DoesNotContain(failure.Message, domain.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Message, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Message, wire, StringComparison.Ordinal);
            var error = Assert.Single(frames, frame => frame.GetProperty("kind").GetString() == "error").GetProperty("error");
            Assert.Equal("agent.history.provenance_unavailable", error.GetProperty("code").GetString());
            Assert.Equal(503, error.GetProperty("statusCode").GetInt32());
            Assert.DoesNotContain(frames, frame => frame.GetProperty("kind").GetString() == "completed");
        }
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_callback_journal_failure_preserves_its_cause_and_an_accepted_effect_without_replay(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.PauseThenAnswer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        fixture.CaptureDomainFailures = true;
        fixture.Policy = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };
        fixture.Probe.SwallowCallbackCancellation = delegated;
        var failure = new IOException("Synthetic observed-outcome journal outage " + Guid.NewGuid().ToString("N"));
        fixture.Probe.BeforeOperation = () => fixture.Services.GetRequiredService<SessionBoundary>().ThrowBeforeToolInsert = failure;
        await fixture.Post("Perform exactly one allowed owner change");

        var first = await fixture.Stream();

        Assert.Single(fixture.Probe.Inputs);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal("changed", fixture.Mutation.Value);
        var domain = Assert.Single(fixture.DomainFailures);
        Assert.Same(failure, domain.InnerException);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.False(fixture.Probe.AfterCallbackEntered);
        if (delegated)
        {
            Assert.NotNull(fixture.Probe.CallbackCancellation);
            Assert.DoesNotContain(failure.Message, fixture.Probe.CallbackCancellation!.Message, StringComparison.Ordinal);
        }
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        var proposal = Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        var receipt = Assert.IsType<AgentEffectReceipt>((await proposals.FindResultAsync(proposal.Id))?.Receipt);
        Assert.Equal(proposal.PreparedInvocation!.Target, receipt.Before);
        Assert.Equal(fixture.Mutation.Revision, receipt.After.Revision);
        fixture.Source.ShowDetail = false;
        fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Explain only the original resource without another effect");

        AssertDisclosureDenied(await fixture.Stream());

        Assert.Single(fixture.Probe.Inputs);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(receipt, (await proposals.FindResultAsync(proposal.Id))?.Receipt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_cancels_a_cooperative_pre_disclosure_write_without_exposing_the_pending_projection(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.BlockAssistantInsert = true;
        await fixture.Post("Do not disclose the detail before its checkpoint commits");
        var running = fixture.Stream();
        await store.AssistantInsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(fixture.Probe.Inputs);
        Assert.True(fixture.Services.GetRequiredService<IAgentTurnRegistry>().Cancel(fixture.Session!.Id, fixture.Probe.TurnId!));

        var cancelled = await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains(cancelled, item => item.Kind == AgentStreamEventKind.TurnCancelled);
        Assert.DoesNotContain(cancelled, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.Empty(fixture.Probe.Inputs);
        Assert.True(store.AssistantInsertCancelled);
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session.Id));
        store.BlockAssistantInsert = false;
        fixture.Source.ShowDetail = false;
        fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("Use fresh original-resource context after Stop");
        Assert.DoesNotContain(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Error);
        Assert.DoesNotContain(Assert.Single(fixture.Probe.Inputs).Context, item => item.Target?.ResourceId == "detail-resource");
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_persistent_terminal_insert_failure_relies_on_the_verified_checkpoint_and_releases_the_lease(bool withdraw)
    {
        using var fixture = new Fixture(true, Script.Answer, services =>
        {
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        });
        fixture.CaptureDomainFailures = true;
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        var failure = new IOException("Synthetic persistent final journal outage " + Guid.NewGuid().ToString("N"));
        fixture.Probe.AfterInput = () => { if (fixture.Probe.Inputs.Count == 1) store.PersistentAssistantInsertFailure = failure; };
        await fixture.Post("Receive detail before the persistent terminal outage");

        var first = await fixture.Stream();

        Assert.Same(failure, Assert.Single(fixture.DomainFailures).InnerException);
        Assert.Equal(1, store.FailedInserts);
        Assert.DoesNotContain(first, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        store.PersistentAssistantInsertFailure = null;
        fixture.Source.ShowDetail = false;
        if (withdraw) fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        await fixture.Post("The new prompt is accepted rather than held by a leaked lease");
        var second = await fixture.Stream();

        if (withdraw) { Assert.Single(fixture.Probe.Inputs); AssertDisclosureDenied(second); }
        else { Assert.Equal(2, fixture.Probe.Inputs.Count); Assert.Contains(second, item => item.Kind == AgentStreamEventKind.Completed); }
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("status", true, false)]
    [InlineData("remove", true, false)]
    [InlineData("both", true, false)]
    [InlineData("status", false, false)]
    [InlineData("remove", false, false)]
    [InlineData("both", false, false)]
    [InlineData("status", true, true)]
    [InlineData("remove", true, true)]
    [InlineData("both", true, true)]
    [InlineData("status", false, true)]
    [InlineData("remove", false, true)]
    [InlineData("both", false, true)]
    public async Task Cleanup_failures_preserve_all_causes_attempt_both_operations_and_release_the_turn(string boundary, bool primaryFails, bool api)
    {
        var primary = new IOException("Synthetic private primary insert cause " + Guid.NewGuid().ToString("N"));
        var remove = new IOException("Synthetic private pause cleanup cause " + Guid.NewGuid().ToString("N"));
        var status = new IOException("Synthetic private status cleanup cause " + Guid.NewGuid().ToString("N"));
        var logger = new RecordingLogger();
        using var fixture = new Fixture(true, Script.Answer, services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.AddLogging(builder => builder.AddProvider(new CaptureLoggerProvider(logger)));
            services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
            services.AddSingleton<PauseBoundary>();
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
        });
        fixture.CaptureDomainFailures = true;
        var sessions = fixture.Services.GetRequiredService<SessionBoundary>();
        var pauses = fixture.Services.GetRequiredService<PauseBoundary>();
        fixture.Probe.AfterInput = () =>
        {
            if (primaryFails) sessions.ThrowBeforeAssistantInsert = primary;
            if (boundary is "remove" or "both") pauses.ThrowOnRemove = remove;
            if (boundary is "status" or "both") sessions.ThrowOnTerminalStatus = status;
        };
        await fixture.Post("Retain the primary failure even if its cleanup also fails");

        string? wire = null;
        var escaped = await Record.ExceptionAsync(async () =>
        {
            if (api) wire = await fixture.StreamViaApi();
            else await fixture.Stream();
        });

        Assert.Equal(1, pauses.RemoveCalls);
        Assert.Equal(1, sessions.TerminalStatusCalls);
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        Assert.Null(escaped);
        var domain = api
            ? Assert.IsType<AgentContributionException>(Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception)
            : Assert.Single(fixture.DomainFailures);
        var causes = ExceptionChain(domain).ToArray();
        if (primaryFails) Assert.Contains(causes, cause => ReferenceEquals(cause, primary));
        if (boundary is "remove" or "both") Assert.Contains(causes, cause => ReferenceEquals(cause, remove));
        if (boundary is "status" or "both") Assert.Contains(causes, cause => ReferenceEquals(cause, status));
        Assert.Contains(fixture.Session.Id, domain.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic private", domain.Message, StringComparison.Ordinal);
        if (api)
        {
            Assert.Contains("agent.history.provenance_unavailable", wire, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic private", wire, StringComparison.Ordinal);
        }
        Assert.Single(fixture.Probe.Inputs);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        yield return exception;
        if (exception is AggregateException aggregate)
        {
            foreach (var cause in aggregate.InnerExceptions.SelectMany(ExceptionChain)) yield return cause;
        }
        else if (exception.InnerException is { } inner)
        {
            foreach (var cause in ExceptionChain(inner)) yield return cause;
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public async Task A_later_pause_storage_change_cannot_admit_a_provider_or_replay_an_accepted_effect(string mutation)
    {
        using var fixture = new Fixture(true, Script.PauseThenAnswer, services =>
        {
            services.AddSingleton<PauseBoundary>();
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
        });
        fixture.Probe.BeforeOperation = () => fixture.Source.ShowDetail = false;
        await fixture.Post("Apply one reviewed change");
        var first = await fixture.Stream();
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
        var applied = await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision);
        var receipt = Assert.IsType<AgentEffectReceipt>(applied.Value?.ToolResult?.Receipt);
        fixture.Services.GetRequiredService<PauseBoundary>().Mutation = mutation;

        var second = await fixture.Stream();

        if (mutation == "unchanged")
        {
            Assert.Equal(2, fixture.Probe.Inputs.Count);
            Assert.DoesNotContain(second, item => item.Kind == AgentStreamEventKind.Error);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(second, item => item.Error?.Code == "agent.history.provenance_unavailable");
        }
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(receipt, (await proposals.FindResultAsync(proposal.Id))?.Receipt);
    }

    [Fact]
    public void Disclosure_history_snapshots_are_deep_and_preserve_distinct_versions_with_the_same_identity()
    {
        var content = new Dictionary<string, string> { ["value"] = "before" };
        var context = new List<AgentContextAttachment> { HistoryAttachment("same", content) };
        var history = new AgentDisclosureHistory();
        Assert.Null(history.Add(context, []));
        content["value"] = "after";
        Assert.Null(history.Add(context, []));
        context.Clear();
        Assert.Equal(2, history.Snapshots.Count);
        Assert.Equal("before", Assert.IsType<JsonElement>(Assert.Single(history.Snapshots[0].Context).Content).GetProperty("value").GetString());
        Assert.Equal("after", Assert.IsType<JsonElement>(Assert.Single(history.Snapshots[1].Context).Content).GetProperty("value").GetString());
    }

    [Fact]
    public void Count_bound_refuses_an_additional_snapshot_without_discarding_any_prior_dependency()
    {
        var history = new AgentDisclosureHistory();
        for (var index = 0; index < AgentDisclosureHistory.MaximumSnapshots; index++)
            Assert.Null(history.Add([HistoryAttachment("same", new { index })], []));
        var before = AgentInvocationIntegrity.Digest(history.Snapshots);
        Assert.Equal("agent.context.disclosure_budget_exceeded", history.Add([HistoryAttachment("same", new { index = 65 })], [])?.Code);
        Assert.Equal(AgentDisclosureHistory.MaximumSnapshots, history.Snapshots.Count);
        Assert.Equal(before, AgentInvocationIntegrity.Digest(history.Snapshots));
        Assert.Null(history.Add([HistoryAttachment("same", new { index = 0 })], []));
    }

    [Fact]
    public void Byte_bound_refuses_complete_new_evidence_without_evicting_the_retained_history()
    {
        var history = new AgentDisclosureHistory();
        Assert.Null(history.Add([HistoryAttachment("first", new { value = "retained" })], []));
        var before = AgentInvocationIntegrity.Digest(history.Snapshots);
        Assert.Equal("agent.context.disclosure_budget_exceeded", history.Add(
            [HistoryAttachment("too-large", new { value = new string('x', AgentDisclosureHistory.MaximumBytes) })], [])?.Code);
        Assert.Single(history.Snapshots);
        Assert.Equal(before, AgentInvocationIntegrity.Digest(history.Snapshots));
    }

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    public async Task A_new_projection_exceeding_the_retained_bound_is_refused_before_provider_entry(int retained, bool exceeds)
    {
        using var fixture = new Fixture(true, Script.Answer);
        await fixture.Post("An earlier authorized request");
        var sessions = fixture.Services.GetRequiredService<IAgentSessionService>();
        var original = Assert.Single(await sessions.ListMessagesAsync(fixture.Session!.Id));
        await sessions.SetMessageStatusAsync(fixture.Session.Id, original.Id, AgentMessageStatus.Completed);
        var attachment = Assert.Single(original.Context);
        var history = AgentDisclosureHistory.Freeze(Enumerable.Range(0, retained).Select(index => new AgentDisclosureSnapshot(
            [index == 0 ? attachment : attachment with { Content = new { snapshot = index } }], [])));
        const string priorAnswer = "The authorized earlier snapshots were observed.";
        await sessions.AddMessageAsync(fixture.Session.Id, new(AgentRole.Assistant, priorAnswer, AgentMessageStatus.Completed, null, [], [])
        {
            HasDisclosureProvenance = true, DisclosureHistory = history,
            DeliveryFingerprint = AgentDisclosureHistory.MessageFingerprint(priorAnswer, [], [], history)
        });
        await fixture.Post("Request the current expanded projection");

        var events = await fixture.Stream();

        if (exceeds)
        {
            Assert.Empty(fixture.Probe.Inputs);
            Assert.Contains(events, item => item.Error?.Code == "agent.context.disclosure_budget_exceeded");
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
        }
        else
        {
            Assert.Single(fixture.Probe.Inputs);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        }
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    private static AgentContextAttachment HistoryAttachment(string id, object content)
        => new(id, Module, "resource", "Resource", "cumulative.resource", AgentContextSensitivity.Internal, "selection", "Evidence", content,
            new Dictionary<string, string>()) { ContributorId = SourceId, ModuleId = Module };

    private static void AssertDisclosureDenied(IEnumerable<AgentStreamEvent> events)
    {
        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error
            && item.Error?.Code == "agent.context.incomplete");
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
    }

    private enum Script { Answer, ReadThenAnswer, PauseThenAnswer }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal ProjectionSource Source { get; } = new();
        internal NeutralReadTool Read { get; } = new();
        internal ProposalMutationTool Mutation { get; } = new();
        internal ProbeState Probe { get; }
        internal AgentSession? Session { get; private set; }
        internal AgentPolicy Policy { get; set; } = AgentPolicy.Default;
        internal bool CaptureDomainFailures;
        internal List<AgentContributionException> DomainFailures { get; } = [];
        internal IServiceProvider Services => scope.ServiceProvider;

        internal Fixture(bool delegated, Script script, Action<IServiceCollection>? configure = null)
        {
            Probe = new(script);
            root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentContextProvider>(Source);
                services.AddSingleton<IAgentTool>(Read);
                services.AddSingleton<IAgentTool>(Mutation);
                if (delegated) services.AddSingleton<IAgentProvider>(new DelegatedProbe(Probe));
                else services.AddSingleton<IAgentProvider>(new HostProbe(Probe));
                configure?.Invoke(services);
            });
            scope = root.CreateScope();
        }

        internal async Task Post(string text)
        {
            var sessions = Services.GetRequiredService<IAgentSessionService>();
            Session ??= await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "cumulative-probe", "explain", null,
                Policy, new Dictionary<string, string>()));
            var collected = await Services.GetRequiredService<IAgentContextCollector>().CollectAsync(Session.Policy,
                new(Session.Id, SourceId, new Dictionary<string, string>()));
            Assert.True(collected.Succeeded, collected.Error?.Message);
            var context = collected.Value!.Attachments;
            await sessions.AddMessageAsync(Session.Id, new(AgentRole.User, text, AgentMessageStatus.Pending, null,
                context.Select(item => item.Id).ToArray(), context) { HasDisclosureProvenance = true });
        }

        internal async Task<List<AgentStreamEvent>> Stream()
        {
            var events = new List<AgentStreamEvent>();
            try
            {
                await foreach (var item in Services.GetRequiredService<IAgentStreamingService>().StreamAsync(Session!.Id))
                {
                    if (item.Payload is AgentTurnStarted started) Probe.TurnId = started.TurnId;
                    events.Add(item);
                }
            }
            catch (AgentContributionException failure) when (CaptureDomainFailures) { DomainFailures.Add(failure); }
            return events;
        }

        internal async Task<string> StreamViaApi()
        {
            var app = new TestEndpointRouteBuilder(Services);
            AgentApi.MapAgentApi(app);
            var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
                item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsStreamSession");
            var context = new DefaultHttpContext
            {
                RequestServices = Services,
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "actor-1"), new Claim("elsa.identity.tenant_id", "tenant-1")], "test"))
            };
            context.Request.RouteValues["sessionId"] = Session!.Id;
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class ProjectionSource : IAgentContextProvider
    {
        internal bool ShowDetail = true;
        internal bool AddSection, RefreshPayload;
        private int payloadRevision;
        internal string? BindingDrift;
        internal string DetailRevision = "detail-v1";
        internal HashSet<string> AllowedDetailRevisions { get; } = ["detail-v1", "detail-v2"];
        public AgentContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["cumulative.resource"], ["1"], ["resource"]);
        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution([Attachment("original", "original-resource", "original-v1")], []));
        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Context.Any(item => item.Target?.ResourceId == "detail-resource" && !AllowedDetailRevisions.Contains(item.Target.Revision!)))
                return ValueTask.FromResult(new AgentContextContribution([], [new(SourceId, "cumulative.read_denied", "Current owner authority withholds this retained detail resource.", true)]));
            if (request.IsHistorical) return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
            var current = new List<AgentContextAttachment> { Attachment("original", "original-resource", "original-v1") };
            if (ShowDetail) current.Add(Attachment("detail", "detail-resource", DetailRevision));
            if (AddSection) current.Add(Attachment("section", "section-resource", "section-v1"));
            if (BindingDrift == "scopes") current = current.Select(item => item with { AgentScopes = ["new-scope"] }).ToList();
            if (BindingDrift == "revision") current = current.Select(item => item.Target?.ResourceId == "detail-resource"
                ? item with { Target = item.Target with { Revision = "detail-v2" } } : item).ToList();
            if (BindingDrift == "target") current.Add(Attachment("another", "another-resource", "another-v1"));
            if (RefreshPayload)
            {
                var value = ++payloadRevision;
                current = current.Select(item => item with { Summary = "Authorized summary " + value, Content = new { timestamp = value } }).ToList();
            }
            return ValueTask.FromResult(new AgentContextContribution(current, []));
        }
        internal void SetAuthority(string authority)
        {
            if (authority is "withdrawn" or "restored") AllowedDetailRevisions.Remove("detail-v1");
            if (authority == "restored") AllowedDetailRevisions.Add("detail-v1");
        }
        private static AgentContextAttachment Attachment(string id, string resource, string revision)
            => new(id, Module, resource, "Owned resource", "cumulative.resource", AgentContextSensitivity.Internal, "selection",
                resource + " " + revision, new { resource, revision }, new Dictionary<string, string>())
            { ModuleId = Module, Target = new(Module, "resource", resource, revision) };
    }

    private sealed record Input(IReadOnlyCollection<AgentContextAttachment> Context, IReadOnlyList<AgentTurnMessage> History,
        IReadOnlyList<AgentToolResult> Results, IReadOnlyCollection<string> Tools);

    private sealed class ProbeState(Script script)
    {
        internal List<Input> Inputs { get; } = [];
        internal Action? BeforeOperation;
        internal Action? AfterInput;
        internal bool Operated, AfterCallbackEntered;
        internal string? TextlessTerminal, TurnId;
        internal Action? Cancel;
        internal bool SwallowCallbackCancellation;
        internal OperationCanceledException? CallbackCancellation;
        internal bool NeedsOperation => !Operated && script != Script.Answer;
        internal string ToolName => script == Script.PauseThenAnswer ? "test_mutate" : "cumulative_neutral_read";
    }

    private sealed class NeutralReadTool : IAgentTool
    {
        internal int Preparations, Executions;
        public AgentToolDescriptor Descriptor { get; } = new("cumulative_neutral_read", "Read another owned resource", "A neutral owner read.",
            AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, "{}", [], [])
        { ModuleId = "test.module", DisclosureSourceId = AgentTestContextSource.TestModuleSourceId, ResultType = "neutral.result", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(null, JsonSerializer.SerializeToElement(invocation.Arguments),
                AgentRisk.ReadOnly, new("neutral.preview", "1", JsonSerializer.SerializeToElement(new { read = true })), false)));
        }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "The neutral resource was read without changes.",
                new("neutral.result", "1", JsonSerializer.SerializeToElement(new { read = true })))));
        }
    }

    private class HostProbe(ProbeState state) : IAgentProvider
    {
        protected ProbeState State { get; } = state;
        public string ProviderId => "cumulative-probe";
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat, AgentProviderOperation.Streaming], AgentProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            State.Inputs.Add(new(context.Context, context.History, context.PendingToolResults, context.AvailableTools.Select(item => item.Name).ToArray()));
            State.AfterInput?.Invoke();
            if (State.TextlessTerminal is { } terminal)
            {
                if (terminal == "cancelled") { State.Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (terminal == "error") yield return new("textless-error", AgentStreamEventKind.Error, null, null, new("probe.error", "The provider stopped without text.", 503), DateTimeOffset.UtcNow);
                yield break;
            }
            yield return Text(DerivedText);
            if (State.NeedsOperation)
            {
                State.Operated = true; State.BeforeOperation?.Invoke();
                yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, null, null, DateTimeOffset.UtcNow,
                    Payload: new AgentToolCallRequest("cumulative-call", State.ToolName, "{\"value\":\"changed\"}", AgentRisk.ReadOnly, false));
            }
        }
        protected static AgentStreamEvent Text(string text) => new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, text, null, null, DateTimeOffset.UtcNow);
    }

    private sealed class DelegatedProbe(ProbeState state) : HostProbe(state), IAgentHarness
    {
        public AgentHarnessCapabilities Capabilities => AgentHarnessCapabilities.Tools;
        public async IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            State.Inputs.Add(new(request.Context, request.History, request.PendingToolResults, request.Tools.Select(item => item.Descriptor.Name).ToArray()));
            State.AfterInput?.Invoke();
            if (State.TextlessTerminal is { } terminal)
            {
                if (terminal == "cancelled") { State.Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (terminal == "error") yield return new("textless-error", AgentStreamEventKind.Error, null, null, new("probe.error", "The provider stopped without text.", 503), DateTimeOffset.UtcNow);
                yield break;
            }
            yield return Text(DerivedText);
            if (!State.NeedsOperation) yield break;
            State.Operated = true; State.BeforeOperation?.Invoke();
            AgentToolDispatch? dispatch = null;
            try
            {
                dispatch = await request.ToolInvoker!.DispatchAsync(new("cumulative-call", State.ToolName, request.SessionId, request.ActorId,
                    new Dictionary<string, object?> { ["value"] = "changed" }, request.Context)
                { TenantId = request.TenantId, TurnId = request.TurnId }, request.Policy, cancellationToken);
            }
            catch (OperationCanceledException stopped) { State.CallbackCancellation = stopped; }
            if (State.CallbackCancellation is { } cancellation)
            {
                if (State.SwallowCallbackCancellation) yield break;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
            if (dispatch!.Proposal is { } proposal)
            {
                yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, Payload: proposal);
                yield break;
            }
            State.AfterCallbackEntered = true;
            yield return Text("The delegated model received the read result after its callback.");
        }
    }

    private static IReadOnlyList<AgentDisclosureSnapshot>? ChangeHistory(IReadOnlyList<AgentDisclosureSnapshot>? history, string? mutation)
        => mutation switch
        {
            "missing" => null,
            "truncated" => [],
            "changed" => history!.Select(snapshot => snapshot with
            { Context = snapshot.Context.Select(item => item with { Summary = "Changed stored provenance" }).ToArray() }).ToArray(),
            _ => history
        };

    private sealed class PauseBoundary : IAgentTurnStateStore
    {
        private AgentPausedTurn? state;
        internal string? Mutation;
        internal Exception? ThrowOnSave;
        internal Exception? ThrowOnRemove;
        internal Exception? ThrowOnFind;
        internal int RemoveCalls;
        public void Save(AgentPausedTurn value)
        {
            if (ThrowOnSave is { } failure) throw failure;
            state = value;
        }
        public AgentPausedTurn? Find(string sessionId)
        {
            if (ThrowOnFind is { } failure) throw failure;
            return state is not null && state.SessionId == sessionId
                ? state with { DisclosureHistory = ChangeHistory(state.DisclosureHistory, Mutation) } : null;
        }
        public void Remove(string sessionId)
        {
            RemoveCalls++;
            if (ThrowOnRemove is { } failure) throw failure;
            if (state?.SessionId == sessionId) state = null;
        }
    }

    private sealed class SessionBoundary(IAgentSessionService inner) : IAgentSessionService
    {
        internal string? Mutation;
        internal Exception? ThrowBeforeAssistantInsert;
        internal Exception? ThrowBeforeToolInsert;
        internal Exception? PersistentAssistantInsertFailure;
        internal Exception? ThrowOnTerminalStatus;
        internal Exception? ThrowOnStreamingStatus, ThrowOnFind, ThrowOnList;
        internal bool AsyncFailures;
        internal int AssistantInsertCalls, HoldAssistantAt;
        internal TaskCompletionSource SuccessfulWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSuccessfulWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int TerminalStatusCalls;
        internal bool BlockAssistantInsert, AssistantInsertCancelled;
        internal TaskCompletionSource AssistantInsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int FailedInserts;
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            => ThrowOnFind is { } failure ? Failure<AgentSession?>(failure) : inner.FindAsync(sessionId, cancellationToken);
        private Task<T> Failure<T>(Exception cause)
        {
            if (AsyncFailures) return Task.FromException<T>(cause);
            throw cause;
        }
        public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Role == AgentRole.Tool && ThrowBeforeToolInsert is { } failure)
            {
                ThrowBeforeToolInsert = null; FailedInserts++;
                return Failure<AgentMessage>(failure);
            }
            return AddCoreAsync(sessionId, request, cancellationToken);
        }
        private async Task<AgentMessage> AddCoreAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken)
        {
            if (request.Role == AgentRole.Assistant && ++AssistantInsertCalls == HoldAssistantAt)
            {
                SuccessfulWriteStarted.TrySetResult();
                await ReleaseSuccessfulWrite.Task.WaitAsync(cancellationToken);
            }
            if (request.Role == AgentRole.Assistant && BlockAssistantInsert)
            {
                AssistantInsertStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { AssistantInsertCancelled = true; throw; }
            }
            if (request.Role == AgentRole.Assistant && (ThrowBeforeAssistantInsert ?? PersistentAssistantInsertFailure) is { } failure)
            {
                ThrowBeforeAssistantInsert = null; FailedInserts++;
                throw failure;
            }
            return Project(await inner.AddMessageAsync(sessionId, request, cancellationToken));
        }
        public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
            => ThrowOnList is { } failure ? Failure<IReadOnlyCollection<AgentMessage>>(failure) : ListCoreAsync(sessionId, cancellationToken);
        private async Task<IReadOnlyCollection<AgentMessage>> ListCoreAsync(string sessionId, CancellationToken cancellationToken)
            => (await inner.ListMessagesAsync(sessionId, cancellationToken)).Select(Project).ToArray();
        public async Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        { var message = await inner.FindMessageAsync(sessionId, messageId, cancellationToken); return message is null ? null : Project(message); }
        public async Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
        { var message = await inner.FindLatestMessageAsync(sessionId, role, cancellationToken); return message is null ? null : Project(message); }
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
        {
            if (status == AgentMessageStatus.Streaming && ThrowOnStreamingStatus is { } streamingFailure)
                return Failure<object?>(streamingFailure);
            if (status is AgentMessageStatus.Completed or AgentMessageStatus.Cancelled or AgentMessageStatus.Failed)
            {
                TerminalStatusCalls++;
                if (ThrowOnTerminalStatus is { } failure) throw failure;
            }
            return inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        }
        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => inner.AddContextAsync(sessionId, attachments, cancellationToken);
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
            => inner.ListContextAsync(sessionId, cancellationToken);
        private AgentMessage Project(AgentMessage message) => message.Role == AgentRole.Assistant
            ? message with { DisclosureHistory = ChangeHistory(message.DisclosureHistory, Mutation) } : message;
    }

    private sealed class ThrowingDisclosureCollector(IAgentContextCollector inner, Exception failure) : IAgentContextCollector
    {
        public Task<AgentResult<AgentContextContribution>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default)
            => request.Disclosure?.IsHistorical == true ? throw failure : inner.CollectAsync(policy, request, cancellationToken);
    }

    private sealed class CaptureLoggerProvider(RecordingLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
