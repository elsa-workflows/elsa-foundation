using System.Security.Claims;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.Api.Endpoints;
using Elsa.Weaver.Api.Models;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

public sealed class WeaverPolicyTests
{
    [Fact]
    public void Default_policy_opens_on_auto_read_only_but_offers_autopilot_up_to_the_ceiling()
    {
        var policy = WeaverPolicy.Default;

        // A new session opens on the safe default, while the ceiling makes Autopilot (FullAuto) selectable.
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, policy.AutonomyMode);
        Assert.Equal(WeaverAutonomyMode.FullAuto, policy.MaxAutonomyMode);
        Assert.True(policy.RequiresApprovalForMutations);
        Assert.Equal([WeaverAutonomyMode.Manual, WeaverAutonomyMode.AutoReadOnly, WeaverAutonomyMode.FullAuto], policy.AllowedAutonomyModes);
    }

    [Fact]
    public void Full_auto_does_not_require_approval_for_mutations()
    {
        var policy = WeaverPolicy.Default with { AutonomyMode = WeaverAutonomyMode.FullAuto, MaxAutonomyMode = WeaverAutonomyMode.FullAuto };

        Assert.False(policy.RequiresApprovalForMutations);
        Assert.Equal([WeaverAutonomyMode.Manual, WeaverAutonomyMode.AutoReadOnly, WeaverAutonomyMode.FullAuto], policy.AllowedAutonomyModes);
    }

    [Theory]
    [InlineData(WeaverAutonomyMode.FullAuto, WeaverAutonomyMode.AutoReadOnly, WeaverAutonomyMode.AutoReadOnly)] // request above ceiling is clamped down
    [InlineData(WeaverAutonomyMode.Manual, WeaverAutonomyMode.AutoReadOnly, WeaverAutonomyMode.Manual)] // request below ceiling is honored
    [InlineData(WeaverAutonomyMode.FullAuto, WeaverAutonomyMode.FullAuto, WeaverAutonomyMode.FullAuto)] // request at ceiling is honored
    public void Clamp_caps_requested_mode_at_the_ceiling(WeaverAutonomyMode requested, WeaverAutonomyMode ceiling, WeaverAutonomyMode expected)
    {
        var policy = WeaverPolicy.Default with { MaxAutonomyMode = ceiling };

        Assert.Equal(expected, policy.Clamp(requested));
    }

    [Theory]
    [InlineData("manual", WeaverAutonomyMode.Manual)]
    [InlineData("auto-read-only", WeaverAutonomyMode.AutoReadOnly)]
    [InlineData("full-auto", WeaverAutonomyMode.FullAuto)]
    [InlineData("FULL-AUTO", WeaverAutonomyMode.FullAuto)]
    public void Autonomy_mode_round_trips_through_the_wire_contract(string wire, WeaverAutonomyMode mode)
    {
        Assert.Equal(mode, WeaverApiMapping.ParseAutonomyMode(wire));
        Assert.Equal(wire.ToLowerInvariant(), mode.ToContractString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Unrecognized_autonomy_mode_parses_to_null(string? wire)
        => Assert.Null(WeaverApiMapping.ParseAutonomyMode(wire));

    [Fact]
    public async Task Policy_denies_requests_when_agent_is_disabled()
    {
        var evaluator = new DefaultWeaverPolicyEvaluator();
        var decision = await evaluator.EvaluateAvailabilityAsync(WeaverPolicy.Default with { Enabled = false });

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "weaver.disabled");
    }

    [Fact]
    public async Task Policy_denies_context_that_exceeds_allowed_sensitivity()
    {
        var evaluator = new DefaultWeaverPolicyEvaluator();
        var policy = WeaverPolicy.Default with
        {
            MaxContextSensitivity = WeaverContextSensitivity.Internal,
            AllowedContextKinds = ["workflow.definition"]
        };

        var decision = await evaluator.EvaluateContextAsync(policy,
        [
            CreateAttachment("workflow", "workflow-definition", WeaverContextSensitivity.Secret)
        ]);

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "weaver.context.sensitivity_denied");
    }

    [Fact]
    public async Task Policy_denies_context_kind_that_is_not_allowlisted()
    {
        var evaluator = new DefaultWeaverPolicyEvaluator();
        var policy = WeaverPolicy.Default with { AllowedContextKinds = ["workflow.definition"] };

        var decision = await evaluator.EvaluateContextAsync(policy,
        [
            CreateAttachment("settings", "configuration-secret", WeaverContextSensitivity.Internal)
        ]);

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "weaver.context.kind_denied");
    }

    [Fact]
    public async Task Sanitizer_redacts_secret_context_before_policy_evaluation()
    {
        var sanitizer = new DefaultWeaverContextSanitizer();

        var attachments = await sanitizer.SanitizeAsync(
        [
            CreateAttachment("workflow", "workflow-definition", WeaverContextSensitivity.Secret, content: new { token = "secret" })
        ]);

        var attachment = Assert.Single(attachments);
        Assert.Equal(WeaverContextSensitivity.SecretRedacted, attachment.Sensitivity);
        Assert.Null(attachment.Content);
        Assert.All(attachment.References.Values, value => Assert.Equal("[redacted]", value));
    }

    [Fact]
    public void Agent_endpoint_actor_resolves_claim_owned_actor_and_tenant()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim("elsa.identity.tenant_id", "tenant-1")
        ], "test"));

        Assert.Equal("user-1", WeaverEndpointActor.Resolve(principal));
        Assert.Equal("tenant-1", WeaverEndpointActor.ResolveTenant(principal));
        Assert.True(WeaverEndpointActor.CanAccess("user-1", "tenant-1", principal));
        Assert.False(WeaverEndpointActor.CanAccess("user-2", "tenant-1", principal));
        Assert.False(WeaverEndpointActor.CanAccess("user-1", "tenant-2", principal));
    }

    [Fact]
    public void Agent_endpoint_actor_fails_closed_for_claimless_principals()
    {
        // Issue #374: two distinct principals that both lack the identity/tenant claims must not
        // collide into a shared "anonymous"/"default" identity that grants cross-actor access.
        var claimlessA = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "a")], "test"));
        var claimlessB = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "b")], "test"));

        Assert.Null(WeaverEndpointActor.Resolve(claimlessA));
        Assert.Null(WeaverEndpointActor.ResolveTenant(claimlessA));

        // A session created by one claim-less principal is not accessible to another (nor to itself).
        Assert.False(WeaverEndpointActor.CanAccess("anonymous", "default", claimlessA));
        Assert.False(WeaverEndpointActor.CanAccess("anonymous", "default", claimlessB));

        // A principal with an actor but no tenant claim still fails closed.
        var actorOnly = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        Assert.Null(WeaverEndpointActor.ResolveTenant(actorOnly));
        Assert.False(WeaverEndpointActor.CanAccess("user-1", "tenant-1", actorOnly));
    }

    private static WeaverContextAttachment CreateAttachment(string source, string contentType, WeaverContextSensitivity sensitivity, object? content = null)
        => new(
            "ctx-1",
            source,
            "source-1",
            "Context",
            contentType,
            sensitivity,
            "selection",
            "Context summary.",
            content,
            new Dictionary<string, string> { ["ref"] = "value" });
}
