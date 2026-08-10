using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// A document write a design commit ordered but could not perform itself, because its target belongs to
/// another Groundwork target and so cannot join the design transaction.
/// </summary>
public sealed record DesignPostCommitDocumentWrite(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    string ContentJson)
{
    /// <summary>The create-only write this deferred document describes.</summary>
    public SaveDocumentRequest ToSaveRequest() => new(DocumentKind, Id, SchemaVersion, ContentJson, ExpectedVersion: 0);
}

/// <summary>
/// One outstanding obligation of a committed design mutation. Written inside the design commit, so it becomes
/// durable at exactly the moment the mutation becomes done, and deleted once delivered.
/// </summary>
/// <param name="IntentId">
/// The durable logical identity. Deterministic, so the same commit replayed produces the same intent rather
/// than a second one.
/// </param>
/// <param name="Kind">Selects the <see cref="IDesignPostCommitIntentDeliverer"/> that knows how to perform it.</param>
public sealed record DesignPostCommitIntent(
    string IntentId,
    string Kind,
    DesignPostCommitDocumentWrite Document);

/// <summary>
/// Performs one kind of design post-commit intent. Implementations live with the lane that owns the deferred
/// write, which is what keeps the design lane's outbox from knowing about publishing.
/// <para>
/// Delivery must be idempotent: the redrive can only learn that an attempt succeeded by completing it, so a
/// crash between a successful delivery and the intent's deletion is redelivered.
/// </para>
/// </summary>
public interface IDesignPostCommitIntentDeliverer
{
    /// <summary>The <see cref="DesignPostCommitIntent.Kind"/> this deliverer performs.</summary>
    string IntentKind { get; }

    /// <summary>
    /// Performs the intent. Returning normally means delivered, and the redrive deletes the intent. Throwing
    /// means not delivered: the intent keeps its place and becomes claimable again after a backoff.
    /// </summary>
    ValueTask DeliverAsync(DesignPostCommitIntent intent, CancellationToken cancellationToken = default);
}

/// <summary>What one sweep did with one intent.</summary>
public enum DesignPostCommitIntentOutcome
{
    /// <summary>Delivered and deleted.</summary>
    Delivered,

    /// <summary>Not delivered; released for a later sweep.</summary>
    Deferred,

    /// <summary>Delivered, but another owner had already taken the claim over, so this sweep did not delete it.</summary>
    ClaimLost
}

/// <summary>The result of one intent's pass through a sweep.</summary>
public sealed record DesignPostCommitIntentResult(
    string IntentId,
    string Kind,
    DesignPostCommitIntentOutcome Outcome,
    string? FailureMessage = null);

/// <summary>The result of one bounded redrive sweep.</summary>
public sealed record DesignPostCommitRedriveResult(IReadOnlyList<DesignPostCommitIntentResult> Intents)
{
    public int DeliveredCount => Intents.Count(intent => intent.Outcome is DesignPostCommitIntentOutcome.Delivered);

    public int DeferredCount => Intents.Count(intent => intent.Outcome is DesignPostCommitIntentOutcome.Deferred);
}
