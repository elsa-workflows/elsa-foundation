namespace Elsa.Architecture.Tests;

/// <summary>
/// The one way these guards decide which Elsa domain a path belongs to, so the InternalsVisibleTo rule, the
/// feature-class rule and any per-domain activation agree on what "the same domain" means.
/// </summary>
/// <remarks>
/// <para>
/// A domain is the folder directly under <c>Essentials/src</c> or <c>Essentials/tests</c>, so production code and its
/// tests map to the same key. <c>Workflows</c> is the exception: <c>Workflows.Design</c>, <c>Workflows.Runtime</c>,
/// <c>Workflows.Publishing</c> and the other <c>Workflows.*</c> folders are separate domains, so the key takes one
/// more segment. An extension is its own domain, keyed <c>Extensions/&lt;name&gt;</c> for both its <c>src</c> and
/// <c>tests</c> roots, so it never collides with an Essentials domain that shares its name.
/// </para>
/// <para>
/// Hosts (<c>Essentials/apps</c>), tools, samples and Studio belong to no domain.
/// </para>
/// </remarks>
internal static class ElsaDomains
{
    /// <summary>
    /// The domain key of a repository-relative path (either separator), or <see langword="null"/> when the path
    /// belongs to no domain.
    /// </summary>
    internal static string? Of(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments is ["Essentials", "src" or "tests", "Workflows", var workflowsDomain, _, ..])
            return $"Workflows.{workflowsDomain}";

        if (segments is ["Essentials", "src" or "tests", var domain, _, ..])
            return domain;

        if (segments is ["Extensions", var extension, "src" or "tests", _, ..])
            return $"Extensions/{extension}";

        return null;
    }
}
