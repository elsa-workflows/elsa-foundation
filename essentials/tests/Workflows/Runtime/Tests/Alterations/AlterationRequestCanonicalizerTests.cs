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

    private static WorkflowAlterationSubmission Request(IReadOnlyCollection<string> ids, string payload, params string[] kinds) =>
        new(new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"), WorkflowAlterationTargetSelector.ForExecutionIds(ids), kinds.Select(kind => Envelope(payload, kind)).ToArray());

    private static WorkflowAlterationEnvelope Envelope(string payload, string kind = "ModifyVariable")
    {
        using var document = JsonDocument.Parse(payload);
        return new WorkflowAlterationEnvelope(kind, 1, document.RootElement);
    }
}
