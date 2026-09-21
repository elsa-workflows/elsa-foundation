using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Validations.Core.Events;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-019 + SC-020 + Unit C FR-031 + research item R4: parametrised parity test driven by
/// the <see cref="CoreNamespacesWithCatalogs"/> table. Each row is a
/// <c>(Type anchorType, string projectName)</c> pair where <c>anchorType</c>'s namespace is
/// scanned for all public non-abstract concrete <see cref="IEvent"/> types and
/// <c>projectName</c> identifies the project whose directory contains the per-domain catalog.
/// <para>
/// Two properties are verified:
/// (a) every event in the namespace has a corresponding <c>### Xxx</c> heading in the catalog;
/// (b) every <c>### Xxx</c> heading in the catalog maps to a real event in at least one of
///     the namespaces registered for that catalog (handles the case where two Core namespaces
///     publish into the same feature catalog).
/// Scanning by namespace rather than assembly lets one Core assembly feed several catalogs:
/// Elsa.Workflows.Design.Core carries both the design mutation events and the reconciliation events.
/// </para>
/// <para>
/// To add a new domain: add one <c>yield return</c> line to <see cref="CoreNamespacesWithCatalogs"/>.
/// For domains where two namespaces publish events into the same catalog add one row per namespace;
/// the reverse-direction test unions all namespaces for that catalog automatically.
/// </para>
/// </summary>
/// <remarks>
/// The catalog filename was renamed from <c>DOMAIN_EVENTS.md</c> to <c>EVENTS.md</c> on
/// 2026-05-29 when the lifecycle/domain split landed (framework §2.22.1 amendment), then
/// folded into the per-domain <c>EXTENSION_POINTS.md</c> (with the events as an "Events"
/// section) on 2026-06-03 (framework §2.22.1 amendment). Catalog locations moved from
/// <c>*.Core</c> to composition-root feature projects on 2026-06-03 (framework §2.22.1
/// amendment). Only undecorated PascalCase <c>### Xxx</c> headings are matched, so decorated
/// contract/contributor headings in the other sections — e.g. <c>### `IDraftValidator` *(Core)*</c>
/// — are ignored. The <c>On</c> prefix that used to do this filtering was dropped from event
/// names on 2026-08-08 (framework §2.6.6 amendment, constitution v4.0.0).
/// </remarks>
public sealed class CatalogParityTests
{
    private const string CatalogFileName = "EXTENSION_POINTS.md";

    /// <summary>
    /// Table of (anchorType, projectName) pairs.
    /// anchorType — a type whose namespace is scanned for IEvent types.
    /// projectName — the project whose directory contains the EXTENSION_POINTS.md catalog.
    /// Add one line per namespace/catalog pair to extend coverage.
    /// </summary>
    public static IEnumerable<object[]> CoreNamespacesWithCatalogs()
    {
        // Workflows.Design mutation events → composition root at Elsa.Workflows.Design.Api
        yield return [typeof(DraftCreated), "Elsa.Workflows.Design.Api"];
        // Validation events → composition root at Elsa.Workflows.Design.Validations
        yield return [typeof(DraftValidating), "Elsa.Workflows.Design.Validations"];
        // Serialization converter-initializing event
        yield return [typeof(JsonPayloadConvertersInitializing), "Elsa.Serialization.SystemText"];
        // JavaScript rendering (declarations) event
        yield return [typeof(DeclarationsDocumentGenerating), "Elsa.Expressions.JavaScript.Rendering"];
        // Activity reconciliation event
        yield return [typeof(ActivityVersionsReconciling), "Elsa.Activities.Design.Reconciliation"];
        // Workflow reconciliation event
        yield return [typeof(WorkflowVersionsReconciling), "Elsa.Workflows.Design.Reconciliation"];
    }

    [Theory]
    [MemberData(nameof(CoreNamespacesWithCatalogs))]
    public void Every_event_has_a_catalog_heading(Type anchorType, string projectName)
    {
        var events = PublishedEventTypesIn(anchorType).Select(t => t.Name).ToHashSet();
        var headings = ReadCatalogHeadings(projectName);

        var missing = events.Except(headings).ToList();

        Assert.True(
            missing.Count == 0,
            $"{projectName}/{CatalogFileName} is missing catalog headings for: {string.Join(", ", missing)}"
        );
    }

    [Theory]
    [MemberData(nameof(CoreNamespacesWithCatalogs))]
    public void Every_catalog_heading_maps_to_a_real_event(Type anchorType, string projectName)
    {
        // For catalogs fed by several namespaces the reverse check must union ALL anchors
        // registered for this catalog so that headings contributed by a sibling namespace
        // don't appear stale. anchorType seeds the union; all other registered anchors for
        // the same catalog are appended and de-duplicated.
        var events = new[] { anchorType }
            .Concat(CoreNamespacesWithCatalogs()
                .Where(row => (string)row[1] == projectName)
                .Select(row => (Type)row[0]))
            .Distinct()
            .SelectMany(PublishedEventTypesIn)
            .Select(t => t.Name)
            .ToHashSet();

        var headings = ReadCatalogHeadings(projectName);
        var stale = headings.Except(events).ToList();

        Assert.True(
            stale.Count == 0,
            $"{projectName}/{CatalogFileName} has catalog headings with no corresponding event: {string.Join(", ", stale)}"
        );
    }

    private static IEnumerable<Type> PublishedEventTypesIn(Type anchorType) =>
        anchorType.Assembly.GetTypes()
            .Where(t => t.Namespace == anchorType.Namespace)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IEvent).IsAssignableFrom(t));

    /// <summary>
    /// Extracts level-3 markdown headings from the catalog beside the selected project file.
    /// Filters to headings that look like event class names per R4 — PascalCase alphanumeric +
    /// no decoration. Prose like <c>### DraftValidating: domain gate</c> would be ignored (the
    /// regex requires the heading to end at the class name), as would any decorated heading such
    /// as <c>### `IDraftValidator` *(Core)*</c>. An undecorated single-word level-3 heading in a
    /// catalog is therefore reserved for event names; <see cref="Every_catalog_heading_maps_to_a_real_event"/>
    /// fails if one appears that no event backs.
    /// </summary>
    private static IReadOnlySet<string> ReadCatalogHeadings(string projectName)
    {
        var path = FindCatalogPath(projectName);
        var content = File.ReadAllText(path);
        var matches = Regex.Matches(content, @"^### ([A-Z][A-Za-z0-9]*)\s*$", RegexOptions.Multiline);
        return matches.Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static string FindCatalogPath(string projectName)
    {
        // Walk up from the test bin folder to the repo root, then find the selected
        // project in the domain-tree source layout.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var solution = Path.Combine(dir.FullName, "Elsa.Server.slnx");
            if (File.Exists(solution))
            {
                var project = Directory
                    .EnumerateFiles(Path.Combine(dir.FullName, "src"), $"{projectName}.csproj", SearchOption.AllDirectories)
                    .SingleOrDefault();
                if (project is not null)
                {
                    var candidate = Path.Combine(Path.GetDirectoryName(project)!, CatalogFileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {CatalogFileName} beside {projectName}.csproj walking up from {AppContext.BaseDirectory}"
        );
    }
}
