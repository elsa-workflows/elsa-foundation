using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Readability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The composition test of framework constitution §2.23.1: shells composed from every feature, in which every service type
/// each feature registers resolves.
/// </summary>
/// <remarks>
/// <para>
/// The features are not listed anywhere: they are every concrete <see cref="IShellFeature"/> in every packable runtime
/// library under <c>src/</c>, found by reflection over the assemblies this project loads, and a packable library this
/// project does not load fails the first test by name. CShells composes them the way a host composes a shell: its catalog
/// discovers them, it adds the <c>DependsOn</c> closure in dependency order, binds each feature's settings (validating
/// those it is given), and activation runs the shell's initializers and its <c>ValidateOnStart</c> checks, maps every web
/// feature's endpoints and composes the shell's middleware pipeline. CShells logs some failures and carries on past them,
/// so what it logs as an error, or as a skip, fails the test as well.
/// </para>
/// <para>
/// Every feature goes into one shell, except where two features are <see cref="Alternatives"/> that refuse to share one:
/// then a second shell holds the other side, so each feature is still composed and resolved.
/// </para>
/// <para>
/// Attribution: CShells constructs each feature through <see cref="IShellFeatureFactory"/> immediately before calling its
/// <c>ConfigureServices</c>, in order, against one service collection. A recorder composed ahead of every feature captures
/// that collection, so each construction closes the previous feature's span. A failing service is named with the feature
/// whose <c>ConfigureServices</c> added it to that shell; where two features add the same registration with a
/// <c>TryAdd</c>, that is the first of them in dependency order.
/// </para>
/// </remarks>
public sealed class FeatureCompositionTests
{
    /// <summary>
    /// Features that own the same contract and refuse, each with its own message, to share a shell. The first shell holds
    /// every feature except the second sides, the second shell every feature except the first sides; a feature that depends
    /// on a left-out one is left out with it.
    /// </summary>
    private static readonly Alternative[] Alternatives =
    [
        new("The complete runtime EF family and its per-family features register the same stores.",
            ["WorkflowsRuntimeEntityFrameworkCore"],
            [
                "WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence",
                "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence",
                "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence",
                "WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence",
                "WorkflowsRuntimeAlterationEntityFrameworkCorePersistence",
                "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence",
                "WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence"
            ]),
        new("A shell runs exactly one agent harness.", ["AnthropicAgent"], ["GitHubCopilotAgent"]),
    ];

