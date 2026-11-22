using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

internal sealed class RuleInvocationFactory(IRuleImplementationIdentitySource identitySource) : IRuleInvocationFactory
{
    public async ValueTask<RuleInvocationPreparation> PrepareAsync(RuleRunIdentity identity, RuleProgram program,
        IReadOnlyList<RuleFact> initialFacts, int? maxRounds = null, RuleNativeResultAddress? nativeDestination = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(initialFacts);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Capture caller-owned collections before awaiting deployment evidence.
            var facts = RuleRecordCodec.CanonicalFacts(initialFacts);
            var artifact = RulesCodec.CreateArtifact(program);
            var budget = RulesCodec.ResolveMaxRounds(program, facts, maxRounds);
            var implementation = await identitySource.ReadAsync(cancellationToken);
            if (implementation is null)
                return new(RuleInvocationPreparationDisposition.Unavailable, null, new("rules.implementation-unavailable"));
            if (implementation.PinSchemaVersion != 1 || implementation.LanguageId != RuleLanguage.Id
                || implementation.LanguageVersion != 1 || implementation.ReceiptSchemaVersion != 1
                || implementation.StateSchemaVersion != 1)
                return new(RuleInvocationPreparationDisposition.Unavailable, null, new("rules.unsupported-semantic-schema"));
            var invocation = new RuleInvocationEnvelope(identity, artifact, facts, budget, implementation, 1, nativeDestination);
            _ = RuleAdmissionCapacity.Measure(invocation);
            return new(RuleInvocationPreparationDisposition.Prepared, invocation, null);
        }
        catch (NotSupportedException)
        {
            return new(RuleInvocationPreparationDisposition.Unavailable, null, new("rules.unsupported-format"));
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or OverflowException)
        {
            return new(RuleInvocationPreparationDisposition.Invalid, null, new("rules.invalid-invocation"));
        }
    }
}
