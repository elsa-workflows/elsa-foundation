using System.Text.Json;
using System.Security.Claims;
using Elsa.Activities.Bpmn.Api.Endpoints;
using Elsa.Activities.Bpmn.Core;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_starts_capture_canonical_subject_but_replay_preserves_original_authority(bool capture)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("external-actor", null) { ExecutionSubjectId = capture ? "persisted-user-a" : null };
        var published = await runtime.PublishAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, "subject-publication", caller);
        var started = await runtime.StartAsync(published.SourceReferenceId, "subject-start", null, caller);
        var state = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        Assert.Equal("bpmn-runtime", state.Authority.SystemIdentity);
        Assert.Equal("external-actor", state.Authority.RootInitiator);
        if (capture)
        {
            Assert.Equal("persisted-user", state.Authority.Metadata["elsa.identity.subject-kind"]);
            Assert.Equal("persisted-user-a", state.Authority.Metadata["elsa.identity.subject-id"]);
            Assert.Equal(2, state.Authority.Metadata.Count);
        }
        else Assert.Empty(state.Authority.Metadata);
        var replay = await runtime.StartAsync(published.SourceReferenceId, "subject-start", null, caller with { ExecutionSubjectId = "later-user" });
        Assert.True(replay.Replay);
        var retained = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(state.Authority, Web), System.Text.Json.JsonSerializer.Serialize(retained.Authority, Web));
    }

    [Theory]
    [InlineData("missing", null)]
    [InlineData("blank", null)]
    [InlineData("duplicate", null)]
    [InlineData("canonical", "persisted-user")]
    public void Api_caller_captures_only_one_nonblank_canonical_subject_without_reinterpreting_audit_actor(string mode, string? expected)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaimTypes.Normalized, "v1"), new(ClaimTypes.NameIdentifier, "external-actor"),
            new("sub", "external-actor"), new(IdentityClaimTypes.Permission, "never-snapshotted")
        };
        if (mode != "missing") claims.Add(new(IdentityClaimTypes.UserId, mode == "blank" ? " " : "persisted-user"));
        if (mode == "duplicate") claims.Add(new(IdentityClaimTypes.UserId, "persisted-user"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "trusted"));
        // A second untrusted identity must never supply the canonical execution subject.
        principal.AddIdentity(new ClaimsIdentity([new Claim(IdentityClaimTypes.UserId, "forged-user")], "untrusted"));
        var contexts = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var validator = new NormalizedPrincipalValidator(Options.Create(new FoundationIdentityOptions
        { NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "trusted" } }));
        var captured = new BpmnApiCaller(contexts, validator, null!, null!).Current();
        Assert.Equal("external-actor", captured.ActorId);
        Assert.Equal(expected, captured.ExecutionSubjectId);
    }
}
