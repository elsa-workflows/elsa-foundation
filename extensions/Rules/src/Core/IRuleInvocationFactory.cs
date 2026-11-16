namespace Elsa.Rules.Core;

/// <summary>Outcome of preparing a new immutable invocation, without writing durable state.</summary>
public enum RuleInvocationPreparationDisposition
{
    Prepared,
    Invalid,
    Unavailable
}

/// <summary>A frozen invocation for admission and retries, or a safe preparation diagnostic.</summary>
public sealed record RuleInvocationPreparation
{
    public RuleInvocationPreparation(RuleInvocationPreparationDisposition disposition,
        RuleInvocationEnvelope? invocation, RuleRunDiagnostic? diagnostic)
    {
        if (!Enum.IsDefined(disposition)
            || (disposition == RuleInvocationPreparationDisposition.Prepared) != (invocation is not null)
            || (disposition == RuleInvocationPreparationDisposition.Prepared) == (diagnostic is not null))
            throw new ArgumentException("Preparation payload does not match its disposition.");
        Disposition = disposition;
        Invocation = invocation;
        Diagnostic = diagnostic;
    }

    public RuleInvocationPreparationDisposition Disposition { get; }
    public RuleInvocationEnvelope? Invocation { get; }
    public RuleRunDiagnostic? Diagnostic { get; }
}

/// <summary>Freezes a new invocation's artifact, default budget and trusted installed implementation.</summary>
/// <remarks>Retain the returned envelope for admission retries. Preparing again can select a different deployment.</remarks>
public interface IRuleInvocationFactory
{
    ValueTask<RuleInvocationPreparation> PrepareAsync(RuleRunIdentity identity, RuleProgram program,
        IReadOnlyList<RuleFact> initialFacts, int? maxRounds = null, RuleNativeResultAddress? nativeDestination = null,
        CancellationToken cancellationToken = default);
}
