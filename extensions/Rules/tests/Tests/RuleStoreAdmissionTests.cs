using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreAdmissionTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Fact]
    public async Task Matching_admission_replays_before_current_validator_is_consulted()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var firstValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready);
        var first = await fixture.CreateStore(firstValidator).AdmitAsync(new(invocation));

        var unavailableValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var replay = await fixture.CreateStore(unavailableValidator).AdmitAsync(new(invocation));

        Assert.Equal(RuleAdmissionDisposition.Admitted, first.Disposition);
        Assert.Equal(RuleAdmissionDisposition.Replay, replay.Disposition);
        Assert.Equal(first.TerminalId, replay.TerminalId);
        Assert.Equal(0, unavailableValidator.Calls);
    }

    [Fact]
    public async Task Different_fingerprint_for_retained_identity_conflicts_before_validation()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var admitted = await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready))
            .AdmitAsync(new(invocation));
        var changed = RuleTestData.Copy(invocation, facts: RuleTestData.Facts("left", "seed"));
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);

        var conflict = await fixture.CreateStore(validator).AdmitAsync(new(changed));

        Assert.Equal(RuleAdmissionDisposition.Admitted, admitted.Disposition);
        Assert.Equal(RuleAdmissionDisposition.Conflict, conflict.Disposition);
        Assert.Null(conflict.ModelRevision);
        Assert.Null(conflict.TerminalId);
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public async Task New_refusal_is_durable_and_same_fingerprint_replays_original_diagnostic()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var diagnostic = new RuleRunDiagnostic("rules.invalid-finite-model");
        var firstValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Refused, diagnostic);
        var first = await fixture.CreateStore(firstValidator).AdmitAsync(new(invocation));
        var replayValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var replay = await fixture.CreateStore(replayValidator).RefuseAdmissionAsync(
            new(invocation, new RuleRunDiagnostic("rules.different-diagnostic")));
        var terminal = await fixture.CreateStore(replayValidator).ReadTerminalAsync(invocation.Identity);

        Assert.Equal(RuleAdmissionDisposition.Refused, first.Disposition);
        Assert.NotNull(first.TerminalId);
        Assert.Equal(RuleRefusalDisposition.Replay, replay.Disposition);
        Assert.Equal(first.TerminalId, replay.TerminalId);
        Assert.Equal(0, replayValidator.Calls);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(diagnostic.Code, terminal.Result?.Diagnostic?.Code);
        Assert.Empty(terminal.Result?.ResultFacts ?? []);
    }

    [Fact]
    public async Task New_refusal_requires_exact_validator_diagnostic()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Refused,
            new RuleRunDiagnostic("rules.invalid-finite-model"));

        var result = await fixture.CreateStore(validator).RefuseAdmissionAsync(
            new(invocation, new RuleRunDiagnostic("rules.other")));
        var read = await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready))
            .FindAsync(invocation.Identity);

        Assert.Equal(RuleRefusalDisposition.Invalid, result.Disposition);
        Assert.Null(result.TerminalId);
        Assert.Equal(RuleRunReadDisposition.NotFound, read.Disposition);
    }

}
