namespace Elsa.Api.Compatibility.Testing.Transitions;

/// <summary>Checks that the transition registry is an exact, bounded inventory of legacy registrations.</summary>
public sealed class TransitionExceptionValidator
{
    public TransitionValidationResult Validate(
        IEnumerable<FastEndpointsRegistration> registrations,
        IEnumerable<FastEndpointsTransitionException> exceptions)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(exceptions);
        var discovered = registrations.ToArray();
        var registry = exceptions.ToArray();
        var issues = new List<TransitionValidationIssue>();

        foreach (var duplicate in discovered.GroupBy(registration => registration.Identity, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new TransitionValidationIssue("DuplicateRegistration", duplicate.Key,
                "More than one discovered FastEndpoints registration has the same identity."));
        }

        foreach (var registration in discovered)
        {
            if (registration.DynamicallyUnloadable)
            {
                issues.Add(new TransitionValidationIssue("DynamicUnloadableRegistration", registration.Identity,
                    "Dynamically unloadable endpoint modules cannot use FastEndpoints."));
            }

            var matches = registry.Where(exception => string.Equals(exception.RegistrationIdentity, registration.Identity, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                if (registration.DynamicRoute)
                {
                    issues.Add(new TransitionValidationIssue("DynamicRegistration", registration.Identity,
                        "The unresolved registration has no reviewed owner-source fingerprint."));
                }
                issues.Add(new TransitionValidationIssue("NewRegistration", registration.Identity,
                    "No transition exception matches the discovered registration."));
                continue;
            }

            if (matches.Length > 1)
            {
                issues.Add(new TransitionValidationIssue("AmbiguousException", registration.Identity,
                    "More than one transition exception matches the registration identity."));
                continue;
            }

            var exception = matches[0];
            if (exception.DynamicallyUnloadable)
                issues.Add(new TransitionValidationIssue("DynamicException", registration.Identity,
                    "A transition exception cannot authorize a dynamically unloadable endpoint module."));
            if (!string.Equals(exception.Owner, registration.Owner, StringComparison.Ordinal))
                issues.Add(new TransitionValidationIssue("OwnerMismatch", registration.Identity,
                    $"Registry owner '{exception.Owner}' does not match discovered owner '{registration.Owner}'."));
            if (registration.DynamicRoute && !string.Equals(exception.SourceHash, registration.SourceHash, StringComparison.Ordinal))
                issues.Add(new TransitionValidationIssue("DynamicRegistration", registration.Identity,
                    $"The reviewed owner-source fingerprint '{exception.SourceHash}' no longer matches the unresolved registration '{registration.SourceHash}'."));
            var expected = exception.Endpoints.ToHashSet();
            var actual = registration.Endpoints.ToHashSet();
            var expanded = actual.Except(expected).OrderBy(endpoint => endpoint.ToString(), StringComparer.Ordinal).ToArray();
            var removed = expected.Except(actual).OrderBy(endpoint => endpoint.ToString(), StringComparer.Ordinal).ToArray();
            if (expanded.Length > 0)
                issues.Add(new TransitionValidationIssue("ExpandedRegistration", registration.Identity,
                    $"Discovered routes exceed the approved exception: {string.Join(", ", expanded)}."));
            if (removed.Length > 0)
                issues.Add(new TransitionValidationIssue("RegistrationMismatch", registration.Identity,
                    $"Discovered routes no longer match the approved exception: {string.Join(", ", removed)}."));
            if (string.IsNullOrWhiteSpace(exception.RemovalOwner) || string.IsNullOrWhiteSpace(exception.FollowUp))
                issues.Add(new TransitionValidationIssue("IncompleteException", registration.Identity,
                    "A transition exception requires a removal owner and follow-up."));
        }

        var discoveredIdentities = discovered.Select(registration => registration.Identity).ToHashSet(StringComparer.Ordinal);
        foreach (var exception in registry.Where(exception => !discoveredIdentities.Contains(exception.RegistrationIdentity)))
        {
            issues.Add(new TransitionValidationIssue("StaleException", exception.RegistrationIdentity,
                "The transition exception has no discovered FastEndpoints registration."));
        }

        return new TransitionValidationResult(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.RegistrationIdentity, StringComparer.Ordinal)
            .ToArray());
    }

    public static TransitionValidationResult Reconcile(
        IEnumerable<FastEndpointsRegistration> registrations,
        IEnumerable<FastEndpointsTransitionException> exceptions) =>
        new TransitionExceptionValidator().Validate(registrations, exceptions);
}

public sealed record TransitionValidationIssue(string Code, string RegistrationIdentity, string Message);

public sealed record TransitionValidationResult(IReadOnlyList<TransitionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, Issues.Select(issue =>
                $"{issue.Code}: {issue.RegistrationIdentity}: {issue.Message}")));
    }
}
