using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Xunit;

namespace Elsa.Modularity.EntityFramework.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UpgradedModuleCollection
{
    /// <summary>
    /// Two loaded generations of one EF module make every discovery over the whole process refuse, so these tests run
    /// alone, after every test that discovers modules that way.
    /// </summary>
    public const string Name = "Upgraded EF module generations";
}

/// <summary>
/// Spec 183's FR-021, amended 2026-09-29, as the activation guard reads it: a module upgraded in place leaves its previous
/// generation loaded in a load context Nuplane never unloads, so reading every load context finds the module declared
/// twice and the catalog refuses every apply until the host restarts. The guard judges the shell generation an apply is
/// about to build, which composes the active package set, so a replaced generation stops counting for it at once.
/// </summary>
[Collection(UpgradedModuleCollection.Name)]
public sealed class LoadedEfModuleAssemblySourceTests : IDisposable
{
    private readonly string _module = $"Upgraded{Guid.NewGuid():N}";
    private readonly List<AssemblyLoadContext> _contexts = [];
    private readonly Assembly _previous;
    private readonly Assembly _current;

    public LoadedEfModuleAssemblySourceTests()
    {
        var image = Image(_module);
        _previous = Load(image);
        _current = Load(image);
    }

    [Fact]
    public async Task A_replaced_generation_of_a_module_is_not_read_so_the_module_is_declared_once()
    {
        var loaded = await new LoadedEfModuleAssemblySource(new Replaced(_previous)).GetAssembliesAsync();

        Assert.DoesNotContain(_previous, loaded);
        Assert.Same(_current, Assert.Single(EfModuleCatalog.Discover(Generations(loaded))).Assembly);
    }

    /// <summary>What the exclusion prevents: with no evidence of a replacement both generations are read, and refused.</summary>
    [Fact]
    public async Task Without_a_superseded_source_both_generations_are_read_and_the_catalog_refuses_them()
    {
        var loaded = await new LoadedEfModuleAssemblySource().GetAssembliesAsync();

        Assert.Equal([_previous, _current], Generations(loaded));
        var refusal = Assert.Throws<InvalidOperationException>(() => EfModuleCatalog.Discover(Generations(loaded)));
        Assert.Contains($"'{_module}'", refusal.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _contexts.ForEach(context => context.Unload());

    /// <summary>
    /// This test's two generations among <paramref name="loaded"/>, oldest first: another test's, still loaded until the
    /// collector reclaims them, would be a collision of their own.
    /// </summary>
    private Assembly[] Generations(IEnumerable<Assembly> loaded) =>
        [.. loaded.Where(assembly => assembly.GetName().Name == _module).OrderBy(assembly => assembly == _current)];

    private Assembly Load(byte[] image)
    {
        var context = new AssemblyLoadContext($"upgraded-module-{Guid.NewGuid():N}", isCollectible: true);
        _contexts.Add(context);
        using var stream = new MemoryStream(image);
        return context.LoadFromStream(stream);
    }

    /// <summary>An assembly that declares one EF module and nothing else.</summary>
    private static byte[] Image(string module)
    {
        var declaration = new CustomAttributeBuilder(
            typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!,
            [module, typeof(object)],
            [typeof(EfModuleAttribute).GetProperty(nameof(EfModuleAttribute.HistoryModule))!],
            [module]);
        var builder = new PersistedAssemblyBuilder(new AssemblyName(module), typeof(object).Assembly, [declaration]);
        builder.DefineDynamicModule(module);
        using var image = new MemoryStream();
        builder.Save(image);
        return image.ToArray();
    }

    /// <summary>A host whose package runtime replaced exactly <paramref name="assembly"/>.</summary>
    private sealed class Replaced(Assembly assembly) : ISupersededAssemblySource
    {
        public ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<Assembly>>(new HashSet<Assembly> { assembly });

        public ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<Assembly>>(new HashSet<Assembly>());
    }
}
