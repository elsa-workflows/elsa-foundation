using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class AlterationRequestCanonicalizerTests
{
    [Fact]
    public void Canonicalize_NormalizesExplicitIdsAndObjectProperties_ButPreservesAlterationOrder()
    {
        var first = Request([" execution-b", "execution-a", "execution-b"], "{\"b\":1.0,\"a\":true}", "ModifyVariable", "CancelWorkflow");
        var same = Request(["execution-a", "execution-b"], "{\"a\":true,\"b\":1}", "ModifyVariable", "CancelWorkflow");
        var reordered = Request(["execution-a", "execution-b"], "{\"a\":true,\"b\":1}", "CancelWorkflow", "ModifyVariable");

        Assert.Equal(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(same));
        Assert.NotEqual(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(reordered));
    }

    [Fact]
    public void Canonicalize_BindsTenantAndAuthorityAndNormalizesQueryDefaults()
    {
        var query = new WorkflowAlterationQuerySelector(definitionId: "orders", matchAllAuthorized: false);
        var first = new WorkflowAlterationSubmission(new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"), WorkflowAlterationTargetSelector.ForQuery(query), [Envelope("{}")] );
        var same = new WorkflowAlterationSubmission(new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"), WorkflowAlterationTargetSelector.ForQuery(new WorkflowAlterationQuerySelector(definitionId: "orders")), [Envelope("{}")] );
        var otherTenant = new WorkflowAlterationSubmission(new WorkflowAlterationAuthorityScope("tenant-b", "system", "operator"), WorkflowAlterationTargetSelector.ForQuery(query), [Envelope("{}")] );

        Assert.Equal(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(same));
        Assert.NotEqual(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(otherTenant));
    }

    [Fact]
    public void Retained_payload_preserves_raw_spelling_while_hash_uses_exact_json_equality()
    {
        const string raw = "{ \"UpperKey\": \"\\u0061\", \"n\": 100000000000000000000000000000000000000000000000001.0, \"nil\": null }";
        var first = Request(["execution-a"], raw, "ModifyVariable");
        var same = Request(["execution-a"], "{\"nil\":null,\"n\":100000000000000000000000000000000000000000000000001,\"UpperKey\":\"a\"}", "ModifyVariable");
        using var retained = JsonDocument.Parse(WorkflowAlterationRequestCanonicalizer.Canonicalize(first));

        Assert.Equal(raw, retained.RootElement.GetProperty("alterations")[0].GetProperty("payload").GetRawText());
        Assert.Equal(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(same));
        foreach (var different in new[]
                 {
                     "{\"nil\":null,\"n\":100000000000000000000000000000000000000000000000002,\"UpperKey\":\"a\"}",
                     "{\"n\":100000000000000000000000000000000000000000000000001,\"UpperKey\":\"a\"}",
                     "{\"nil\":null,\"n\":100000000000000000000000000000000000000000000000001,\"upperKey\":\"a\"}"
                 })
            Assert.NotEqual(WorkflowAlterationRequestCanonicalizer.Hash(first), WorkflowAlterationRequestCanonicalizer.Hash(Request(["execution-a"], different, "ModifyVariable")));
    }

    [Fact]
    public void Hash_normalizes_query_instants_and_preserves_opaque_authority_code_units()
    {
        var instant = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(1));
        WorkflowAlterationSubmission Make(string system, DateTimeOffset from) => new(
            new WorkflowAlterationAuthorityScope("tenant-a", system, "operator"),
            WorkflowAlterationTargetSelector.ForQuery(new WorkflowAlterationQuerySelector(from: from)), [Envelope("{}")]);

        Assert.Equal(WorkflowAlterationRequestCanonicalizer.Hash(Make("system", instant)), WorkflowAlterationRequestCanonicalizer.Hash(Make("system", instant.ToUniversalTime())));
        Assert.NotEqual(WorkflowAlterationRequestCanonicalizer.Hash(Make("system\ud800", instant)), WorkflowAlterationRequestCanonicalizer.Hash(Make("system\ufffd", instant)));
    }

    private static WorkflowAlterationSubmission Request(IReadOnlyCollection<string> ids, string payload, params string[] kinds) =>
        new(new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"), WorkflowAlterationTargetSelector.ForExecutionIds(ids), kinds.Select(kind => Envelope(payload, kind)).ToArray());

    private static WorkflowAlterationEnvelope Envelope(string payload, string kind = "ModifyVariable")
    {
        using var document = JsonDocument.Parse(payload);
        return new WorkflowAlterationEnvelope(kind, 1, document.RootElement);
    }
}
