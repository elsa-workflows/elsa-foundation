using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreIntegrityTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Theory]
    [InlineData("acknowledged")]
    [InlineData("delivery-owner")]
    [InlineData("delivery-expiry")]
    [InlineData("delivery-fence")]
    [InlineData("acknowledgement-json")]
    public async Task Running_native_row_with_terminal_delivery_state_fails_closed_without_rewrite(string corruption)
    {
        var invocation = Copy(RuleStoreTestFixture.NewInvocation(), destination: Destination());
        var admission = await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready))
            .AdmitAsync(new(invocation));
        Assert.Equal(RuleAdmissionDisposition.Admitted, admission.Disposition);

        long? injectedExpiryTicks = null;
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
            switch (corruption)
            {
                case "acknowledged": row.DeliveryAcknowledged = true; break;
                case "delivery-owner": row.DeliveryOwner = "delivery-worker"; break;
                case "delivery-expiry":
                    injectedExpiryTicks = DateTimeOffset.UtcNow.AddMinutes(2).UtcDateTime.Ticks;
                    row.DeliveryExpiresAtUtcTicks = injectedExpiryTicks;
                    break;
                case "delivery-fence": row.DeliveryFence = 1; break;
                case "acknowledgement-json": row.DeliveryAcknowledgementJson = "{}"; break;
                default: throw new ArgumentOutOfRangeException(nameof(corruption));
            }
            await db.SaveChangesAsync();
        }

        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var store = fixture.CreateStore(validator);
        var run = await store.FindAsync(invocation.Identity);
        var terminal = await store.ReadTerminalAsync(invocation.Identity);
        var cancel = await store.CancelAsync(new(invocation));

        Assert.Equal(RuleRunReadDisposition.Unavailable, run.Disposition);
        Assert.Null(run.View);
        Assert.Equal(RuleTerminalReadDisposition.Unavailable, terminal.Disposition);
        Assert.Null(terminal.Result);
        Assert.Equal(RuleCancellationDisposition.Unavailable, cancel.Disposition);
        Assert.Equal(0, validator.Calls);

        await using var verify = fixture.Contexts.CreateDbContext();
        var retained = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        Assert.Equal((int)RuleRunStatus.Running, retained.Status);
        Assert.Equal(corruption == "acknowledged", retained.DeliveryAcknowledged);
        Assert.Equal(corruption == "delivery-owner" ? "delivery-worker" : null, retained.DeliveryOwner);
        Assert.Equal(injectedExpiryTicks, retained.DeliveryExpiresAtUtcTicks);
        Assert.Equal(corruption == "delivery-fence" ? 1 : 0, retained.DeliveryFence);
        Assert.Equal(corruption == "acknowledgement-json" ? "{}" : null, retained.DeliveryAcknowledgementJson);
        Assert.Null(retained.TerminalId64);
        Assert.Null(retained.TerminalJson);
        Assert.Null(retained.DeliveryId64);
        Assert.Null(retained.DeliveryPayloadJson);
        Assert.Null(retained.DeliveryPayloadSha25664);
    }

    [Theory]
    [InlineData("terminal-id")]
    [InlineData("delivery-id")]
    public async Task Canonical_terminal_and_delivery_with_wrong_derived_identity_fail_closed(string corruption)
    {
        var invocation = Copy(RuleStoreTestFixture.NewInvocation(), destination: Destination());
        var cancel = await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable))
            .CancelAsync(new(invocation));
        Assert.Equal(RuleCancellationDisposition.Cancelled, cancel.Disposition);

        string terminalJson;
        string deliveryJson;
        string deliveryHash;
        string terminalId;
        string deliveryId;
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
            var original = RuleRecordCodec.ReadTerminal(Encoding.UTF8.GetBytes(row.TerminalJson!));
            terminalId = original.TerminalId;
            deliveryId = row.DeliveryId64!;
            if (corruption == "terminal-id")
            {
                terminalId = DifferentSha256(original.TerminalId);
                var alteredTerminal = new RuleTerminalResult(terminalId, original.Kind, original.CommittedRounds,
                    original.ResultSchemaVersion, original.ResultFacts, original.FinalFactsSha256, original.Diagnostic);
                terminalJson = Encoding.UTF8.GetString(RuleRecordCodec.TerminalBytes(alteredTerminal));
                deliveryId = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes("rules.delivery:1:" + terminalId));
                deliveryJson = Encoding.UTF8.GetString(RuleRecordCodec.DeliveryRecordBytes(deliveryId, invocation.NativeDestination!, alteredTerminal));
                deliveryHash = RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(deliveryId, invocation.NativeDestination!, alteredTerminal));
            }
            else
            {
                terminalJson = row.TerminalJson!;
                deliveryId = DifferentSha256(deliveryId);
                deliveryJson = Encoding.UTF8.GetString(RuleRecordCodec.DeliveryRecordBytes(deliveryId, invocation.NativeDestination!, original));
                deliveryHash = RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(deliveryId, invocation.NativeDestination!, original));
            }

            var encodedTerminal = RuleRecordCodec.ReadTerminal(Encoding.UTF8.GetBytes(terminalJson));
            var encodedDelivery = RuleRecordCodec.ReadDeliveryRecord(Encoding.UTF8.GetBytes(deliveryJson));
            Assert.Equal(terminalId, encodedTerminal.TerminalId);
            Assert.Equal(deliveryId, encodedDelivery.DeliveryId);
            Assert.Equal(deliveryHash, encodedDelivery.CanonicalPayloadSha256);
            Assert.Equal(terminalJson, Encoding.UTF8.GetString(RuleRecordCodec.TerminalBytes(encodedTerminal)));
            Assert.Equal(deliveryJson, Encoding.UTF8.GetString(RuleRecordCodec.DeliveryRecordBytes(
                encodedDelivery.DeliveryId, encodedDelivery.Destination, encodedDelivery.Result)));

            row.TerminalId64 = terminalId;
            row.TerminalJson = terminalJson;
            row.DeliveryId64 = deliveryId;
            row.DeliveryPayloadJson = deliveryJson;
            row.DeliveryPayloadSha25664 = deliveryHash;
            await db.SaveChangesAsync();
        }

        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var store = fixture.CreateStore(validator);
        var run = await store.FindAsync(invocation.Identity);
        var terminal = await store.ReadTerminalAsync(invocation.Identity);
        var cancellation = await store.CancelAsync(new(invocation));

        Assert.Equal(RuleRunReadDisposition.Unavailable, run.Disposition);
        Assert.Null(run.View);
        Assert.Equal(RuleTerminalReadDisposition.Unavailable, terminal.Disposition);
        Assert.Null(terminal.Result);
        Assert.Equal(RuleCancellationDisposition.Unavailable, cancellation.Disposition);
        Assert.Equal(0, validator.Calls);

        await using var verify = fixture.Contexts.CreateDbContext();
        var retained = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        Assert.Equal((int)RuleRunStatus.Cancelled, retained.Status);
        Assert.Equal(1, retained.ModelRevision);
        Assert.Equal(terminalId, retained.TerminalId64);
        Assert.Equal(terminalJson, retained.TerminalJson);
        Assert.Equal(deliveryId, retained.DeliveryId64);
        Assert.Equal(deliveryJson, retained.DeliveryPayloadJson);
        Assert.Equal(deliveryHash, retained.DeliveryPayloadSha25664);
    }

    private static string DifferentSha256(string digest) => (digest[0] == '0' ? '1' : '0') + digest[1..];

    private static string Key(RuleRunIdentity identity) => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));
}
