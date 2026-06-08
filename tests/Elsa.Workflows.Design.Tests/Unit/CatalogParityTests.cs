using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-019 + SC-020 + Unit C FR-031 + research item R4: parametrised parity test driven by
/// the <see cref="CoreAssembliesWithCatalogs"/> table. Each row is a
/// <c>(Type anchorType, string projectDirName)</c> pair where <c>anchorType</c>'s assembly is
/// scanned for all public non-abstract concrete <see cref="IEvent"/> types and
/// <c>projectDirName</c> identifies the per-domain catalog at
/// <c>src/&lt;projectDirName&gt;/EXTENSION_POINTS.md</c>.
/// <para>
/// Two properties are verified:
/// (a) every event in the assembly has a corresponding <c>### OnXxx</c> heading in the catalog;
/// (b) every <c>### OnXxx</c> heading in the catalog maps to a real event in at least one of
///     the assemblies registered for that catalog (handles the multi-assembly case where two
///     Core assemblies publish into the same feature catalog).
/// </para>
/// <para>
/// To add a new domain: add one <c>yield return</c> line to <see cref="CoreAssembliesWithCatalogs"/>.
/// For domains where two assemblies publish events into the same catalog add one row per assembly;
/// the reverse-direction test unions all assemblies for that catalog automatically.
/// </para>
/// </summary>
/// <remarks>
/// The catalog filename was renamed from <c>DOMAIN_EVENTS.md</c> to <c>EVENTS.md</c> on
/// 2026-05-29 when the lifecycle/domain split landed (framework §2.22.1 amendment), then
/// folded into the per-domain <c>EXTENSION_POINTS.md</c> (with the events as an "Events"
/// section) on 2026-06-03 (framework §2.22.1 amendment). Catalog locations moved from
/// <c>*.Core</c> to composition-root feature projects on 2026-06-03 (framework §2.22.1
/// amendment). Only <c>### On…</c> headings are matched, so contract/contributor headings
/// in the other sections are ignored.
/// </remarks>
public sealed class CatalogParityTests
{
    private const string CatalogFileName = "EXTENSION_POINTS.md";

    /// <summary>
    /// Table of (anchorType, projectDirName) pairs.
    /// anchorType — a type whose Assembly is scanned for IEvent types.
    /// projectDirName — the src/ sub-directory that contains the EXTENSION_POINTS.md catalog.
    /// Add one line per assembly/catalog pair to extend coverage.
    /// </summary>
    public static IEnumerable<object[]> CoreAssembliesWithCatalogs()
    {
        // Workflows.Design mutation events → composition root at Elsa.Workflows.Design.Api
        yield return [typeof(OnDraftCreated), "Elsa.Workflows.Design.Api"];
        // Validation events → composition root at Elsa.Workflows.Design.Validations
        yield return [typeof(OnDraftValidating), "Elsa.Workflows.Design.Validations"];
        // Serialization converter-initializing event
        yield return [typeof(OnJsonPayloadConvertersInitializing), "Elsa.Serialization"];
        // JavaScript expression evaluation events
        yield return [typeof(OnEvaluatingScript), "Elsa.Expressions.JavaScript"];
        // JavaScript rendering (declarations) event
        yield return [typeof(OnDeclarationsDocumentGenerating), "Elsa.Expressions.JavaScript.Rendering"];
        // Activity constructor registration event (Elsa.Activities.Runtime.Core → Elsa.Activities.Runtime catalog)
        yield return [typeof(OnActivityConstructorsInitializing), "Elsa.Activities.Runtime"];
        // Activity reconciliation event
        yield return [typeof(OnActivityVersionsReconciling), "Elsa.Activities.Design.Reconciliation"];
        // Workflow reconciliation event
        yield return [typeof(OnWorkflowVersionsReconciling), "Elsa.Workflows.Design.Reconciliation"];
    }

    [Theory]
    [MemberData(nameof(CoreAssembliesWithCatalogs))]
    public void Every_event_has_a_catalog_heading(Type anchorType, string projectDirName)
    {
        var events = PublishedEventTypesIn(anchorType.Assembly).Select(t => t.Name).ToHashSet();
        var headings = ReadCatalogHeadings(projectDirName);

        var missing = events.Except(headings).ToList();

        Assert.True(
            missing.Count == 0,
            $"{projectDirName}/{CatalogFileName} is missing catalog headings for: {string.Join(", ", missing)}"
        );
    }

    [Theory]
    [MemberData(nameof(CoreAssembliesWithCatalogs))]
    public void Every_catalog_heading_maps_to_a_real_event(Type anchorType, string projectDirName)
    {
        // For multi-assembly catalogs (e.g. Elsa.Activities.Runtime receives events from
        // both Elsa.Activities.Runtime.Core and Elsa.Activities.Design.Core) the reverse
        // check must union ALL assemblies registered for this catalog so that headings
        // contributed by a sibling assembly don't appear stale.
        // anchorType.Assembly seeds the union; all other registered assemblies for the
        // same catalog are appended and de-duplicated.
        var events = new[] { anchorType.Assembly }
            .Concat(CoreAssembliesWithCatalogs()
                .Where(row => (string)row[1] == projectDirName)
                .Select(row => ((Type)row[0]).Assembly))
            .Distinct()
            .SelectMany(a => PublishedEventTypesIn(a))
            .Select(t => t.Name)
            .ToHashSet();

        var headings = ReadCatalogHeadings(projectDirName);
        var stale = headings.Except(events).ToList();

        Assert.True(
            stale.Count == 0,
            $"{projectDirName}/{CatalogFileName} has catalog headings with no corresponding event: {string.Join(", ", stale)}"
        );
    }

    private static IEnumerable<Type> PublishedEventTypesIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IEvent).IsAssignableFrom(t));

    /// <summary>
    /// Extracts level-3 markdown headings from <c>src/&lt;projectDirName&gt;/EXTENSION_POINTS.md</c>.
    /// Filters to headings that look like event class names per R4 — alphanumeric + starts
    /// with <c>On</c> + no decoration. Prose like <c>### OnDraftValidating: domain gate</c>
    /// would be ignored (the regex requires the heading to end at the class name).
    /// </summary>
    private static IReadOnlySet<string> ReadCatalogHeadings(string projectDirName)
    {
        var path = FindCatalogPath(projectDirName);
        var content = File.ReadAllText(path);
        var matches = Regex.Matches(content, @"^### (On[A-Za-z0-9]+)\s*$", RegexOptions.Multiline);
        return matches.Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static string FindCatalogPath(string projectDirName)
    {
        // Walk up from the test bin folder to the repo root, then resolve
        // src/<projectDirName>/EXTENSION_POINTS.md.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", projectDirName, CatalogFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate src/{projectDirName}/{CatalogFileName} walking up from {AppContext.BaseDirectory}"
        );
    }
}