    [Fact]
    public void Every_packable_runtime_library_is_loaded_so_its_features_are_composed()
    {
        var missing = PackableRuntimeLibraries.Value
            .Where(library => library.Assembly is null)
            .Select(library => $"{library.Name} ({library.ProjectPath})")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These packable libraries are not referenced by Elsa.Architecture.Tests, so the composition test cannot see " +
            "their features. Add a ProjectReference for each:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public async Task Every_feature_composes_and_each_service_it_registers_resolves()
    {
        var features = FeatureTypes.Value;
        // A sweep that finds nothing passes vacuously; this floor sits well below today's count.
        Assert.True(features.Count >= 100, $"Only {features.Count} features were discovered; discovery has stopped finding them.");

        await using var host = await CompositionHost.StartAsync(Compositions(features));
        var failures = new List<string>();

        // The two hand-kept tables name features and settings; one that matches nothing has gone stale and configures
        // nothing, because CShells ignores a setting no property binds.
        var names = features.Select(ShellBuilder.ResolveFeatureName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        failures.AddRange(Alternatives
            .SelectMany(alternative => alternative.First.Concat(alternative.Second))
            .Concat(host.Settings.FeatureNames)
            .Where(name => !names.Contains(name))
            .Select(name => $"'{name}' is named in this test's alternatives or settings but is not a feature."));
        failures.AddRange(host.Settings.Unbound(features));

        foreach (var shell in host.Shells)
            failures.AddRange(await host.ResolveAsync(shell, names));

        // A shell that refused to activate stopped composing at the failing feature, so the features after it were never
        // composed: the refusal is the cause, and listing them would bury it.
        var composed = host.Recordings.SelectMany(recording => recording.Spans).Select(span => span.Feature).ToHashSet();
        if (!host.AnyRefused)
            failures.AddRange(features
                .Where(feature => !composed.Contains(feature))
                .Select(feature => $"{Describe(feature)}: composed by no shell" +
                                   (feature.IsVisible ? "." : "; CShells discovers exported feature types only.")));

        failures.AddRange(host.Log.Failures());

        Assert.True(
            failures.Count == 0,
            "Composing every feature failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Resolves every registration of a service type: the enumerable constructs each one, where a single resolve would
    /// construct only the last and leave a broken earlier registration unexercised.
    /// </summary>
    private static string? Resolve(IServiceProvider services, string shell, string feature, ServiceKey key)
    {
        try
        {
            _ = key.Key is null
                ? services.GetServices(key.Type).ToArray()
                : services.GetKeyedServices(key.Type, key.Key).ToArray();
            return null;
        }
        catch (Exception exception)
        {
            return $"[{shell}] {feature}: {key} -> {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static readonly Regex FeatureMention = new(@"feature '(?<name>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Describe(Type feature) => $"{ShellBuilder.ResolveFeatureName(feature)} ({feature.FullName})";

    private sealed record Alternative(string Reason, string[] First, string[] Second);

    private sealed record Composition(string Shell, IReadOnlyList<Type> Features);

    /// <summary>
    /// The shells: one when no features are alternatives, else two, each leaving out one side of every
    /// <see cref="Alternative"/> together with the features that depend on it.
    /// </summary>
    private static IReadOnlyList<Composition> Compositions(IReadOnlyList<Type> features)
    {
        if (Alternatives.Length == 0)
            return [new("every-feature", features)];

        var catalog = FeatureDiscovery.DiscoverFeatures(features.Select(feature => feature.Assembly).Distinct())
            .ToDictionary(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);
        var resolver = new FeatureDependencyResolver();

        IReadOnlyList<Type> Without(IEnumerable<string> leftOut)
        {
            var excluded = leftOut.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return features
                .Where(feature =>
                {
                    var name = ShellBuilder.ResolveFeatureName(feature);
                    return !excluded.Contains(name) &&
                           (!catalog.ContainsKey(name) || !resolver.ResolveDependencies(name, catalog).Any(excluded.Contains));
                })
                .ToArray();
        }

        return
        [
            new("every-feature", Without(Alternatives.SelectMany(alternative => alternative.Second))),
            new("every-alternative", Without(Alternatives.SelectMany(alternative => alternative.First)))
        ];
    }

    private sealed record PackableRuntimeLibrary(string Name, string ProjectPath, Assembly? Assembly);

    /// <summary>
    /// Every project under <c>src/</c> that ships as a runtime library: packable, and neither an executable (a host or a
    /// dotnet tool) nor a Roslyn component, none of which a shell loads.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<PackableRuntimeLibrary>> PackableRuntimeLibraries = new(() =>
        ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => ModuleRoots.IsNotTestFile(RepoRoot, path) && !IsBuildOutput(path))
            .Select(path => (Path: path, Project: XDocument.Load(path)))
            .Where(project => IsPackableRuntimeLibrary(project.Project))
            .Select(project =>
            {
                var name = Property(project.Project, "AssemblyName") ?? Path.GetFileNameWithoutExtension(project.Path);
                return new PackableRuntimeLibrary(name, Path.GetRelativePath(RepoRoot, project.Path).Replace('\\', '/'), TryLoad(name));
            })
            .OrderBy(library => library.Name, StringComparer.Ordinal)
            .ToArray());

    private static bool IsPackableRuntimeLibrary(XDocument project) =>
        !string.Equals(Property(project, "IsPackable"), "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Property(project, "OutputType"), "Exe", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Property(project, "IsRoslynComponent"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? Property(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value.Trim()).LastOrDefault();

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Every concrete feature type in the packable runtime libraries, public or not.</summary>
    private static readonly Lazy<IReadOnlyList<Type>> FeatureTypes = new(() =>
        PackableRuntimeLibraries.Value
            .Select(library => library.Assembly)
            .OfType<Assembly>()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IShellFeature).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray());

    /// <summary>A service type with its key, as one <see cref="ServiceDescriptor"/> registers it.</summary>
    private sealed record ServiceKey(Type Type, object? Key)
    {
        public override string ToString() => Key is null ? Type.FullName ?? Type.Name : $"{Type.FullName} (key '{Key}')";
    }

    /// <summary>The registrations one feature added to a shell.</summary>
    private sealed record FeatureSpan(Type Feature, string Label, IReadOnlyList<ServiceDescriptor> Registrations)
    {
        /// <summary>
        /// The distinct service types the span registers, open generics left out: those resolve only once closed over a type
        /// argument, which the shell does wherever a registered service consumes one.
        /// </summary>
        public IEnumerable<ServiceKey> ServiceKeys => Registrations
            .Where(registration => !registration.ServiceType.IsGenericTypeDefinition)
            .Select(registration => new ServiceKey(registration.ServiceType, registration.ServiceKey))
            .Distinct();
    }

    /// <summary>
    /// What one shell's features registered, feature by feature, in the order CShells configured them.
    /// </summary>
    private sealed class ShellRecording(string shell)
    {
        private readonly List<FeatureSpan> _spans = [];
        private readonly List<Type> _postConfigurers = [];
        private HashSet<ServiceDescriptor> _before = new(ReferenceEqualityComparer.Instance);
        private IServiceCollection? _services;
        private Type? _current;
        private bool _configured;

        public string Shell { get; } = shell;

        public IReadOnlyList<FeatureSpan> Spans => _spans;

        /// <summary>CShells constructed a feature: the previous feature's <c>ConfigureServices</c> has returned.</summary>
        public void Constructed(Type feature, object instance)
        {
            if (_configured)
                return;
            if (_services is null)
                throw new InvalidOperationException($"CShells constructed '{feature.FullName}' before the recorder, so its registrations cannot be attributed.");

            CloseSpan();
            _current = feature;
            if (instance is IPostConfigureShellServices)
                _postConfigurers.Add(feature);
        }

        /// <summary>The shell is built: attribute what the post-configure phase added to the features that post-configure.</summary>
        public void Complete()
        {
            if (_services is null)
                return;

            var added = Added();
            foreach (var feature in _postConfigurers)
                _spans.Add(new FeatureSpan(feature, $"{Describe(feature)} post-configure", added));
        }

        public void Attach(IServiceCollection services)
        {
            if (_current is not null || _spans.Count > 0)
                throw new InvalidOperationException("The recorder was not the first feature CShells configured.");

            _services = services;
            Mark();
        }

        public void EndConfigurePhase()
        {
            CloseSpan();
            _current = null;
            _configured = true;
        }

        private void CloseSpan()
        {
            if (_current is not null)
                _spans.Add(new FeatureSpan(_current, Describe(_current), Added()));
            Mark();
        }

        private IReadOnlyList<ServiceDescriptor> Added() => _services!.Where(descriptor => !_before.Contains(descriptor)).ToArray();

        private void Mark() => _before = new HashSet<ServiceDescriptor>(_services!, ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Decorates CShells' own feature factory, so every feature is still constructed by CShells, and hands out a recorder
    /// for the recorder's catalog entry.
    /// </summary>
    private sealed class RecordingFeatureFactory(Type recorderType) : IShellFeatureFactory
    {
        private readonly Dictionary<string, ShellRecording> _recordings = new(StringComparer.Ordinal);
        private IShellFeatureFactory? _inner;

        public IReadOnlyCollection<ShellRecording> Recordings => _recordings.Values;

        public RecordingFeatureFactory Decorating(IShellFeatureFactory inner)
        {
            _inner = inner;
            return this;
        }

        public T CreateFeature<T>(Type featureType, ShellSettings? shellSettings = null, ShellFeatureContext? featureContext = null)
            where T : class
        {
            var shell = shellSettings?.Id.Name ?? throw new InvalidOperationException("CShells constructed a feature without its shell's settings.");
            if (!_recordings.TryGetValue(shell, out var recording))
                _recordings[shell] = recording = new ShellRecording(shell);

            if (featureType == recorderType)
                return (T)(object)new Recorder(recording);

            var feature = _inner!.CreateFeature<T>(featureType, shellSettings, featureContext);
            recording.Constructed(featureType, feature);
            return feature;
        }

        /// <summary>
        /// Composed first because it is requested first and depends on nothing. Its post-configure step runs first too, so
        /// it ends the last feature's span before any other feature post-configures.
        /// </summary>
        private sealed class Recorder(ShellRecording recording) : IShellFeature, IPostConfigureShellServices
        {
            public void ConfigureServices(IServiceCollection services) => recording.Attach(services);

            public void PostConfigureServices(IServiceCollection services) => recording.EndConfigurePhase();
        }
    }

    /// <summary>
    /// The recorder's catalog entry. CShells' catalog scans whole assemblies, and this test assembly carries stand-in
    /// features that reuse real feature names (<see cref="DomainManagementApiCompositionTests"/>), so the entry lives in a
    /// one-type assembly emitted at run time. The factory never constructs it; it hands out the recorder instead.
    /// </summary>
    public abstract class RecorderCatalogEntry : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            throw new InvalidOperationException("The recording feature factory constructs the recorder in place of this entry.");
    }

    private static Type EmitRecorderCatalogEntry()
    {
        var module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("Elsa.Architecture.Tests.CompositionRecorder"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("CompositionRecorder");
        var type = module.DefineType("CompositionRecorderFeature", TypeAttributes.Public | TypeAttributes.Sealed, typeof(RecorderCatalogEntry));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }

    /// <summary>
    /// The least configuration under which every feature composes: each entry is a setting a feature refuses to compose or
    /// activate without, or a path that would otherwise land in the working directory. Everything else runs on its
    /// defaults. Files live in <paramref name="directory"/>.
    /// </summary>
    private sealed class MinimumSettings(string directory)
    {
        /// <summary>A 32-byte key for the durable runtime's paging cursors, which refuse to sign with less.</summary>
        private const string SigningKey = "feature-composition-test-signing-key";

        /// <summary>Settings every feature declaring a property of that name receives.</summary>
        private Dictionary<string, object> ByProperty(string shell) => new(StringComparer.Ordinal)
        {
            // Every EF module in the shell's own database file. Several modules default to a file of their own in the
            // working directory rather than to the shared connection.
            ["ConnectionString"] = $"Data Source={Path.Join(directory, $"{shell}.db")}",
            ["RecoveryContinuationSigningKey"] = SigningKey,
            ["HierarchyCursorSigningKey"] = SigningKey,
            // The zero-configuration path: ephemeral keys and in-process stores, honored only in Development, where this
            // host runs. Without it the identity features refuse to activate until a key and seed credentials are supplied.
            ["IsDevelopmentOrDemo"] = true,
        };

        private readonly Dictionary<string, Dictionary<string, object>> _byFeature = ByFeature(directory);

        public IEnumerable<string> FeatureNames => _byFeature.Keys;

        /// <summary>
        /// The per-feature settings no property of their feature takes. CShells binds a feature's public settable
        /// properties and ignores every other key without a word, so a misspelt or renamed setting would leave the
        /// feature on its default.
        /// </summary>
        public IEnumerable<string> Unbound(IEnumerable<Type> features) => features
            .SelectMany(feature => (_byFeature.GetValueOrDefault(ShellBuilder.ResolveFeatureName(feature)) ?? [])
                .Where(setting => !Binds(feature, setting.Key))
                .Select(setting => $"{Describe(feature)}: setting '{setting.Key}' names no property, so CShells ignores it."));

        /// <summary>Whether the path of a setting's key leads through properties: settable ones on the feature itself.</summary>
        private static bool Binds(Type feature, string key)
        {
            var segments = key.Split(':');
            var property = feature.GetProperty(segments[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property?.SetMethod?.IsPublic != true)
                return false;

            var type = property.PropertyType;
            foreach (var segment in segments.Skip(1))
            {
                var next = int.TryParse(segment, out _)
                    ? type.GetElementType() ?? type.GetGenericArguments().LastOrDefault() ?? typeof(object)
                    : type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType;
                if (next is null)
                    return false;
                type = next;
            }

            return true;
        }

        public Dictionary<string, object> For(Type feature, string shell)
        {
            var settings = ByProperty(shell)
                .Where(setting => feature.GetProperty(setting.Key, BindingFlags.Public | BindingFlags.Instance)?.SetMethod?.IsPublic == true)
                .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);
            foreach (var (key, value) in _byFeature.GetValueOrDefault(ShellBuilder.ResolveFeatureName(feature)) ?? [])
                settings[key] = value;
            return settings;
        }

        private static Dictionary<string, Dictionary<string, object>> ByFeature(string directory)
        {
            var activities = Path.Join(directory, "activities.json");
            File.WriteAllText(activities, "[]");
            var workflows = Directory.CreateDirectory(Path.Join(directory, "workflows")).FullName;

            return new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["JsonActivityReconciliation"] = new() { ["Options:SourceId"] = "composition", ["Options:FilePath"] = activities },
                ["JsonWorkflowReconciliation"] = new() { ["Options:SourceId"] = "composition", ["Options:FolderPath"] = workflows },
                ["JsonWorkflowArtifactReconciliation"] = new() { ["Options:SourceId"] = "composition", ["Options:FolderPath"] = workflows },
                ["FileSystemDistributedLocking"] = new() { ["LocksFolderPath"] = Path.Join(directory, "locks") },
                // Medallion's providers connect when a lock is taken, not when they are constructed, and nothing takes one here.
                ["DatabaseDistributedLocking"] = new() { ["Provider"] = "PostgreSql", ["ConnectionString"] = "Host=127.0.0.1;Database=elsa" },
                // Activation clones the remote, so the remote is a local repository and the clone stays in the directory.
                ["WorkflowsDesignGitReconciliation"] = new() { ["RemoteUrl"] = GitRemote(directory), ["LocalCachePath"] = Path.Join(directory, "git-clone") },
            };
        }

        /// <summary>A repository with one commit on <c>main</c>, isolated from the machine's git configuration.</summary>
        private static string GitRemote(string directory)
        {
            var remote = Directory.CreateDirectory(Path.Join(directory, "git-remote")).FullName;
            string[][] commands =
            [
                ["init", "-b", "main"],
                ["config", "maintenance.auto", "false"],
                ["-c", "commit.gpgsign=false", "-c", "core.hooksPath=.git/no-hooks", "-c", "user.name=Composition", "-c", "user.email=composition@elsa.local",
                    "commit", "--allow-empty", "-m", "init"]
            ];
            foreach (var arguments in commands)
            {
                var (exitCode, output) = ChildProcess.Run("git", ["-C", remote, .. arguments]);
                if (exitCode != 0)
                    throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed:{Environment.NewLine}{output}");
            }

            return remote;
        }
    }

    private sealed class CompositionHost(
        WebApplication app,
        RecordingFeatureFactory factory,
        MinimumSettings settings,
        CapturedLog log,
        string directory) : IAsyncDisposable
    {
        private readonly Dictionary<string, IShell> _active = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _refused = new(StringComparer.Ordinal);

        public IEnumerable<string> Shells => _active.Keys.Concat(_refused.Keys).Order(StringComparer.Ordinal);

        public bool AnyRefused => _refused.Count > 0;

        public IReadOnlyCollection<ShellRecording> Recordings => factory.Recordings;

        public MinimumSettings Settings => settings;

        public CapturedLog Log => log;

        /// <summary>
        /// Builds and starts the host the way the Workbench's <c>Program.cs</c> does, then activates every shell. A failure
        /// before the host exists disposes what was built and deletes the directory, so a throw leaks nothing.
        /// </summary>
        public static async Task<CompositionHost> StartAsync(IReadOnlyList<Composition> compositions)
        {
            var directory = Directory.CreateTempSubdirectory("elsa-feature-composition-").FullName;
            WebApplication? app = null;
            try
            {
                var recorderType = EmitRecorderCatalogEntry();
                var factory = new RecordingFeatureFactory(recorderType);
                var settings = new MinimumSettings(directory);
                var log = new CapturedLog();

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = "Development",
                    ContentRootPath = directory
                });
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders().AddProvider(log).SetMinimumLevel(LogLevel.Warning);

                builder.Services.AddSingleton<IShellFeatureFactory>(services => factory.Decorating(new DefaultShellFeatureFactory(services)));
                AddHostServices(builder.Services, directory);

                var features = compositions.SelectMany(composition => composition.Features).Distinct();
                builder.Services.AddCShellsAspNetCore(shells =>
                {
                    shells
                        .WithAssemblies([recorderType.Assembly, .. features.Select(feature => feature.Assembly).Distinct()])
                        .WithAuthenticationAndAuthorization()
                        .WithWebRouting(options => options.EnablePathRouting = true);
                    foreach (var composition in compositions)
                        shells.AddShell(composition.Shell, shell => Compose(shell, composition, recorderType, settings));
                });
                builder.Services.AddAuthentication();
                builder.Services.AddAuthorization();

                app = builder.Build();
                // Without it CShells prepares no shell's endpoints or middleware pipeline: every web feature's
                // MapEndpoints and every middleware feature is skipped with a warning.
                app.MapShells();
                app.UseAuthentication();
                app.UseAuthorization();
                await app.StartAsync();

                var host = new CompositionHost(app, factory, settings, log, directory);
                foreach (var composition in compositions)
                    await host.ActivateAsync(composition.Shell);
                return host;
            }
            catch
            {
                if (app is not null)
                    await app.DisposeAsync();
                DeleteDirectory(directory);
                throw;
            }
        }

        /// <summary>
        /// What a host composes on its own container for its shells, because no feature can bring it. Each mirrors the
        /// Workbench's <c>Program.cs</c>, with a stand-in where the real thing is a server or a package feed.
        /// </summary>
        private static void AddHostServices(IServiceCollection services, string directory)
        {
            // A shell runs its ValidateOnStart checks as it activates only when the host asks for it (#2331).
            services.AddShellStartupValidation();
            // OpenIddict's vendor store is the host's choice; the Workbench registers its EF store the same way.
            services.AddDbContext<OpenIddictStoreContext>(options => options.UseSqlite($"Data Source={Path.Join(directory, "openiddict.db")}"));
            services.AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictStoreContext>());
            // The Workbench shares Nuplane's admin operations with its shells; the module catalog reads packages through them.
            services.AddSingleton<INuplaneAdminOperations, NoPackageFeed>();
        }

        private static void Compose(
            ShellBuilder shell,
            Composition composition,
            Type recorderType,
            MinimumSettings settings)
        {
            // A path of its own, as path routing gives each shell, so the two shells' endpoints do not overlap.
            shell.WithConfiguration("WebRouting:Path", composition.Shell);
            shell.WithFeature(recorderType);
            foreach (var feature in composition.Features)
            {
                var featureSettings = settings.For(feature, composition.Shell);
                if (featureSettings.Count == 0)
                    shell.WithFeature(feature);
                else
                    shell.WithFeature(feature, configure => configure.WithSettings(featureSettings));
            }
        }

        private async Task ActivateAsync(string shell)
        {
            try
            {
                _active[shell] = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shell);
            }
            catch (Exception exception)
            {
                _refused[shell] = exception;
            }

            factory.Recordings.SingleOrDefault(recording => recording.Shell == shell)?.Complete();
        }

        public async Task<IReadOnlyList<string>> ResolveAsync(string shell, IReadOnlySet<string> featureNames)
        {
            if (_refused.TryGetValue(shell, out var refusal))
                return [$"[{shell}] the shell refused to activate{NamedFeatures(refusal, featureNames)}: {refusal}"];

            var recording = factory.Recordings.SingleOrDefault(candidate => candidate.Shell == shell);
            if (recording is null || recording.Spans.Sum(span => span.Registrations.Count) == 0)
                return [$"[{shell}] no registration was attributed to any feature, so nothing was resolved: the recorder no longer sees the collection CShells configures features against."];

            await using var scope = _active[shell].BeginScope();
            return recording.Spans
                .SelectMany(span => span.ServiceKeys.Select(key => Resolve(scope.ServiceProvider, shell, span.Label, key)))
                .OfType<string>()
                .ToArray();
        }

        /// <summary>
        /// The features a refusal names, as CShells names one ("Failed to configure services for feature 'X'") and as a
        /// feature's own message may, so the one failure line says whose composition broke.
        /// </summary>
        private static string NamedFeatures(Exception refusal, IReadOnlySet<string> featureNames)
        {
            var named = Chain(refusal)
                .SelectMany(exception => FeatureMention.Matches(exception.Message).Select(match => match.Groups["name"].Value))
                .Where(featureNames.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return named.Length == 0 ? "" : $" (feature {string.Join(", ", named)})";

            static IEnumerable<Exception> Chain(Exception? exception)
            {
                for (; exception is not null; exception = exception.InnerException)
                    yield return exception;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            DeleteDirectory(directory);
        }

        private static void DeleteDirectory(string directory)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file a shell service still holds open, or a read-only git object; the OS reclaims its temporary directory.
            }
        }
    }

    /// <summary>OpenIddict's vendor entity model, which the host owns.</summary>
    private sealed class OpenIddictStoreContext(DbContextOptions<OpenIddictStoreContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.UseOpenIddict();
            base.OnModelCreating(builder);
        }
    }

