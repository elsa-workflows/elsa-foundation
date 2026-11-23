using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class StandaloneHttpAuthoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("http-authoring"), "tenant");

    [Fact]
    public void Repeated_authoring_registration_resolves_one_descriptor_validator_and_planner_without_an_ingress()
    {
        var services = new ServiceCollection(); var feature = new StandaloneHttpTriggerAuthoringFeature();
        feature.ConfigureServices(services); feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var type = Assert.Single(Assert.Single(scope.ServiceProvider.GetServices<IStandaloneTriggerTypeSource>()).GetTypes());
        Assert.Equal("http", type.TypeKey);
        Assert.Equal("http", Assert.Single(scope.ServiceProvider.GetServices<ITriggerConfigurationValidator>()).TypeKey);
        Assert.Equal("http", Assert.Single(scope.ServiceProvider.GetServices<ITriggerReservationPlanner>()).TypeKey);
        Assert.Empty(scope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>());
        Assert.Empty(scope.ServiceProvider.GetServices<IStandaloneTriggerIngress<StandaloneHttpTriggerSource>>());
    }

    [Theory]
    [InlineData("valid", null)] [InlineData("version", "UnsupportedSchema")]
    [InlineData("slash", "InvalidRoute")] [InlineData("slashes", "InvalidRoute")]
    [InlineData("method", "InvalidMethod")] [InlineData("source", "InvalidSourceAccess")]
    [InlineData("mode", "ResponseModeRequired")] [InlineData("policy", "InvalidAdmissionPolicy")]
    [InlineData("response-policy", "UnexpectedAdmissionPolicy")]
    public async Task Registered_validation_reports_unsupported_and_invalid_configuration_before_planning(string defect, string? code)
    {
        using var provider = Provider(); using var scope = provider.CreateScope();
        var node = JsonNode.Parse(Configuration().GetRawText())!;
        if (defect == "slash") node["path"] = "/";
        if (defect == "slashes") node["path"] = " /// ";
        if (defect == "method") node["method"] = "TRACE";
        if (defect == "source") node["sourceAccess"] = "Guess";
        if (defect == "mode") node["http"]!["mode"] = "Guess";
        if (defect == "policy") node["http"]!["admissionPolicy"] = "Guess";
        if (defect == "response-policy") { node["http"]!["mode"] = "WorkflowResponse"; node["http"]!["admissionPolicy"] = "RequireAll"; }
        var findings = await scope.ServiceProvider.GetRequiredService<ITriggerConfigurationValidator>()
            .ValidateAsync(defect == "version" ? 2 : 1, JsonSerializer.SerializeToElement(node));
        if (code is null) Assert.Empty(findings);
        else Assert.Contains(findings, item => item.Code == code);
    }

    [Theory]
    [InlineData(TriggerDesiredState.Paused, false, false, false)]
    [InlineData(TriggerDesiredState.Enabled, false, false, true)]
    [InlineData(TriggerDesiredState.Paused, true, false, true)]
    [InlineData(TriggerDesiredState.Paused, false, true, true)]
    [InlineData(TriggerDesiredState.Deleted, true, true, false)]
    public async Task Reservation_lifecycle_retains_activated_paused_routes_and_releases_deleted_routes(
        TriggerDesiredState state, bool activated, bool previouslyActivated, bool reserved)
    {
        using var provider = Provider(); using var scope = provider.CreateScope();
        var trigger = Trigger(state, activated); var previous = previouslyActivated ? new TriggerAggregate(Trigger(TriggerDesiredState.Enabled, true), []) : null;
        var claims = await scope.ServiceProvider.GetRequiredService<ITriggerReservationPlanner>().PlanAsync(Scope, previous,
            new("request", "fingerprint", "operator", null, trigger, []));
        if (!reserved) { Assert.Empty(claims); return; }
        var claim = Assert.Single(claims, item => item.StimulusType == HttpEndpointRouting.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "POST"), claim.StimulusHash);
        Assert.Equal(bool.TrueString, claim.Metadata[HttpEndpointRouting.AuthorizeMetadataKey]);
        Assert.Equal("RequireAll", claim.Metadata["http:admissionPolicy"]);
        Assert.Equal(state.ToString(), claim.Metadata["http:triggerState"]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Workflow_response_reservation_requires_one_enabled_subscription(int enabled)
    {
        using var provider = Provider(); using var scope = provider.CreateScope();
        var subscriptions = Enumerable.Range(0, enabled + 1).Select(index => new TriggerSubscription("subscription-" + index, "trigger",
            "definition-" + index, "Published", "native", 1, 1, index < enabled, [], new(TriggerManagementKind.Studio))).ToArray();
        var write = new TriggerStoreWrite("request", "fingerprint", "operator", null, Trigger(TriggerDesiredState.Enabled, false, "WorkflowResponse"), subscriptions);
        var planner = scope.ServiceProvider.GetRequiredService<ITriggerReservationPlanner>();
        if (enabled == 1) Assert.NotEmpty(await planner.PlanAsync(Scope, null, write));
        else Assert.Equal("validation", (await Assert.ThrowsAsync<TriggerOperationException>(() => planner.PlanAsync(Scope, null, write).AsTask())).Code);
    }

    private static ServiceProvider Provider()
    { var services = new ServiceCollection(); new StandaloneHttpTriggerAuthoringFeature().ConfigureServices(services); return services.BuildServiceProvider(); }
    private static JsonElement Configuration(string mode = "AsyncFanOut") => JsonSerializer.SerializeToElement(new
        { method = "POST", path = "orders/{id}", sourceAccess = "Authenticated", http = new { mode } });
    private static StandaloneTrigger Trigger(TriggerDesiredState state, bool activated, string mode = "AsyncFanOut") =>
        new("trigger", "Orders", "http", 1, Configuration(mode), 1, 1, 1, state, new(TriggerManagementKind.Studio), Now, Now,
            "creator", hasActivated: activated, tenantId: Scope.TenantId);
}
