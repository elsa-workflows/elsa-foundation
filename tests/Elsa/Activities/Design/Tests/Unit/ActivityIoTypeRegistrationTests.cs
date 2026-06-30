using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// The proof for the revised FR-004 (research D8): an activity with a complex-typed AND an enum-typed input,
/// run through the reflection-only scanner → the runtime registration startup pass → the compiler's resolution
/// logic, resolves each input back to its REAL CLR type — NOT <c>object</c> — with the stored token being an
/// alias only (never an assembly-qualified name).
/// </summary>
public sealed class ActivityIoTypeRegistrationTests
{
    private static ClrAssemblyScanner CreateScanner() =>
        new(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), NullLogger<ClrAssemblyScanner>.Instance);

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
        await new RegisterActivityIoTypesStartupTask(registry, NullLogger<RegisterActivityIoTypesStartupTask>.Instance)
            .ExecuteAsync(CancellationToken.None);

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
        var task = new RegisterActivityIoTypesStartupTask(registry, NullLogger<RegisterActivityIoTypesStartupTask>.Instance);

        await task.ExecuteAsync(CancellationToken.None);
        await task.ExecuteAsync(CancellationToken.None); // Must not throw on the fail-fast duplicate rule.

        Assert.True(registry.TryGetType(typeof(FixturePayload).FullName!, out var resolved));
        Assert.Equal(typeof(FixturePayload), resolved);
    }

    private static WellKnownTypeRegistry SeedPrimitives(WellKnownTypeRegistry registry)
    {
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        return registry;
    }
}
