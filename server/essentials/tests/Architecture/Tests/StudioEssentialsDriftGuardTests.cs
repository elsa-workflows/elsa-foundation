using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps the Studio Essentials app and the Essentials server in step (#5768): a Studio module is compiled into
/// <c>studio/src/apps/Elsa.Studio.Essentials.Host</c> exactly when <c>server/apps/Elsa.Essentials.Host</c> advertises the backend
/// capabilities that module requires.
/// </summary>
/// <remarks>
/// <para>
/// Both sides are read statically. <b>The server's capabilities</b> are the <c>elsa.api.*</c> ids declared, in a source file that
/// uses one of the declaration forms in <see cref="DeclarationTokens"/>, by a project in the server's transitive <c>ProjectReference</c> closure that
/// declares at least one feature its <c>shells.json</c> enables. That is a project-level approximation: a project carries one
/// domain's API, and its capability sources (the user-task source in the runtime API, for one) are registered by its enabled
/// feature rather than declared beside it, so a finer per-feature split would miss them. It can overstate what the server
/// advertises only when an enabled feature shares a project with a disabled one that declares a capability;
/// <see cref="Capability_declaring_server_projects_enable_every_feature_they_hold"/> keeps that from happening silently.
/// </para>
/// <para>
/// <b>A module's requirement</b> is every <c>elsa.api.*</c> id its <c>Client/src</c> names, tests excluded, less the ids
/// <see cref="AddOnOwnedIds"/> lists for it: screens the module carries for another add-on, which stay hidden until that add-on
/// is installed. A module is in exactly when that set is non-empty and fully served. A module that names no id has no backend
/// to match, so it sits in one of the two reviewed lists, <see cref="InSetWithoutBackend"/> or <see cref="FeedOnlyWithoutBackend"/>,
/// each with its reason.
/// </para>
/// </remarks>
public sealed class StudioEssentialsDriftGuardTests
{
    private const string ServerProject = "server/apps/Elsa.Essentials.Host/Elsa.Essentials.Host.csproj";
    private const string ServerShells = "server/apps/Elsa.Essentials.Host/shells.json";
    private const string StudioProject = "studio/src/apps/Elsa.Studio.Essentials.Host/Elsa.Studio.Essentials.Host.csproj";

    /// <summary>Ids a module names for screens another add-on serves. They do not count toward the module's requirement.</summary>
    internal static readonly IReadOnlyDictionary<string, (Func<string, bool> Owned, string Reason)> AddOnOwnedIds =
        new Dictionary<string, (Func<string, bool>, string)>(StringComparer.Ordinal)
        {
            ["Elsa.Studio.Workflows"] = (id => id.StartsWith("elsa.api.bpmn-", StringComparison.Ordinal),
                "The BPMN process and run screens live in the Workflows module and show once the BPMN server add-on advertises elsa.api.bpmn-*."),
            ["Elsa.Studio.Dashboard"] = (id => id == "elsa.api.studio-dashboard-authoring",
                "Dashboard question authoring in the Weaver dock (native/weaverAuthoring.tsx); Weaver's server add-on declares it."),
        };

    /// <summary>Modules that name no backend capability and ship compiled into Studio Essentials.</summary>
    internal static readonly IReadOnlyDictionary<string, (string[] ServerFeatures, string Reason)> InSetWithoutBackend =
        new Dictionary<string, (string[], string)>(StringComparer.Ordinal)
        {
            ["Elsa.Studio.Backends"] = ([], "Backend profiles and selection: part of every Studio shell, composed by Elsa.Studio.Hosting.Distribution."),
            ["Elsa.Studio.FeatureManagement"] = ([], "The Features page that enables installed add-ons; it talks to the Studio host's own administration API."),
            ["Elsa.Studio.ExpressionEditors.JavaScript"] = (["JavaScriptExpressions"], "The JavaScript expression editor; the server compiles in JavaScript expressions."),
            ["Elsa.Studio.ExpressionEditors.Liquid"] = (["Liquid"], "The Liquid expression editor; the server compiles in Liquid expressions."),
        };

    /// <summary>
    /// Modules that name no backend capability and reach Studio Essentials only from the feed. Where the module has a server
    /// side, <c>ServerRoot</c> names it, and the guard checks the reason: no project under it is in the Essentials server's
    /// closure, and the server advertises none of the capabilities it declares.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string? ServerRoot, string Reason)> FeedOnlyWithoutBackend =
        new Dictionary<string, (string?, string)>(StringComparer.Ordinal)
        {
            ["Elsa.Studio.Weaver.Workflows"] = ("extensions/Weaver/src", "Weaver's workflow tools; the Weaver server side is a feed add-on on the Essentials server."),
            ["Elsa.Studio.ExtensionBuilder"] = ("extensions/ExtensionBuilder/src", "A development tool; extensions and diagnostics stay feed add-ons (#5717)."),
            ["Elsa.Studio.ConsoleStream"] = (null, "A development tool that streams the Studio host's own console, with no server side; it stays a feed add-on (#5717)."),
            ["Elsa.Studio.Samples.WeatherForecast"] = (null, "The sample feature, with no server side; it stays a feed add-on (#5717)."),
        };

    /// <summary>
    /// Every form a production source file uses to declare a capability: a declaration record, the registration helper, the
    /// service-collection extensions and capability sources. <see cref="Every_capability_literal_sits_in_a_declaration_file"/>
    /// fails when a file names an <c>elsa.api.*</c> id without one, so a new declaration form cannot slip past the scan.
    /// </summary>
    internal static readonly string[] DeclarationTokens = ["ApiCapabilityDeclaration", "ApiCapabilityRegistration", "AddApiCapability", "IApiCapabilitySource"];

    private static readonly Regex CapabilityLiteral = new("""["'](?<id>elsa\.api\.[a-z0-9][a-z0-9.-]*)["']""", RegexOptions.Compiled);
    private static readonly Regex StudioModuleAttribute = new(@"^\s*\[StudioModule\(", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ShellFeatureName = new("""\[ShellFeature\(\s*(?:name:\s*)?"(?<name>[^"]+)""", RegexOptions.Compiled);

    [Fact]
    public void Studio_essentials_compiles_in_exactly_the_modules_the_essentials_server_serves()
    {
        var served = ServerCapabilities();
        var modules = StudioModules();
        var expected = modules.Where(module => IsServed(module.Key, module.Value, served)).Select(module => module.Key).ToHashSet(StringComparer.Ordinal);
        var actual = CompiledIntoStudioEssentials(modules.Keys);

        var missing = expected.Except(actual).Order(StringComparer.Ordinal).ToArray();
        var extra = actual.Except(expected).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0 && extra.Length == 0, string.Join(Environment.NewLine,
        [
            $"{StudioProject} is out of step with {ServerProject}.",
            .. missing.Select(module => $"  Missing: {module}, whose requirement [{string.Join(", ", Requirement(module, modules[module]))}] the server serves. Add its ProjectReference, its assembly in Program.cs and its feature in shells.json."),
            .. extra.Select(module => modules[module].Count == 0
                ? $"  Not in the set: {module} names no capability and is reviewed as feed-only ({(FeedOnlyWithoutBackend.TryGetValue(module, out var entry) ? entry.Reason : "unlisted")}). Remove it."
                : $"  Not served: {module}, which requires [{string.Join(", ", Requirement(module, modules[module]).Except(served))}] beyond what the server advertises. Remove it; it is a feed add-on."),
        ]));
    }

    /// <summary>A module without capability ids is reviewed by hand, so every one must be listed, and a listed one must still name none.</summary>
    [Fact]
    public void Every_module_without_a_backend_requirement_is_reviewed_in_one_list()
    {
        var modules = StudioModules();
        var unlisted = modules.Where(module => module.Value.Count == 0)
            .Select(module => module.Key)
            .Where(module => !InSetWithoutBackend.ContainsKey(module) && !FeedOnlyWithoutBackend.ContainsKey(module))
            .ToArray();
        var stale = InSetWithoutBackend.Keys.Concat(FeedOnlyWithoutBackend.Keys)
            .Where(module => !modules.TryGetValue(module, out var ids) || ids.Count > 0)
            .ToArray();

        Assert.True(unlisted.Length == 0, $"Studio modules that name no elsa.api.* capability must be listed in InSetWithoutBackend or FeedOnlyWithoutBackend, with a reason: {string.Join(", ", unlisted)}");
        Assert.True(stale.Length == 0, $"Listed as naming no capability, but missing or naming one now: {string.Join(", ", stale)}");
        Assert.Empty(InSetWithoutBackend.Keys.Intersect(FeedOnlyWithoutBackend.Keys));
    }

    /// <summary>A feed-only module with a server side is feed-only because the Essentials server leaves that side out.</summary>
    [Fact]
    public void Feed_only_modules_have_their_server_side_left_out_of_the_essentials_server()
    {
        var closure = Closure(Path.Join(RepoRoot, ServerProject));
        var served = ServerCapabilities();
        foreach (var (module, (serverRoot, _)) in FeedOnlyWithoutBackend)
        {
            if (serverRoot is null)
                continue;
            var root = Path.GetFullPath(Path.Join(RepoRoot, serverRoot)) + Path.DirectorySeparatorChar;
            Assert.True(Directory.Exists(root), $"{module}: {serverRoot} does not exist.");
            Assert.True(SourceFiles(root, "*.csproj").Any(), $"{module}: {serverRoot} holds no project, so the check below would pass with nothing to match.");
            var compiledIn = closure.Where(project => project.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.True(compiledIn.Length == 0, $"{module} is reviewed as feed-only, but the Essentials server compiles in {string.Join(", ", compiledIn)}.");
            var declared = SourceFiles(root, "*.cs").Select(File.ReadAllText).Where(IsDeclarationFile).SelectMany(CapabilityIds).ToArray();
            Assert.True(!declared.Any(served.Contains), $"{module} is reviewed as feed-only, but the Essentials server serves {string.Join(", ", declared.Where(served.Contains))}.");
        }
    }

    /// <summary>Every production file that names an <c>elsa.api.*</c> id uses a declaration form the server scan reads.</summary>
    [Fact]
    public void Every_capability_literal_sits_in_a_declaration_file()
    {
        var roots = new[] { Path.Join(RepoRoot, "server", "essentials", "src") }
            .Concat(Directory.EnumerateDirectories(Path.Join(RepoRoot, "extensions")).Select(extension => Path.Join(extension, "src")).Where(Directory.Exists));
        var unread = roots.SelectMany(root => SourceFiles(root, "*.cs"))
            .Where(file => File.ReadAllText(file) is var source && CapabilityLiteral.IsMatch(source) && !IsDeclarationFile(source))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .ToArray();

        Assert.True(unread.Length == 0, $"These files name an elsa.api.* id without a form in DeclarationTokens; add the form so the server scan reads them: {string.Join(", ", unread)}");
    }

    /// <summary>An in-set module without a capability id still depends on server features; those must stay enabled.</summary>
    [Fact]
    public void In_set_modules_without_a_capability_have_their_server_features_enabled()
    {
        var enabled = EnabledServerFeatures();
        var gaps = InSetWithoutBackend
            .SelectMany(module => module.Value.ServerFeatures.Where(feature => !enabled.Contains(feature)).Select(feature => $"{module.Key} needs {feature}"))
            .ToArray();

        Assert.True(gaps.Length == 0, $"{ServerShells} no longer enables: {string.Join("; ", gaps)}");
    }

    /// <summary>Every add-on-owned exclusion still matches an id its module names, so a stale exclusion cannot hide a requirement.</summary>
    [Fact]
    public void Add_on_owned_exclusions_match_ids_their_module_names()
    {
        var modules = StudioModules();
        foreach (var (module, (owned, _)) in AddOnOwnedIds)
        {
            Assert.True(modules.TryGetValue(module, out var ids), $"{module} is not a Studio module.");
            Assert.True(ids.Any(owned), $"{module} no longer names an id its add-on-owned exclusion matches; remove the exclusion.");
        }
    }

    /// <summary>
    /// The server scan counts a project's capabilities as served when any of its features is enabled. That holds only while a
    /// project that declares a capability has every feature it holds enabled; otherwise a disabled feature's capability would
    /// count as served and the guard would demand a Studio module the server does not advertise.
    /// </summary>
    [Fact]
    public void Capability_declaring_server_projects_enable_every_feature_they_hold()
    {
        var enabled = EnabledServerFeatures();
        var mixed = ServerProjects()
            .Where(project => project.Capabilities.Length > 0 && project.Features.Any(enabled.Contains) && !project.Features.All(enabled.Contains))
            .Select(project => $"{Path.GetRelativePath(RepoRoot, project.Path)} (disabled: {string.Join(", ", project.Features.Where(feature => !enabled.Contains(feature)))})")
            .ToArray();

        Assert.True(mixed.Length == 0, $"These server projects declare a capability and mix enabled and disabled features, so the project-level scan may overstate what Essentials serves. Map capabilities to features for them: {string.Join("; ", mixed)}");
    }

    /// <summary>An add-on-owned id stays unserved by the Essentials server; once it is served, the add-on is part of Essentials and the exclusion would hide a real requirement.</summary>
    [Fact]
    public void Add_on_owned_ids_are_not_served_by_the_essentials_server()
    {
        var served = ServerCapabilities();
        foreach (var (module, (owned, _)) in AddOnOwnedIds)
            Assert.True(!served.Any(owned), $"{module} excludes {string.Join(", ", served.Where(owned))} as add-on owned, but the Essentials server serves it. Remove the exclusion.");
    }

    /// <summary>
    /// The scans above pass trivially if they find nothing. This pins that each reads the real tree: every feature the server's
    /// shells.json enables is found in its closure, the capability set holds what Essentials is known to serve and lacks what it
    /// is known not to, and the module sweep finds the known modules on both sides of the line.
    /// </summary>
    [Fact]
    public void The_drift_scans_read_the_real_server_and_studio_sources()
    {
        var declared = ServerProjects().SelectMany(project => project.Features).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(EnabledServerFeatures().Where(feature => !declared.Contains(feature)));

        var served = ServerCapabilities();
        Assert.Superset(new HashSet<string>(["elsa.api.workflow-design", "elsa.api.runtime", "elsa.api.secrets", "elsa.api.user-tasks"]), served);
        Assert.DoesNotContain("elsa.api.agent", served);
        Assert.DoesNotContain("elsa.api.studio-dashboard", served);
        Assert.DoesNotContain("elsa.api.identity.external-authentication", served);

        var modules = StudioModules();
        Assert.True(modules.Count >= 20, $"Only {modules.Count} Studio modules found.");
        Assert.Contains("elsa.api.etl-design", modules["Elsa.Studio.Etl"]);
        Assert.Contains("Elsa.Studio.Workflows", CompiledIntoStudioEssentials(modules.Keys));
    }

    private static bool IsServed(string module, IReadOnlySet<string> ids, IReadOnlySet<string> served) =>
        ids.Count == 0
            ? InSetWithoutBackend.ContainsKey(module)
            : Requirement(module, ids).ToArray() is { Length: > 0 } requirement && requirement.All(served.Contains);

    private static IEnumerable<string> Requirement(string module, IReadOnlySet<string> ids) =>
        AddOnOwnedIds.TryGetValue(module, out var exclusion) ? ids.Where(id => !exclusion.Owned(id)) : ids;

    private static HashSet<string> EnabledServerFeatures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Join(RepoRoot, ServerShells)));
        return document.RootElement.GetProperty("CShells").GetProperty("Shells").EnumerateObject()
            .SelectMany(shell => shell.Value.GetProperty("Features").EnumerateObject())
            .Where(feature => feature.Value.ValueKind != JsonValueKind.False)
            .Select(feature => feature.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The capability ids the projects with an enabled feature declare.</summary>
    private static HashSet<string> ServerCapabilities()
    {
        var enabled = EnabledServerFeatures();
        return ServerProjects()
            .Where(project => project.Features.Any(enabled.Contains))
            .SelectMany(project => project.Capabilities)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<(string Path, string[] Features, string[] Capabilities)> ServerProjects()
    {
        foreach (var project in Closure(Path.Join(RepoRoot, ServerProject)))
        {
            var sources = SourceFiles(Path.GetDirectoryName(project)!, "*.cs").Select(File.ReadAllText).ToArray();
            var features = sources
                .SelectMany(source => ShellFeatureName.Matches(source).Select(match => match.Groups["name"].Value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var capabilities = sources.Where(IsDeclarationFile).SelectMany(CapabilityIds).Distinct(StringComparer.Ordinal).ToArray();
            yield return (project, features, capabilities);
        }
    }

    private static bool IsDeclarationFile(string source) => DeclarationTokens.Any(token => source.Contains(token, StringComparison.Ordinal));

    private static IEnumerable<string> CapabilityIds(string source) => CapabilityLiteral.Matches(source).Select(match => match.Groups["id"].Value);

    /// <summary>Every Studio module (a project with a <c>[StudioModule]</c> feature) by assembly name, with the capability ids its client names.</summary>
    private static Dictionary<string, IReadOnlySet<string>> StudioModules()
    {
        var roots = Directory.EnumerateDirectories(Path.Join(RepoRoot, "studio", "src", "essentials"))
            .Concat(Directory.EnumerateDirectories(Path.Join(RepoRoot, "extensions"))
                .Select(extension => Path.Join(extension, "studio"))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories));
        var modules = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var directory in roots)
        {
            var project = Directory.EnumerateFiles(directory, "*.csproj").SingleOrDefault();
            if (project is null || !SourceFiles(directory, "*.cs").Any(file => StudioModuleAttribute.IsMatch(File.ReadAllText(file))))
                continue;
            var client = Path.Join(directory, "Client", "src");
            var ids = !Directory.Exists(client)
                ? []
                : SourceFiles(client, "*.ts*")
                    .Where(file => !file.Split(Path.DirectorySeparatorChar).Contains("__tests__") && !Path.GetFileName(file).Contains(".test.", StringComparison.Ordinal))
                    .SelectMany(file => CapabilityLiteral.Matches(File.ReadAllText(file)).Select(match => match.Groups["id"].Value))
                    .ToHashSet(StringComparer.Ordinal);
            modules[Path.GetFileNameWithoutExtension(project)] = ids;
        }

        return modules;
    }

    /// <summary>The Studio modules in the Studio Essentials app's transitive project closure.</summary>
    private static HashSet<string> CompiledIntoStudioEssentials(IEnumerable<string> modules)
    {
        var closure = Closure(Path.Join(RepoRoot, StudioProject)).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        return modules.Where(closure.Contains).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Closure(string root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(root)]);
        while (pending.TryPop(out var project))
        {
            if (!seen.Add(project))
                continue;
            foreach (var reference in ProjectGraph.ReferencedProjectPaths(project))
                pending.Push(reference);
        }

        return seen;
    }

    private static IEnumerable<string> SourceFiles(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !file.Split(Path.DirectorySeparatorChar).Contains("node_modules"));
}
