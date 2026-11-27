using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Exceptions;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// How the import reports a refusal that reaches it as an exception: found however deep it was wrapped, and as a
/// diagnostic that says why, not "failed unexpectedly". The publish phase and the publisher adapter
/// (<c>Elsa3.Activities.Design.Import.Publishing</c>) both use it, so a refusal reads the same wherever it is raised.
/// </summary>
public static class ReusableActivityImportRefusals
{
    /// <summary>
    /// The exception itself, or the one a compiler or store wrapped it in, following every exception an
    /// <see cref="AggregateException"/> holds, not only its first.
    /// </summary>
    public static T? Find<T>(Exception exception) where T : Exception => exception switch
    {
        T match => match,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find<T>).FirstOrDefault(found => found is not null),
        { InnerException: { } inner } => Find<T>(inner),
        _ => null
    };

    /// <summary>
    /// A schema write refusal as one diagnostic carrying what <c>SchemaWriteRefusalProblem</c> gives every API (#5972): the
    /// code, the family and both versions, and for a dormancy refusal its feature and reason. Its message names the family
    /// and versions and, since the cause differs by refusal, what this host needs before it can write the family.
    /// </summary>
    public static ActivityDiagnostic SchemaWriteRefused(SchemaWriteRefusedException refusal, ActivityDiagnosticSubject subject)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        ArgumentNullException.ThrowIfNull(subject);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["family"] = refusal.Family,
            ["writeVersion"] = refusal.WriteVersion,
            ["requiredVersion"] = refusal.RequiredVersion
        };
        if (refusal is SchemaDormancyRefusedException dormancy)
        {
            if (dormancy.FeatureId is { } feature)
                metadata["feature"] = feature;
            metadata["reason"] = dormancy.Reason;
        }

        return new(
            refusal.Code,
            ActivityDiagnosticSeverity.Error,
            refusal.Message,
            subject,
            // The refused write may be the publication's receipt, written after the version itself committed, so this does not
            // claim that nothing was published.
            Remediation: $"This host refused to write schema family '{refusal.Family}'; the message says why and what the host " +
                         "needs. Once that is resolved, upload the same export again and apply it under a new idempotency key: " +
                         "a version that did publish is then reported as already published, and only the others publish.",
            Metadata: metadata);
    }
}
