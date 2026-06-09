using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Elsa.Activities.Design.Reconciliation.Clr.Exceptions;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Runtime.Core.Attributes;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Branch-covered unit tests for <see cref="ActivityTypeVersionResolver"/> (FR-020, §2.23.2). The
/// resolver now reads its own metadata from a <see cref="Type"/> + <see cref="Assembly"/>, so each
/// precedence branch is driven by a real reflection-only input rather than hand-fed strings:
/// <list type="bullet">
/// <item>The valid <c>[Version]</c> branch and the informational-version branch use fixture
/// activities scanned in the default load context (the fixture assembly ships a valid <c>2.1.0</c>
/// informational version).</item>
/// <item>The invalid <c>[Version]</c> branch and the assembly-version-mapping branch use freshly
/// emitted assemblies loaded through a <see cref="MetadataLoadContext"/> — the only way to control
/// the attribute, the assembly version, and the informational version independently of the test
/// host. (A malformed <c>[Version]</c> cannot live in the shared fixture assembly: the scanner
/// scans every type and would throw on it.)</item>
/// </list>
/// </summary>
public sealed class ActivityVersionResolverTests
{
    private readonly ActivityTypeVersionResolver _resolver = new();

    [Fact]
    public void VersionAttribute_Wins_OverAssemblyVersions()
    {
        var result = _resolver.Resolve(typeof(VersionedFixtureActivity), typeof(VersionedFixtureActivity).Assembly);

        Assert.Equal("3.0.0", result);
    }

    [Fact]
    public void InvalidVersionAttribute_Throws_InvalidActivityVersionException()
    {
        WithEmitted(
            assemblyVersion: new Version(2, 1, 0, 0),
            informationalVersion: null,
            versionAttributeValue: "not-a-semver",
            (type, assembly) =>
            {
                var ex = Assert.Throws<InvalidActivityVersionException>(() => _resolver.Resolve(type, assembly));
                Assert.Equal(type.FullName, ex.ActivityTypeFullName);
                Assert.Equal("not-a-semver", ex.OffendingValue);
            });
    }

    [Fact]
    public void NoAttribute_ValidInformationalVersion_IsUsed()
    {
        // The fixture assembly pins <Version>2.1.0</Version> with the source-revision suffix
        // disabled, so its AssemblyInformationalVersionAttribute is the valid SemVer "2.1.0".
        var result = _resolver.Resolve(typeof(UnannotatedFixtureActivity), typeof(UnannotatedFixtureActivity).Assembly);

        Assert.Equal("2.1.0", result);
    }

    [Fact]
    public void NoAttribute_InvalidInformationalVersion_FallsBackToAssemblyVersion()
    {
        var result = WithEmitted(
            assemblyVersion: new Version(2, 1, 5, 0),
            informationalVersion: "2.1.0+source.revision.is.not.semver core?",
            versionAttributeValue: null,
            _resolver.Resolve);

        Assert.Equal("2.1.5", result);
    }

    [Fact]
    public void NoAttribute_NoInformational_MapsAssemblyMajorMinorBuild()
    {
        var result = WithEmitted(
            assemblyVersion: new Version(4, 2, 7, 99),
            informationalVersion: null,
            versionAttributeValue: null,
            _resolver.Resolve);

        Assert.Equal("4.2.7", result);
    }

    /// <summary>
    /// Emits a throwaway assembly with the requested assembly version, optional informational
    /// version, and optional type-level <c>[Version]</c> attribute, then hands a type resolved from
    /// it (through a reflection-only <see cref="MetadataLoadContext"/> — the same surface the
    /// production scanner reads) to <paramref name="use"/>. The context is kept alive for the
    /// duration of the callback so the resolver can read all metadata it needs.
    /// </summary>
    private static T WithEmitted<T>(
        Version assemblyVersion,
        string? informationalVersion,
        string? versionAttributeValue,
        Func<Type, Assembly, T> use)
    {
        var assemblyName = new AssemblyName("Emitted_" + Guid.NewGuid().ToString("N")) { Version = assemblyVersion };
        var builder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Main");
        var typeBuilder = module.DefineType("Emitted.MyActivity", TypeAttributes.Public | TypeAttributes.Class);

        if (versionAttributeValue is not null)
        {
            var versionCtor = typeof(VersionAttribute).GetConstructor([typeof(string)])!;
            typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(versionCtor, [versionAttributeValue]));
        }

        typeBuilder.CreateType();

        if (informationalVersion is not null)
        {
            var infoCtor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(infoCtor, [informationalVersion]));
        }

        using var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;

        using var context = new MetadataLoadContext(new PathAssemblyResolver(BuildResolverPaths()));
        var loaded = context.LoadFromStream(stream);
        var type = loaded.GetType("Emitted.MyActivity")!;

        return use(type, loaded);
    }

    private void WithEmitted(
        Version assemblyVersion,
        string? informationalVersion,
        string? versionAttributeValue,
        Action<Type, Assembly> assert) =>
        WithEmitted<object?>(assemblyVersion, informationalVersion, versionAttributeValue, (type, assembly) =>
        {
            assert(type, assembly);
            return null;
        });

    /// <summary>
    /// The reflection-only context must resolve the emitted assembly's references — notably the
    /// <c>[Version]</c> attribute's home assembly (<c>Elsa.Activities.Runtime.Core</c>), which lives
    /// next to the test binary rather than in the runtime directory. Mirror the scanner: combine the
    /// test host's base directory with the shared framework directory, deduplicated by name.
    /// </summary>
    private static IEnumerable<string> BuildResolverPaths()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
                    byName[name] = dll;
            }
        }

        Add(AppContext.BaseDirectory);
        Add(RuntimeEnvironment.GetRuntimeDirectory());

        return byName.Values;
    }
}