    /// <summary>A package feed that is never asked: composing and resolving read no packages.</summary>
    private sealed class NoPackageFeed : INuplaneAdminOperations
    {
        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// What the host and its shells log at Warning and above. CShells logs some failures and carries on, so a feature it
    /// skipped would otherwise leave this test green: these entries fail it.
    /// </summary>
    private sealed class CapturedLog : ILoggerProvider
    {
        /// <summary>
        /// The CShells warnings that mean it skipped something and carried on; every other warning is ordinary noise.
        /// Endpoint mapping failures are logged as errors, which fail the test anyway.
        /// </summary>
        private static readonly string[] SkipWarnings =
        [
            // ShellProviderBuilder: binding a feature's settings threw, and the feature was configured on its defaults.
            "Feature will use defaults",
            // FeatureConfigurationBinder: one setting could not be converted, and its property kept its default.
            "Failed to bind property",
            // ShellEndpointRegistrationHandler: no endpoint or middleware of the shell was prepared.
            "MapShells() has not run yet"
        ];

        private readonly ConcurrentQueue<Entry> _entries = new();

        public IEnumerable<string> Failures() => _entries
            .Where(entry => entry.Level >= LogLevel.Error ||
                            SkipWarnings.Any(warning => entry.Template.Contains(warning, StringComparison.Ordinal)))
            .Select(entry => $"[log] {entry.Level} {entry.Category}: {entry.Message}" +
                             (entry.Exception is null ? "" : $" ({entry.Exception.GetType().Name}: {entry.Exception.Message})"));

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed record Entry(string Category, LogLevel Level, string Template, string Message, Exception? Exception);

        private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var message = formatter(state, exception);
                var template = (state as IEnumerable<KeyValuePair<string, object?>>)?
                    .FirstOrDefault(value => value.Key == "{OriginalFormat}").Value as string;
                entries.Enqueue(new Entry(category, logLevel, template ?? message, message, exception));
            }
        }
    }
}
