using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class PersistedRuntimeHistoryOccurrenceAuthorizationTests
{
    [Theory]
    [InlineData("direct")] [InlineData("role")] [InlineData("membership")]
    public async Task Actual_local_principal_and_current_persisted_grants_disclose_real_frozen_occurrences(string source)
    {
        await using var fixture = await Fixture.Start(source);
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1");
        var first = await Available(response, "activity-0"); var token = first.GetProperty("sequenceToken").GetString(); Assert.False(string.IsNullOrWhiteSpace(token));
        using var next = await fixture.Client.GetAsync(Endpoint + "?limit=1&position=2&sequenceToken=" + Uri.EscapeDataString(token!));
        var last = await Available(next, "activity-2"); Assert.Equal("2", last.GetProperty("selectedPosition").GetString());
        Assert.True(fixture.Gate.OccurrenceCommands > fixture.SeedOccurrenceCommands);
    }

    [Theory]
    [InlineData("runtime")] [InlineData("structure")] [InlineData("role")] [InlineData("role-membership")]
    [InlineData("membership")] [InlineData("membership-grants")] [InlineData("disabled")] [InlineData("deleted")]
    public async Task Actual_persisted_retirement_denies_before_occurrence_admission_despite_old_sign_in_claims(string change)
    {
        await using var fixture = await Fixture.Start(Source(change)); await fixture.Retire(change);
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1");
        await Missing(response); Assert.Equal(fixture.SeedOccurrenceCommands, fixture.Gate.OccurrenceCommands);
    }

    [Theory]
    [InlineData("runtime")] [InlineData("structure")] [InlineData("role")] [InlineData("role-membership")]
    [InlineData("membership")] [InlineData("membership-grants")] [InlineData("disabled")] [InlineData("deleted")]
    [InlineData("values")] [InlineData("payloads")]
    public async Task Actual_persisted_retirement_while_a_real_provider_read_is_pending_cannot_disclose_it(string change)
    {
        await using var fixture = await Fixture.Start(Source(change)); fixture.Gate.Arm();
        var pending = fixture.Client.GetAsync(Endpoint + "?limit=1");
        try
        {
            await fixture.Gate.Entered.WaitAsync(TimeSpan.FromSeconds(30));
            await fixture.Retire(change);
        }
        finally { fixture.Gate.Release(); }
        using var response = await pending; await Missing(response);
    }

    [Theory]
    [InlineData("values")] [InlineData("payloads")]
    public async Task Frozen_continuation_cannot_cross_a_changed_actual_value_or_payload_profile(string change)
    {
        await using var fixture = await Fixture.Start();
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1");
        var first = await Available(response, "activity-0"); var token = first.GetProperty("sequenceToken").GetString()!;
        await fixture.Retire(change);
        using var retired = await fixture.Client.GetAsync(Endpoint + "?limit=1&position=1&sequenceToken=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.BadRequest, retired.StatusCode);
        Assert.DoesNotContain("activity-", await retired.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("values")] [InlineData("payloads")]
    public async Task Current_grants_outside_the_incoming_permission_ceiling_never_escalate_the_pending_result(string change)
    {
        await using var fixture = await Fixture.Start();
        fixture.UsePrincipal(Without(fixture.SignedIn, IdentityClaimTypes.Permission, Permission(change)));
        fixture.Gate.Arm(); var pending = fixture.Client.GetAsync(Endpoint + "?limit=1");
        try { await fixture.Gate.Entered.WaitAsync(TimeSpan.FromSeconds(30)); await fixture.Retire(change); }
        finally { fixture.Gate.Release(); }
        using var response = await pending; await Available(response, "activity-0");
    }

    [Fact]
    public async Task Canonical_local_user_is_authorized_independently_of_the_audit_name_identifier()
    {
        await using var fixture = await Fixture.Start();
        var principal = Without(fixture.SignedIn, ClaimTypes.NameIdentifier);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(ClaimTypes.NameIdentifier, "audit-only-alias"));
        fixture.UsePrincipal(principal);
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1"); await Available(response, "activity-0");
    }

    [Theory]
    [InlineData("runtime")] [InlineData("structure")]
    public async Task Current_persisted_required_grants_cannot_exceed_the_incoming_request_ceiling(string permission)
    {
        await using var fixture = await Fixture.Start();
        fixture.UsePrincipal(Without(fixture.SignedIn, IdentityClaimTypes.Permission, Permission(permission)));
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1");
        if (permission == "runtime") Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        else await Missing(response);
        Assert.Equal(fixture.SeedOccurrenceCommands, fixture.Gate.OccurrenceCommands);
    }

    [Fact]
    public async Task Another_real_tenant_user_cannot_reuse_the_authorized_run_or_frozen_continuation()
    {
        await using var fixture = await Fixture.Start();
        using var first = await fixture.Client.GetAsync(Endpoint + "?limit=1");
        var page = await Available(first, "activity-0"); var reads = fixture.Gate.OccurrenceCommands;
        var foreign = await fixture.ForeignPrincipal(); fixture.UsePrincipal(foreign);
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1&position=1&sequenceToken=" + Uri.EscapeDataString(page.GetProperty("sequenceToken").GetString()!));
        await Missing(response); Assert.Equal(reads, fixture.Gate.OccurrenceCommands);
    }

    [Theory]
    [InlineData("provider-only")] [InlineData("duplicate-user")]
    public async Task Unsupported_subject_shapes_cannot_impersonate_the_real_local_persisted_user(string shape)
    {
        await using var fixture = await Fixture.Start();
        var principal = shape == "provider-only" ? Without(fixture.SignedIn, IdentityClaimTypes.UserId) : fixture.SignedIn.Clone();
        ((ClaimsIdentity)principal.Identity!).AddClaim(shape == "provider-only"
            ? new(IdentityClaimTypes.SubjectProvider, "external-provider") : new(IdentityClaimTypes.UserId, "another-user"));
        fixture.UsePrincipal(principal);
        using var response = await fixture.Client.GetAsync(Endpoint + "?limit=1"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", json.RootElement.GetProperty("state").GetString()); Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(fixture.SeedOccurrenceCommands, fixture.Gate.OccurrenceCommands);
    }

    private static async Task<JsonElement> Available(HttpResponseMessage response, string activity)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text); var root = json.RootElement;
        Assert.Equal("available", root.GetProperty("state").GetString()); Assert.Equal("3", root.GetProperty("totalCount").GetString());
        Assert.Equal(activity, Assert.Single(root.GetProperty("items").EnumerateArray()).GetProperty("activityExecutionId").GetString());
        foreach (var item in root.GetProperty("items").EnumerateArray())
        { Assert.False(item.TryGetProperty("payload", out _)); Assert.False(item.TryGetProperty("metadata", out _)); Assert.False(item.TryGetProperty("values", out _)); }
        return root.Clone();
    }
    private static async Task Missing(HttpResponseMessage response)
    { Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Empty(await response.Content.ReadAsStringAsync()); }
}
