using System.Reflection;
using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// The proof for the revised FR-004 (research D8): an activity with a complex-typed AND an enum-typed input,
/// run through the reflection-only scanner → the runtime registration startup pass → the compiler's resolution
/// logic, resolves each input back to its REAL CLR type — NOT <c>object</c> — with the stored token being an
/// alias only (never an assembly-qualified name). Also covers the dynamically-loaded extension-builder path:
/// activities surfaced by an <see cref="IFeatureAssemblyProvider"/> (and NOT the host AppDomain) register too.
/// </summary>
public sealed class ActivityIoTypeRegistrationTests
{
    private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    private static ClrAssemblyScanner CreateScanner() =>
        new(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), NullLogger<ClrAssemblyScanner>.Instance);

    // Builds the startup task. With no baseAssemblies override the public (DI) constructor is used, which scans
    // the host AppDomain; supply baseAssemblies to substitute the runtime-loaded source (e.g. exclude it) so the
    // IFeatureAssemblyProvider path can be exercised in isolation.
    private static RegisterActivityIoTypesStartupTask CreateTask(
        IWellKnownTypeRegistry registry,
        IEnumerable<IFeatureAssemblyProvider>? providers = null,
        Func<IEnumerable<Assembly>>? baseAssemblies = null) =>
        baseAssemblies is null
            ? new(registry, providers ?? [], EmptyServiceProvider, NullLogger<RegisterActivityIoTypesStartupTask>.Instance)
            : new(registry, providers ?? [], EmptyServiceProvider, NullLogger<RegisterActivityIoTypesStartupTask>.Instance, baseAssemblies);

    // Mirrors WorkflowExecutableCompiler.ResolveInputType: close the authored (alias, kind) into a CLR type via
    // the registry, unknown alias → object.
    private static Type Resolve(IWellKnownTypeRegistry registry, TypeReference reference) =>
        TypeReferenceFactory.Resolve(reference, alias => registry.TryGetTypeOrDefault(alias, out var type) ? type : typeof(object));

    [Fact]
    public async Task ComplexAndEnumInputs_ResolveToRealClrType_AfterScanAndRegistration()
    {
        // 1. Scan the application output folder (where the runtime-loaded ClrFixture assembly lives) the same
        //    way the CLR reconciliation source does. The scanner emits alias-only TypeReferences.
        var models = CreateScanner().Scan(AppContext.BaseDirectory);
        var activity = Assert.Single(models, m => m.ActivityTypeKey == typeof(ComplexInputFixtureActivity).FullName);
        var inputsByName = activity.Inputs.ToDictionary(i => i.Name, StringComparer.Ordinal);

        var payloadRef = inputsByName["Payload"].Type;
        var modeRef = inputsByName["Mode"].Type;
        var labelRef = inputsByName["Label"].Type;

        // The stored token is the canonical alias: dotted FullName for the complex/enum types, bare for the primitive.
        Assert.Equal(typeof(FixturePayload).FullName, payloadRef.Alias);
        Assert.Equal(typeof(FixtureMode).FullName, modeRef.Alias);
        Assert.Equal("String", labelRef.Alias);

        // No assembly-qualified-name substring anywhere in the stored tokens (no assembly/version leakage).
        foreach (var alias in new[] { payloadRef.Alias, modeRef.Alias, labelRef.Alias })
            Assert.DoesNotContain(", Version=", alias, StringComparison.Ordinal);

        // 2. Run the runtime registration pass over a fresh registry. It reflects the runtime-loaded activity
        //    types (the ClrFixture assembly is loaded in this test process) and registers each I/O element type
        //    under the SAME convention the scanner used.
        var registry = SeedPrimitives(new WellKnownTypeRegistry());
        await CreateTask(registry).ExecuteAsync(CancellationToken.None);

        // 3. The compiler's resolution now yields the REAL CLR types, not object.
        Assert.Equal(typeof(FixturePayload), Resolve(registry, payloadRef));
        Assert.Equal(typeof(FixtureMode), Resolve(registry, modeRef));
        Assert.Equal(typeof(string), Resolve(registry, labelRef));

        // Regression guard: the enum/complex inputs must not silently degrade to object.
        Assert.NotEqual(typeof(object), Resolve(registry, payloadRef));
        Assert.NotEqual(typeof(object), Resolve(registry, modeRef));
    }

    [Fact]
    public async Task RegistrationPass_IsIdempotent()
    {
        var registry = SeedPrimitives(new WellKnownTypeRegistry());
        var task = CreateTask(registry);

        await task.ExecuteAsync(CancellationToken.None);
        await task.ExecuteAsync(CancellationToken.None); // Must not throw on the fail-fast duplicate rule.

        Assert.True(registry.TryGetType(typeof(FixturePayload).FullName!, out var resolved));
        Assert.Equal(typeof(FixturePayload), resolved);
    }

    [Fact]
    public async Task ExtensionBuilderActivityIoTypes_RegisteredViaFeatureAssemblyProvider()
    {
        // Exclude the host AppDomain (baseAssemblies → empty) so the ONLY source is the provider-supplied
        // assembly — i.e. the dynamically-loaded extension-builder activity path, not the framework scan.
        var registry = SeedPrimitives(new WellKnownTypeRegistry());
        var provider = new StubFeatureAssemblyProvider(typeof(ComplexInputFixtureActivity).Assembly);
        var task = CreateTask(registry, providers: [provider], baseAssemblies: static () => []);

        await task.ExecuteAsync(CancellationToken.None);
        await task.ExecuteAsync(CancellationToken.None); // Idempotent across the provider path too.

        Assert.True(registry.TryGetType(typeof(FixturePayload).FullName!, out var payload));
        Assert.Equal(typeof(FixturePayload), payload);
        Assert.True(registry.TryGetType(typeof(FixtureMode).FullName!, out var mode));
        Assert.Equal(typeof(FixtureMode), mode);
    }

    [Fact]
    public async Task FaultingFeatureAssemblyProvider_IsTolerated_AndDoesNotAbortRegistration()
    {
        var registry = SeedPrimitives(new WellKnownTypeRegistry());
        var providers = new IFeatureAssemblyProvider[]
        {
            new ThrowingFeatureAssemblyProvider(),
            new StubFeatureAssemblyProvider(typeof(ComplexInputFixtureActivity).Assembly)
        };

        // A misbehaving provider must not abort the pass; the well-behaved provider's types still register.
        await CreateTask(registry, providers, baseAssemblies: static () => []).ExecuteAsync(CancellationToken.None);

        Assert.True(registry.TryGetType(typeof(FixturePayload).FullName!, out var payload));
        Assert.Equal(typeof(FixturePayload), payload);
    }

    private static WellKnownTypeRegistry SeedPrimitives(WellKnownTypeRegistry registry)
    {
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        return registry;
    }

    private sealed class StubFeatureAssemblyProvider(params Assembly[] assemblies) : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Assembly>>(assemblies);
    }

    private sealed class ThrowingFeatureAssemblyProvider : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider failed to enumerate assemblies.");
    }
}
