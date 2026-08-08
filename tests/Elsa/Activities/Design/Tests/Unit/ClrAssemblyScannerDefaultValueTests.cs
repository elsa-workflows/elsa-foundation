using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// G1 static-default ladder tests (spec 149 / RFC #1191, constitution 2.23.2): the reflection-only scanner
/// must emit the effective static default of every input (attribute default, then compiled
/// property-initializer constant, then synthesized <c>default(T)</c>, then explicit null) with the honest
/// <see cref="InputDefinition.HasStaticDefault"/> = <see langword="false"/> escape hatch for values that
/// cannot be statically represented.
/// </summary>
public sealed class ClrAssemblyScannerDefaultValueTests : IDisposable
{
    private readonly TempFolder folder = TempFolder.WithCopyOf(typeof(InitializerDefaultsFixtureActivity).Assembly);
    private readonly Lazy<IReadOnlyList<ActivityVersionReconciliationModel>> models;

    public ClrAssemblyScannerDefaultValueTests()
    {
        models = new Lazy<IReadOnlyList<ActivityVersionReconciliationModel>>(() =>
            new ClrAssemblyScanner(
                    new ActivityTypeVersionResolver(),
                    new ActivityTypeCategoryResolver(),
                    NullLogger<ClrAssemblyScanner>.Instance)
                .Scan(folder.Path));
    }

    public void Dispose() => folder.Dispose();

    private InputDefinition Input(string name) =>
        models.Value.Single(m => m.ActivityTypeKey == typeof(InitializerDefaultsFixtureActivity).FullName)
            .Inputs.Single(i => i.Name == name);

    [Fact]
    public void EnumInitializer_EmitsWireSpelledMemberName()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.EnumWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("auto", input.DefaultValue?.GetString());
    }

    [Fact]
    public void EnumWithoutInitializer_EmitsZeroMemberAsDefault()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.EnumZeroDefault));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("off", input.DefaultValue?.GetString());
    }

    [Fact]
    public void IntInitializer_EmitsNumber()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.IntWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(42, input.DefaultValue?.GetInt32());
    }

    [Fact]
    public void LongInitializer_SurvivesConvI8()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.LongWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(5L, input.DefaultValue?.GetInt64());
    }

    [Fact]
    public void DoubleInitializer_EmitsNumber()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.DoubleWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(1.5, input.DefaultValue?.GetDouble());
    }

    [Fact]
    public void FloatInitializer_EmitsNumber()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.FloatWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(2.5f, input.DefaultValue?.GetSingle());
    }

    [Fact]
    public void StringInitializer_EmitsString()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.StringWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("hello", input.DefaultValue?.GetString());
    }

    [Fact]
    public void BoolInitializer_EmitsTrue()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.BoolWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(JsonValueKind.True, input.DefaultValue?.ValueKind);
    }

    [Fact]
    public void BoolWithoutInitializer_EmitsFalseDefault()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.BoolZeroDefault));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(JsonValueKind.False, input.DefaultValue?.ValueKind);
    }

    [Fact]
    public void NullableReference_WithoutInitializer_DefaultIsExplicitNull()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.NullableStringNoInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Null(input.DefaultValue);
    }

    [Fact]
    public void NullableInt_WithInitializer_UnwrapsNullableConstruction()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.NullableIntWithInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal(7, input.DefaultValue?.GetInt32());
    }

    [Fact]
    public void NullableInt_WithoutInitializer_DefaultIsExplicitNull()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.NullableIntNoInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Null(input.DefaultValue);
    }

    [Fact]
    public void ComputedInitializer_IsNotStaticallyRepresentable()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.ComputedInitializer));

        Assert.False(input.HasStaticDefault);
        Assert.Null(input.DefaultValue);
    }

    [Fact]
    public void ConstructedInitializer_IsNotStaticallyRepresentable()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.ConstructedInitializer));

        Assert.False(input.HasStaticDefault);
        Assert.Null(input.DefaultValue);
    }

    [Fact]
    public void NonSynthesizableStruct_IsNotStaticallyRepresentable()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.NonSynthesizableStruct));

        Assert.False(input.HasStaticDefault);
        Assert.Null(input.DefaultValue);
    }

    [Fact]
    public void AttributeDefault_WinsOverInitializer()
    {
        var input = Input(nameof(InitializerDefaultsFixtureActivity.AttributeBeatsInitializer));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("attribute-wins", input.DefaultValue?.GetString());
    }

    [Fact]
    public void BaseClassInitializer_IsSurfacedOnDerivedActivity()
    {
        var input = models.Value.Single(m => m.ActivityTypeKey == typeof(InheritedInitializerFixtureActivity).FullName)
            .Inputs.Single(i => i.Name == nameof(InitializerBaseActivity.BaseInitialized));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("from-base", input.DefaultValue?.GetString());
    }

    /// <summary>
    /// The known G1 repro (RFC #1191): <c>HttpEndpoint.ResponseMode</c> defaults to
    /// <see cref="ResponseMode.Async"/> in code while the catalog said <c>defaultValue: null</c>.
    /// The wire spelling is camelCase per the FastEndpoints enum converter.
    /// </summary>
    [Fact]
    public void HttpEndpoint_ResponseMode_DefaultsToAsync()
    {
        using var httpFolder = TempFolder.WithCopyOf(typeof(HttpEndpoint).Assembly);
        var scanner = new ClrAssemblyScanner(
            new ActivityTypeVersionResolver(),
            new ActivityTypeCategoryResolver(),
            NullLogger<ClrAssemblyScanner>.Instance);

        var input = scanner.Scan(httpFolder.Path)
            .Single(m => m.ActivityTypeKey == typeof(HttpEndpoint).FullName)
            .Inputs.Single(i => i.Name == nameof(HttpEndpoint.ResponseMode));

        Assert.True(input.HasStaticDefault);
        Assert.Equal("async", input.DefaultValue?.GetString());
    }

    /// <summary>The known G2 repro companion: Request/RouteData are required-to-bind, ParsedContent is not.</summary>
    [Fact]
    public void HttpEndpoint_Outputs_CarryRequiredness()
    {
        using var httpFolder = TempFolder.WithCopyOf(typeof(HttpEndpoint).Assembly);
        var scanner = new ClrAssemblyScanner(
            new ActivityTypeVersionResolver(),
            new ActivityTypeCategoryResolver(),
            NullLogger<ClrAssemblyScanner>.Instance);

        var outputs = scanner.Scan(httpFolder.Path)
            .Single(m => m.ActivityTypeKey == typeof(HttpEndpoint).FullName)
            .Outputs.ToDictionary(o => o.ReferenceKey, StringComparer.Ordinal);

        Assert.True(outputs["Request"].IsRequired);
        Assert.True(outputs["RouteData"].IsRequired);
        Assert.False(outputs["ParsedContent"].IsRequired);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; }

        private TempFolder(string path) => Path = path;

        public static TempFolder WithCopyOf(Assembly assembly)
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clr-defaults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.Copy(assembly.Location, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(assembly.Location)), overwrite: true);
            return new TempFolder(dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a locked file must not fail the test.
            }
        }
    }
}

