using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public class RuleAdmissionTests
{
    [Fact]
    public async Task Admission_requires_installed_identity_and_matching_complete_pin()
    {
        var invocation = Invocation();
        Assert.Equal(RuleAdmissionValidationDisposition.Ready, (await Validate(invocation, invocation.ExpectedImplementation)).Disposition);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, (await Validate(invocation, null)).Disposition);
        var pin = invocation.ExpectedImplementation;
        var other = new RuleSemanticPin(1, pin.Packages, pin.LanguageId, 2, 1, 1);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, (await Validate(invocation, other)).Disposition);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, (await Validate(Copy(invocation, pin: other), other)).Disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_common_schema_is_unavailable_before_reading_identity(bool destinationSchema)
    {
        var original = Invocation();
        var invocation = new RuleInvocationEnvelope(original.Identity, original.Artifact, original.InitialFacts,
            original.MaxRounds, original.ExpectedImplementation, destinationSchema ? 1 : 2,
            destinationSchema ? Destination(formatVersion: 2) : null);
        var source = new IdentitySource(original.ExpectedImplementation);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable,
            (await new RuleAdmissionValidator(source).ValidateAsync(invocation)).Disposition);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Intact_invalid_finite_model_is_refused_but_unknown_interpreter_is_unavailable()
    {
        var invocation = Invocation();
        var invalid = Encoding.UTF8.GetBytes("{\"formatVersion\":1}");
        var hash = RuleRecordCodec.Hash(invalid);
        var malformed = Copy(invocation, artifact: new(1, "rules:" + hash, hash, invalid));
        Assert.Equal(RuleAdmissionValidationDisposition.Refused, (await Validate(malformed, invocation.ExpectedImplementation)).Disposition);
        var unknown = Copy(invocation, artifact: new(2, "rules:" + hash, hash, invalid));
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, (await Validate(unknown, invocation.ExpectedImplementation)).Disposition);
    }

    [Fact]
    public async Task Corrupt_common_envelope_is_invalid_before_reading_deployment_identity()
    {
        var invocation = Invocation();
        var corrupt = Copy(invocation, artifact: new(1, invocation.Artifact.ArtifactId, invocation.Artifact.Sha256, new byte[] { 0 }));
        var source = new IdentitySource(invocation.ExpectedImplementation);
        var result = await new RuleAdmissionValidator(source).ValidateAsync(corrupt);
        Assert.Equal(RuleAdmissionValidationDisposition.Invalid, result.Disposition);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Cancelled_admission_does_not_read_deployment_identity()
    {
        var source = new IdentitySource(Invocation().ExpectedImplementation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new RuleAdmissionValidator(source).ValidateAsync(Invocation(), cancellation.Token));
        Assert.Equal(0, source.ReadCount);
    }

    private static ValueTask<RuleAdmissionValidationResult> Validate(RuleInvocationEnvelope invocation, RuleSemanticPin? installed) =>
        new RuleAdmissionValidator(new IdentitySource(installed)).ValidateAsync(invocation);

    // Synthetic deployment identities test the validator, not package provenance or runtime registration.
    private sealed class IdentitySource(RuleSemanticPin? pin) : IRuleImplementationIdentitySource
    {
        internal int ReadCount { get; private set; }
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult(pin);
        }
    }
}
