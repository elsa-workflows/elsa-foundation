using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.WorkflowPreparationEnrollmentCodec;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Checked metadata controls only; these do not qualify native commit/target/writer exclusion.
public sealed class WorkflowPreparationEnrollmentCodecTests
{
    [Fact]
    public void Abort_round_trips_without_a_header_or_token()
    {
        var barrier = Abort(Binding());
        var restored = Read(Create(barrier), barrier.Binding.Owner.Id);
        Assert.Equal(barrier, restored);
        Assert.Null(restored.HeaderIncarnation);
        Assert.Null(restored.SelectionFingerprint);
    }

    [Fact]
    public void A_new_nonce_or_material_cannot_escape_the_same_preparation_key()
    {
        var original = Binding();
        var nonce = original with { IntentId = "different-intent" };
        var material = original with { Owner = original.Owner with { PreparedContentFingerprint = "other" } };
        Assert.Equal(Create(Abort(original)).Id, Create(Abort(nonce)).Id);
        Assert.Equal(Create(Abort(original)).Id, Create(Abort(material)).Id);
        Assert.False(SameBinding(original, nonce));
        Assert.False(SameBinding(original, material));
    }

    [Fact]
    public void Logical_revision_advances_preserve_identity_but_incarnation_and_target_do_not()
    {
        var original = Binding();
        Assert.True(SameBinding(original, original with
        {
            Owner = original.Owner with { OperationRevision = 12, OperationState = "Rejected" }
        }));
        Assert.False(SameBinding(original, original with { TargetAssociation = "other-native-target" }));
        Assert.False(SameBinding(original, original with { Owner = original.Owner with { LedgerIncarnation = "recreated" } }));
    }

    [Fact]
    public void Global_and_tenant_identity_remain_distinct()
    {
        var original = Binding();
        var tenant = original with { Owner = original.Owner with { Id = original.Owner.Id with { Scope = new("0") } } };
        Assert.NotEqual(Create(Abort(original)).Id, Create(Abort(tenant)).Id);
    }

    [Theory]
    [InlineData("formatVersion")]
    [InlineData("barrier")]
    public void Missing_required_wire_members_are_corrupt(string field)
    {
        var binding = Binding();
        var row = Create(Abort(binding));
        var json = JsonNode.Parse(row.ContentJson)!.AsObject();
        json.Remove(field);
        row.ContentJson = json.ToJsonString();
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id));
    }

    [Fact]
    public void Projection_or_partition_tampering_is_not_absence()
    {
        var binding = Binding();
        var row = Create(Abort(binding));
        row.IncarnationId = "replaced";
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id));
        row = Create(Abort(binding));
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id with { OperationId = "other" }));
    }

    [Fact]
    public void Abort_cannot_carry_selection_or_header_identity()
    {
        Assert.Throws<InvalidDataException>(() => Create(Abort(Binding()) with { HeaderIncarnation = "synthetic" }));
        Assert.Throws<InvalidDataException>(() => Create(Abort(Binding()) with { SelectionFingerprint = "synthetic" }));
    }

    [Fact]
    public void Accepted_requires_actual_header_and_selection_identity()
    {
        var barrier = new Barrier(Binding(), Outcome.Accepted, "barrier", "header", "selection");
        Assert.Equal(barrier, Read(Create(barrier), barrier.Binding.Owner.Id));
        Assert.Throws<InvalidDataException>(() => Create(barrier with { HeaderIncarnation = null }));
        Assert.Throws<InvalidDataException>(() => Create(barrier with { SelectionFingerprint = null }));
    }

    private static Barrier Abort(WorkflowPreparationEfEnrollmentBinding binding) =>
        new(binding, Outcome.Aborted, "permanent-barrier", null, null);

    private static WorkflowPreparationEfEnrollmentBinding Binding() => new(
        new(new(null, "source-owner", "existing-operation"), "ledger-incarnation", 3, "Prepared", "request",
            "snapshot", "source-fingerprint", "source-profile", "compiler-profile", "prepared-profile", "content-fingerprint"),
        "committed-intent", "native-association");
}
