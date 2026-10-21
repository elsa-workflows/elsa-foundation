using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>Computes the complete finite step over one frozen fact set; never advances durable state.</summary>
internal sealed class RuleRoundEvaluator : IRuleRoundValidator
{
    public RuleRoundReceipt Evaluate(RuleRoundInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var program = RulesCodec.ReadArtifact(input.Artifact);
        var facts = RulesCodec.CanonicalFacts(input.Facts);
        if (!facts.Select(fact => fact.Atom).SequenceEqual(input.Facts.Select(fact => fact.Atom), StringComparer.Ordinal))
            throw new InvalidDataException("rules.noncanonical-input");
        RulesCodec.ResolveMaxRounds(program, facts, input.MaxRounds);
        ValidateIdentity(input.Identity);
        if (input.Identity.Ordinal < 0 || input.Identity.Ordinal >= input.MaxRounds || input.ModelRevision <= 0
            || input.Identity.InputStateSha256 != RulesCodec.FactsHash(facts))
            throw new InvalidDataException("rules.round-input-identity");
        var pin = input.Implementation;
        if (pin.PinSchemaVersion != 1 || pin.LanguageId != RulesCodec.LanguageId || pin.LanguageVersion != 1
            || pin.ReceiptSchemaVersion != 1 || pin.StateSchemaVersion != 1)
            throw new NotSupportedException("rules.implementation-schema");
        var known = facts.Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        var fired = program.Rules.Where(rule => rule.Premises.All(fact => known.Contains(fact.Atom)))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
        var additions = fired.Select(rule => rule.Conclusion).Where(fact => !known.Contains(fact.Atom))
            .DistinctBy(fact => fact.Atom, StringComparer.Ordinal).OrderBy(fact => fact.Atom, StringComparer.Ordinal).ToArray();
        var firings = fired.Select(rule => new RuleFiringEvidence(rule.Id, rule.Conclusion)).ToArray();
        var receipt = new RuleRoundReceipt(input.Identity, RulesCodec.PinHash(pin), additions, firings, new string('0', 64));
        var bytes = RulesCodec.ReceiptBytes(receipt);
        if (bytes.Length > RuleLimits.MaxEnvelopeBytes) throw new InvalidDataException("rules.receipt-too-large");
        return new RuleRoundReceipt(receipt.Identity, receipt.ImplementationPinHash, additions, firings, RulesCodec.Hash(bytes));
    }

    public ValueTask<RuleRoundValidationResult> ValidateAsync(RuleRoundValidationInput input,
        RuleRoundReceipt candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(input);
            var expected = Evaluate(input.Input);
            ValidateIdentity(candidate.Identity);
            RulesCodec.CanonicalFacts(candidate.AddedFacts);
            if (candidate.FiringEvidence.Count > RuleLimits.MaxRules)
                throw new InvalidDataException("rules.too-many-firings");
            foreach (var firing in candidate.FiringEvidence)
            {
                RuleValueValidation.Identifier(firing?.RuleId!, nameof(candidate));
                RuleValueValidation.Identifier(firing?.Conclusion?.Atom!, nameof(candidate));
            }
            var bytes = RulesCodec.ReceiptBytes(candidate);
            var equivalent = bytes.Length <= RuleLimits.MaxEnvelopeBytes
                && candidate.SemanticReceiptSha256 == expected.SemanticReceiptSha256
                && bytes.AsSpan().SequenceEqual(RulesCodec.ReceiptBytes(expected));
            return ValueTask.FromResult(new RuleRoundValidationResult(
                equivalent ? RuleValidationDisposition.Equivalent : RuleValidationDisposition.Invalid,
                equivalent ? null : "rules.incomplete-or-conflicting-receipt"));
        }
        catch (NotSupportedException)
        {
            return ValueTask.FromResult(new RuleRoundValidationResult(RuleValidationDisposition.UnsupportedFormat,
                "rules.unsupported-format"));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ValueTask.FromResult(new RuleRoundValidationResult(RuleValidationDisposition.Invalid,
                "rules.invalid-round-receipt"));
        }
    }

    private static void ValidateIdentity(RuleRoundIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identity.Run);
        ArgumentNullException.ThrowIfNull(identity.Run.Scope);
        RuleValueValidation.Identifier(identity.Run.InvocationId, nameof(identity));
        RuleValueValidation.Identifier(identity.Run.Scope.PersistencePartition, nameof(identity));
        if (identity.Run.Scope.TenantId is { } tenant) RuleValueValidation.Identifier(tenant, nameof(identity));
        RuleValueValidation.Sha256(identity.Fingerprint.Sha256, nameof(identity));
        RuleValueValidation.Sha256(identity.InputStateSha256, nameof(identity));
    }
}
