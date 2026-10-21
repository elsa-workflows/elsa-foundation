using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleProjectionTests
{
    [Fact]
    public void Metadata_limit_counts_encoded_bytes_including_escaping()
    {
        var row = Row();
        // Raw UTF-8 text fits; the stored metadata representation does not.
        row.DeliveryAcknowledgementJson = new string('\u0001', RuleLimits.MaxStateMetadataBytes / 4);
        Assert.Throws<InvalidDataException>(() => RuleRowCodec.CheckProjectionSize(row));
    }

    [Fact]
    public void Projection_limit_includes_pending_receipt_and_complete_terminal_delivery()
    {
        var row = Row();
        row.InvocationJson = new string('a', RuleLimits.MaxEnvelopeBytes);
        row.FactsJson = new string('a', RuleLimits.MaxEnvelopeBytes - 4096);
        RuleRowCodec.CheckProjectionSize(row);
        Assert.Throws<InvalidDataException>(() => RuleRowCodec.CheckProjectionSize(row, new string('a', 8192)));
        row.TerminalJson = new string('a', 8192);
        Assert.Throws<InvalidDataException>(() => RuleRowCodec.CheckProjectionSize(row));
        row.TerminalJson = null;
        row.DeliveryPayloadJson = new string('a', 8192);
        Assert.Throws<InvalidDataException>(() => RuleRowCodec.CheckProjectionSize(row));
    }

    private static RuleRunRecord Row()
    {
        var invocation = Invocation();
        var scope = RuleRecordCodec.ScopeBytes(invocation.Identity);
        return RuleRowCodec.Create(invocation, RuleRecordCodec.Hash(scope), Convert.ToBase64String(scope),
            Encoding.UTF8.GetString(RuleRecordCodec.InvocationBytes(invocation)), RuleRecordCodec.Fingerprint(invocation).Sha256);
    }
}
