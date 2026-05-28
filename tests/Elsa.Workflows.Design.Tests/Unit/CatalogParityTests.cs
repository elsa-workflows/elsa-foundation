using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Events;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-019 + SC-020 + Unit C FR-031 + research item R4: parametrised parity test over both
/// <c>.Core</c> assemblies that publish domain events. For each (assembly, markdown path)
/// pair: (a) reflection-scan the assembly for all public non-abstract concrete
/// <see cref="IDomainEvent"/> types; (b) parse the corresponding <c>DOMAIN_EVENTS.md</c>
/// and extract the level-3 markdown headings (<c>### &lt;EventClassName&gt;</c>);
/// (c) assert bidirectional set equality.
/// </summary>
public sealed class CatalogParityTests
{
    public static IEnumerable<object[]> CoreAssembliesWithCatalogs()
    {
        yield return [typeof(OnDraftCreated).Assembly, "Elsa.Workflows.Design.Core"];
        yield return [typeof(OnDraftValidating).Assembly, "Elsa.Workflows.Design.Validations.Core"];
    }

    [Theory]
    [MemberData(nameof(CoreAssembliesWithCatalogs))]
    public void Every_event_has_a_catalog_heading(Assembly assembly, string projectDirName)
    {
        var events = DomainEventTypesIn(assembly).Select(t => t.Name).ToHashSet();
        var headings = ReadCatalogHeadings(projectDirName);

        var missing = events.Except(headings).ToList();

        Assert.True(missing.Count == 0,
            $"{projectDirName}/DOMAIN_EVENTS.md is missing catalog headings for: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(CoreAssembliesWithCatalogs))]
    public void Every_catalog_heading_maps_to_a_real_event(Assembly assembly, string projectDirName)
    {
        var events = DomainEventTypesIn(assembly).Select(t => t.Name).ToHashSet();
        var headings = ReadCatalogHeadings(projectDirName);

        var stale = headings.Except(events).ToList();

        Assert.True(stale.Count == 0,
            $"{projectDirName}/DOMAIN_EVENTS.md has catalog headings with no corresponding event: {string.Join(", ", stale)}");
    }

    private static IEnumerable<Type> DomainEventTypesIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t));

    /// <summary>
    /// Extracts level-3 markdown headings from <c>src/&lt;projectDirName&gt;/DOMAIN_EVENTS.md</c>.
    /// Filters to headings that look like event class names per R4 — alphanumeric + starts with
    /// <c>On</c> + no decoration. Prose like <c>## Workflow inputs (definition-bag, full CRUD)</c>
    /// is correctly ignored because it is a level-2 heading and contains parentheses.
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
        // Walk up from the test bin folder to the repo root, then resolve src/<projectDirName>/DOMAIN_EVENTS.md.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", projectDirName, "DOMAIN_EVENTS.md");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate src/{projectDirName}/DOMAIN_EVENTS.md walking up from {AppContext.BaseDirectory}");
    }
}
