using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Api.Compatibility.Testing.Security;

/// <summary>Reconciles endpoint permission consumption with active contributor provenance.</summary>
public sealed class PermissionOwnershipValidator
{
    private readonly IReadOnlyList<IPermissionContributor> _contributors;

    public PermissionOwnershipValidator(IEnumerable<IPermissionContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        _contributors = contributors.Where(x => x is not null).ToArray();
    }

    public PermissionOwnershipValidationResult Validate(IEnumerable<EndpointManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var consumers = entries.SelectMany(CreateConsumers).ToArray();
        return Validate(consumers);
    }

    public PermissionOwnershipValidationResult Validate(IEnumerable<PermissionConsumption> consumers)
    {
        ArgumentNullException.ThrowIfNull(consumers);

        var issues = new List<PermissionOwnershipIssue>();
        var declarations = new Dictionary<string, List<PermissionDeclaration>>(StringComparer.Ordinal);
        foreach (var contributor in _contributors)
        {
            var owner = string.IsNullOrWhiteSpace(contributor.OwnerId)
                ? contributor.GetType().FullName ?? contributor.GetType().Name
                : contributor.OwnerId.Trim();
            var contributorType = string.IsNullOrWhiteSpace(contributor.ContributorType)
                ? contributor.GetType().FullName ?? contributor.GetType().Name
                : contributor.ContributorType.Trim();

            foreach (var permission in contributor.Contribute().OfType<Permission>())
            {
                string key;
                try
                {
                    key = PermissionKey.Normalize(permission.Key);
                }
                catch (ArgumentException exception)
                {
                    issues.Add(new PermissionOwnershipIssue("InvalidCatalogPermission", permission.Key, owner, null, exception.Message));
                    continue;
                }

                if (key == PermissionKey.Wildcard)
                {
                    issues.Add(new PermissionOwnershipIssue("WildcardCatalogPermission", key, owner, null,
                        "The reserved administrative wildcard is a grant and cannot be catalog-owned."));
                    continue;
                }

                if (!declarations.TryGetValue(key, out var ownedBy))
                    declarations[key] = ownedBy = [];
                if (!ownedBy.Any(declaration => StringComparer.Ordinal.Equals(declaration.Owner, owner)))
                    ownedBy.Add(new PermissionDeclaration(key, owner, contributorType));
            }
        }

        foreach (var declaration in declarations.Where(pair => pair.Value.Count > 1))
        {
            foreach (var owner in declaration.Value)
                issues.Add(new PermissionOwnershipIssue("ConflictingCatalogOwners", declaration.Key, owner.Owner, null,
                    $"Permission is declared by multiple owners: {string.Join(", ", declaration.Value.Select(x => x.Owner).OrderBy(x => x, StringComparer.Ordinal))}."));
        }

        foreach (var consumer in consumers)
        {
            if (string.IsNullOrWhiteSpace(consumer.EndpointOwner))
            {
                issues.Add(new PermissionOwnershipIssue("MissingEndpointOwner", consumer.Permission, null, consumer.Endpoint,
                    "A permission consumer must identify its endpoint owner."));
                continue;
            }

            if (consumer.Permission == PermissionKey.Wildcard)
            {
                issues.Add(new PermissionOwnershipIssue("WildcardEndpointPermission", consumer.Permission, null, consumer.Endpoint,
                    "The reserved administrative wildcard cannot be the endpoint's declared permission."));
                continue;
            }

            if (!declarations.TryGetValue(consumer.Permission, out var ownedBy) || ownedBy.Count == 0)
            {
                issues.Add(new PermissionOwnershipIssue("MissingCatalogOwner", consumer.Permission, null, consumer.Endpoint,
                    $"No active permission contributor owns '{consumer.Permission}'."));
                continue;
            }

            if (ownedBy.Count > 1)
                issues.Add(new PermissionOwnershipIssue("AmbiguousCatalogOwner", consumer.Permission, null, consumer.Endpoint,
                    $"Active permission contributors disagree on the owner of '{consumer.Permission}'."));
        }

        return new PermissionOwnershipValidationResult(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Permission, StringComparer.Ordinal)
            .ThenBy(issue => issue.Endpoint?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ToArray());
    }

    public static PermissionOwnershipValidationResult Validate(
        IEnumerable<IPermissionContributor> contributors,
        IEnumerable<PermissionConsumption> consumers) =>
        new PermissionOwnershipValidator(contributors).Validate(consumers);

    private static IEnumerable<PermissionConsumption> CreateConsumers(EndpointManifestEntry entry)
    {
        var disposition = entry.SecurityDisposition;
        if (disposition is null || disposition.Kind != EndpointSecurityDispositionKind.Permission)
            yield break;

        if (string.IsNullOrWhiteSpace(disposition.Value))
        {
            yield return new PermissionConsumption(new EndpointIdentity(entry.Route.Value, entry.Methods.FirstOrDefault() ?? "*"), entry.Owner, "");
            yield break;
        }

        var permissions = ParsePermissions(disposition.Value);
        foreach (var permission in permissions)
        {
            // The wildcard is valid as a grant alongside an action permission. It is not a
            // catalog declaration and therefore is intentionally omitted from the consumers.
            if (permission != PermissionKey.Wildcard || permissions.Count == 1)
            {
                foreach (var method in entry.Methods)
                    yield return new PermissionConsumption(new EndpointIdentity(entry.Route.Value, method), entry.Owner, permission);
            }
        }
    }

    private static IReadOnlyList<string> ParsePermissions(string value)
    {
        var result = new PermissionPolicyCodec().Parse(value);
        if (result.Status == PermissionPolicyParseStatus.Valid && result.Descriptor is not null)
            return result.Descriptor.Permissions;

        try
        {
            return [PermissionKey.Normalize(value)];
        }
        catch (ArgumentException)
        {
            return [value.Trim()];
        }
    }

    private sealed record PermissionDeclaration(string Permission, string Owner, string ContributorType);
}

public sealed record PermissionConsumption
{
    public PermissionConsumption(EndpointIdentity endpoint, string endpointOwner, string permission)
    {
        Endpoint = endpoint;
        EndpointOwner = endpointOwner?.Trim() ?? string.Empty;
        Permission = Normalize(permission);
    }

    public EndpointIdentity Endpoint { get; }
    public string EndpointOwner { get; }
    public string Permission { get; }

    private static string Normalize(string permission)
    {
        try { return PermissionKey.Normalize(permission); }
        catch (ArgumentException) { return permission?.Trim() ?? string.Empty; }
    }
}

public sealed record PermissionOwnershipIssue(
    string Code,
    string Permission,
    string? CatalogOwner,
    EndpointIdentity? Endpoint,
    string Message);

public sealed record PermissionOwnershipValidationResult(IReadOnlyList<PermissionOwnershipIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, Issues.Select(issue =>
                $"{issue.Code}: {issue.Message} ({issue.Endpoint?.ToString() ?? issue.Permission}).")));
    }
}
