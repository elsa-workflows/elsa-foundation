using System.Security.Claims;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Api.Models;

namespace Elsa.Agent.Tests;

public sealed class AgentPolicyTests
{
    [Fact]
    public void Default_policy_gates_mutations_with_auto_read_only_ceiling()
    {
        var policy = AgentPolicy.Default;

        Assert.Equal(AgentAutonomyMode.AutoReadOnly, policy.AutonomyMode);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, policy.MaxAutonomyMode);
        Assert.True(policy.RequiresApprovalForMutations);
        Assert.Equal([AgentAutonomyMode.Manual, AgentAutonomyMode.AutoReadOnly], policy.AllowedAutonomyModes);
    }

    [Fact]
    public void Full_auto_does_not_require_approval_for_mutations()
    {
        var policy = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto, MaxAutonomyMode = AgentAutonomyMode.FullAuto };

        Assert.False(policy.RequiresApprovalForMutations);
        Assert.Equal([AgentAutonomyMode.Manual, AgentAutonomyMode.AutoReadOnly, AgentAutonomyMode.FullAuto], policy.AllowedAutonomyModes);
    }

    [Theory]
    [InlineData(AgentAutonomyMode.FullAuto, AgentAutonomyMode.AutoReadOnly, AgentAutonomyMode.AutoReadOnly)] // request above ceiling is clamped down
    [InlineData(AgentAutonomyMode.Manual, AgentAutonomyMode.AutoReadOnly, AgentAutonomyMode.Manual)] // request below ceiling is honored
    [InlineData(AgentAutonomyMode.FullAuto, AgentAutonomyMode.FullAuto, AgentAutonomyMode.FullAuto)] // request at ceiling is honored
    public void Clamp_caps_requested_mode_at_the_ceiling(AgentAutonomyMode requested, AgentAutonomyMode ceiling, AgentAutonomyMode expected)
    {
        var policy = AgentPolicy.Default with { MaxAutonomyMode = ceiling };

        Assert.Equal(expected, policy.Clamp(requested));
    }

    [Theory]
    [InlineData("manual", AgentAutonomyMode.Manual)]
    [InlineData("auto-read-only", AgentAutonomyMode.AutoReadOnly)]
    [InlineData("full-auto", AgentAutonomyMode.FullAuto)]
    [InlineData("FULL-AUTO", AgentAutonomyMode.FullAuto)]
    public void Autonomy_mode_round_trips_through_the_wire_contract(string wire, AgentAutonomyMode mode)
    {
        Assert.Equal(mode, AgentApiMapping.ParseAutonomyMode(wire));
        Assert.Equal(wire.ToLowerInvariant(), mode.ToContractString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Unrecognized_autonomy_mode_parses_to_null(string? wire)
        => Assert.Null(AgentApiMapping.ParseAutonomyMode(wire));

    [Fact]
    public async Task Policy_denies_requests_when_agent_is_disabled()
    {
        var evaluator = new DefaultAgentPolicyEvaluator();
        var decision = await evaluator.EvaluateAvailabilityAsync(AgentPolicy.Default with { Enabled = false });

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "agent.disabled");
    }

    [Fact]
    public async Task Policy_denies_context_that_exceeds_allowed_sensitivity()
    {
        var evaluator = new DefaultAgentPolicyEvaluator();
        var policy = AgentPolicy.Default with
        {
            MaxContextSensitivity = AgentContextSensitivity.Internal,
            AllowedContextKinds = ["workflow.definition"]
        };

        var decision = await evaluator.EvaluateContextAsync(policy,
        [
            CreateAttachment("workflow", "workflow-definition", AgentContextSensitivity.Secret)
        ]);

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "agent.context.sensitivity_denied");
    }

    [Fact]
    public async Task Policy_denies_context_kind_that_is_not_allowlisted()
    {
        var evaluator = new DefaultAgentPolicyEvaluator();
        var policy = AgentPolicy.Default with { AllowedContextKinds = ["workflow.definition"] };

        var decision = await evaluator.EvaluateContextAsync(policy,
        [
            CreateAttachment("settings", "configuration-secret", AgentContextSensitivity.Internal)
        ]);

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Violations, x => x.Code == "agent.context.kind_denied");
    }

    [Fact]
    public async Task Sanitizer_redacts_secret_context_before_policy_evaluation()
    {
        var sanitizer = new DefaultAgentContextSanitizer();

        var attachments = await sanitizer.SanitizeAsync(
        [
            CreateAttachment("workflow", "workflow-definition", AgentContextSensitivity.Secret, content: new { token = "secret" })
        ]);

        var attachment = Assert.Single(attachments);
        Assert.Equal(AgentContextSensitivity.SecretRedacted, attachment.Sensitivity);
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

        Assert.Equal("user-1", AgentEndpointActor.Resolve(principal));
        Assert.Equal("tenant-1", AgentEndpointActor.ResolveTenant(principal));
        Assert.True(AgentEndpointActor.CanAccess("user-1", "tenant-1", principal));
        Assert.False(AgentEndpointActor.CanAccess("user-2", "tenant-1", principal));
        Assert.False(AgentEndpointActor.CanAccess("user-1", "tenant-2", principal));
    }

    private static AgentContextAttachment CreateAttachment(string source, string contentType, AgentContextSensitivity sensitivity, object? content = null)
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
