using System.Text.RegularExpressions;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A reviewed, explicit permission for one migration class to contain exactly the destructive operations it
/// lists (elsa-workflows/elsa-foundation#2104, spec 185 FR-012). Lives beside <see cref="EfModuleAttribute"/> so
/// a third-party module can carry it too (spec 185, Out of Scope).
/// </summary>
/// <remarks>
/// <see cref="ExpandOnlyMigrationGuard.Evaluate"/> is what checks the match: this migration's violations
/// (<see cref="ExpandOnlyMigrationGuard.Classify"/>) must equal <see cref="Violations"/> exactly, so this
/// attribute never permits a second destructive operation added in the same pull request (spec 185, Decisions).
/// Each provider's migration of one change carries its own (FR-014): the attribute never covers more than the
/// migration class it sits on.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ExpandOnlyMigrationOptOutAttribute : Attribute
{
    private static readonly Regex ReviewReferencePattern = new(@"^#\d+$", RegexOptions.Compiled);

    /// <param name="reason">Why the removal is safe. Must not be blank.</param>
    /// <param name="reviewReference">The GitHub issue or pull request where this was reviewed, in <c>#NNNN</c> form.</param>
    /// <param name="violations">
    /// The exact violations this opt-out permits, each in <see cref="ExpandOnlyMigrationGuard"/>'s canonical
    /// form (FR-011). At least one; an opt-out that permits nothing is meaningless.
    /// </param>
    public ExpandOnlyMigrationOptOutAttribute(string reason, string reviewReference, params string[] violations)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An expand-only migration opt-out must give a reason.", nameof(reason));
        if (reviewReference is null || !ReviewReferencePattern.IsMatch(reviewReference))
            throw new ArgumentException(
                $"An expand-only migration opt-out's review reference must be a GitHub issue or pull request in '#NNNN' form, not '{reviewReference}'.",
                nameof(reviewReference));
        if (violations is null || violations.Length == 0)
            throw new ArgumentException("An expand-only migration opt-out must list at least one violation it permits.", nameof(violations));
        if (violations.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("An expand-only migration opt-out cannot list a blank violation.", nameof(violations));

        Reason = reason;
        ReviewReference = reviewReference;
        Violations = violations;
    }

    /// <summary>Why this migration's listed violations are safe to ship.</summary>
    public string Reason { get; }

    /// <summary>The GitHub issue or pull request where this opt-out was reviewed, in <c>#NNNN</c> form.</summary>
    public string ReviewReference { get; }

    /// <summary>
    /// The exact violations this opt-out permits, in <see cref="ExpandOnlyMigrationGuard"/>'s canonical form.
    /// A migration's actual violations must equal this list exactly (FR-013).
    /// </summary>
    public IReadOnlyList<string> Violations { get; }
}
