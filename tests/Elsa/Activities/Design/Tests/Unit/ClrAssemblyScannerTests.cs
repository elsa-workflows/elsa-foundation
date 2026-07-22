using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Resilient-scan tests for <see cref="ClrAssemblyScanner"/> (FR-023, §2.23.2). Each case lays a
/// real folder of DLLs and asserts the scanner discovers activity-bearing assemblies, skips the
/// rest silently, and never aborts on an unreadable file.
/// </summary>
public sealed class ClrAssemblyScannerTests
{
    private static ClrAssemblyScanner CreateScanner() =>
        new(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), NullLogger<ClrAssemblyScanner>.Instance);

    private static ClrAssemblyScanner CreateScanner(ILogger<ClrAssemblyScanner> logger) =>
        new(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), logger);

    [Fact]
    public void ActivityAssembly_IsDiscovered_WithResolvedVersions()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var models = CreateScanner().Scan(folder.Path);

        var byKey = models.ToDictionary(m => m.ActivityTypeKey, m => m.Version);
        Assert.Equal("2.1.0", byKey[typeof(UnannotatedFixtureActivity).FullName!]);
        Assert.Equal("3.0.0", byKey[typeof(VersionedFixtureActivity).FullName!]);
    }

    [Fact]
    public void TriggerActivityAttribute_DoesNotMutatePersistedCatalogExecutionType()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(TriggerFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(TriggerFixtureActivity).FullName);

        Assert.Equal(ActivityExecutionType.Action, model.ExecutionType);
    }

    [Fact]
    public void HttpEndpoint_ReconcilesWithLegacyCompatibleIdentityAndExecutionType()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(HttpEndpoint).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(HttpEndpoint).FullName);

        Assert.Equal(typeof(HttpEndpoint).FullName, model.ActivityTypeKey);
        Assert.Equal(ActivityExecutionType.Action, model.ExecutionType);

        var supportedMethods = model.Inputs.Single(input => input.Name == nameof(HttpEndpoint.SupportedMethods));
        Assert.Equal(ActivityInputUIHints.CheckList, supportedMethods.UiHint);
        AssertOptions(
            supportedMethods,
            ("GET", "\"GET\""),
            ("POST", "\"POST\""),
            ("PUT", "\"PUT\""),
            ("HEAD", "\"HEAD\""),
            ("DELETE", "\"DELETE\""));
    }

    [Fact]
    public void CollectionTypedInputs_CarryElementAliasAndCollectionKind()
    {
        // Issue #924: a collection-typed input must report its element type alias plus collection kind so the
        // authoring catalog can render a list editor instead of a scalar box. ExpectedStatusCodes (ICollection<int>)
        // and SupportedMethods (ICollection<string>) both decompose to a List of their element alias.
        using var httpFolder = TempAssemblyFolder.WithCopyOf(typeof(SendHttpRequest).Assembly);
        var models = CreateScanner().Scan(httpFolder.Path);

        var expectedStatusCodes = InputFor<SendHttpRequest>(models, nameof(SendHttpRequest.ExpectedStatusCodes));
        Assert.Equal("Int32", expectedStatusCodes.Type.Alias);
        Assert.Equal(CollectionKind.List, expectedStatusCodes.Type.CollectionKind);

        var supportedMethods = InputFor<HttpEndpoint>(models, nameof(HttpEndpoint.SupportedMethods));
        Assert.Equal("String", supportedMethods.Type.Alias);
        Assert.Equal(CollectionKind.List, supportedMethods.Type.CollectionKind);
    }

    public static TheoryData<Type, string> StableTriggerCatalogHashes => new()
    {
        { typeof(TriggerFixtureActivity), "10C41B96E22D85F9A598D30A6AE51D2FAA0F4EF5AA9909D23932EBC3CD00D80E" },
        { typeof(HttpEndpoint), "E7E0CBCEC193A6377AD9102415555997268AC479B89FDDE58578244A5EBF7C88" }
    };

    [Theory]
    [MemberData(nameof(StableTriggerCatalogHashes))]
    public void TriggerAnnotation_PreservesSameVersionActionCatalogHash(Type activityType, string expectedHash)
    {
        using var folder = TempAssemblyFolder.WithCopyOf(activityType.Assembly);
        var model = CreateScanner().Scan(folder.Path).Single(candidate => candidate.ActivityTypeKey == activityType.FullName);
        var identityGenerator = new GuidIdentityGenerator();
        var definition = new ActivityDefinitionFactory(identityGenerator).Create(
            model.ActivityTypeKey,
            model.Category ?? string.Empty,
            model.DisplayName,
            model.Description,
            "stable-definition-id");
        var descriptorPayload = JsonSerializer.SerializeToElement(model.Descriptor, model.Descriptor.GetType());
        var version = new ActivityDefinitionVersionFactory(identityGenerator, new DefaultActivityDefinitionHasher()).Create(
            definition,
            model.Version,
            model.ProviderKey,
            model.ProviderSchemaVersion,
            model.ConsumerKey,
            model.ConsumerSchemaVersion,
            descriptorPayload,
            "CLR",
            activityType.Assembly.GetName().Name!,
            model.Inputs,
            model.Outputs,
            model.DesignFacets,
            model.ExecutionType,
            "stable-version-id");

        Assert.Equal(ActivityExecutionType.Action, version.ExecutionType);
        Assert.True(expectedHash == version.Hash, $"Expected hash '{expectedHash}', actual hash '{version.Hash}'.");
    }

    [Fact]
    public void SelfDeclaredRequiredInput_MapsIsRequired()
    {
        // Guards the pre-existing (previously unasserted) self-declared [Required] → IsRequired mapping
        // so the base-chain refactor (issue #417 item 3) provably preserves it.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var input = InputFor<UnannotatedFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(UnannotatedFixtureActivity.Message));

        Assert.True(input.IsRequired);
    }

    [Fact]
    public void CSharpRequiredMember_MapsIsRequired()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(RequiredKeywordFixtureActivity).Assembly);

        var input = InputFor<RequiredKeywordFixtureActivity>(
            CreateScanner().Scan(folder.Path),
            nameof(RequiredKeywordFixtureActivity.Message));

        Assert.True(input.IsRequired);
    }

    [Fact]
    public void NullableReferenceMetadata_MapsIndependentlyFromRequiredness()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(RequiredKeywordFixtureActivity).Assembly);
        var models = CreateScanner().Scan(folder.Path);

        var nonNullableRequired = InputFor<RequiredKeywordFixtureActivity>(
            models,
            nameof(RequiredKeywordFixtureActivity.Message));
        var nullableOptional = InputFor<RequiredKeywordFixtureActivity>(
            models,
            nameof(RequiredKeywordFixtureActivity.OptionalNote));
        var nullableRequired = InputFor<RequiredKeywordFixtureActivity>(
            models,
            nameof(RequiredKeywordFixtureActivity.RequiredNullableNote));
        var nullableValue = InputFor<RequiredKeywordFixtureActivity>(
            models,
            nameof(RequiredKeywordFixtureActivity.OptionalCount));

        Assert.True(nonNullableRequired.IsRequired);
        Assert.False(nonNullableRequired.IsNullable);
        Assert.False(nullableOptional.IsRequired);
        Assert.True(nullableOptional.IsNullable);
        Assert.True(nullableRequired.IsRequired);
        Assert.True(nullableRequired.IsNullable);
        Assert.False(nullableValue.IsRequired);
        Assert.True(nullableValue.IsNullable);
        Assert.Equal("Int32?", nullableValue.Type.Alias);
    }

    [Fact]
    public void InheritedRequiredInput_MapsIsRequired()
    {
        // The derived activity re-declares its input with `new` and no [Required]; the attribute lives
        // only on the base declaration. The reflection-only scanner reads the attribute-less derived
        // declaration first, so it must walk the base-property chain to find [Required] (issue #417 item 3).
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var inputs = InputsFor<InheritsRequiredFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(RequiredInputBaseActivity.InheritedRequired));

        // Whatever the scanner surfaces for the hidden/new pair, the inherited [Required] must win.
        Assert.All(inputs, input => Assert.True(input.IsRequired));
        Assert.NotEmpty(inputs);
    }

    [Fact]
    public void ActivityInputAttribute_MapsOrderAndDefaultMetadata()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(ComplexInputFixtureActivity).FullName);
        var inputs = model.Inputs.ToDictionary(i => i.Name, StringComparer.Ordinal);

        Assert.Equal(10, inputs[nameof(ComplexInputFixtureActivity.Mode)].Order);
        Assert.Equal("Simple", inputs[nameof(ComplexInputFixtureActivity.Mode)].Category);
        Assert.Equal("Auto", inputs[nameof(ComplexInputFixtureActivity.Mode)].DefaultValue?.GetString());
        Assert.Equal("Literal", inputs[nameof(ComplexInputFixtureActivity.Mode)].DefaultSyntax);
        Assert.Equal(20, inputs[nameof(ComplexInputFixtureActivity.Payload)].Order);
        Assert.Null(inputs[nameof(ComplexInputFixtureActivity.Payload)].Category);
        Assert.Equal(30, inputs[nameof(ComplexInputFixtureActivity.Label)].Order);
    }

    [Fact]
    public void InputDisplayName_IsHumanized_WhenNotExplicitlyAuthored()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(DisplayNameFixtureActivity).Assembly);

        var input = InputFor<DisplayNameFixtureActivity>(
            CreateScanner().Scan(folder.Path),
            nameof(DisplayNameFixtureActivity.ExpectedStatusCodes));

        Assert.Equal("Expected Status Codes", input.DisplayName);
        Assert.Null(input.Description);
    }

    [Fact]
    public void InputDisplayNameAndDescription_AreRespected_WhenAuthored()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(DisplayNameFixtureActivity).Assembly);

        var input = InputFor<DisplayNameFixtureActivity>(
            CreateScanner().Scan(folder.Path),
            nameof(DisplayNameFixtureActivity.ContentType));

        Assert.Equal("Custom Label", input.DisplayName);
        Assert.Equal("A hand-authored description.", input.Description);
    }

    [Fact]
    public void OutputDisplayName_IsHumanized_WhenNotExplicitlyAuthored()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(DisplayNameFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(DisplayNameFixtureActivity).FullName);
        var output = model.Outputs.Single(o => o.Name == nameof(DisplayNameFixtureResult.ResponseStatusCode));

        Assert.Equal("Response Status Code", output.DisplayName);
    }

    [Fact]
    public void ActivityDisplayName_IsHumanizedTypeName()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(DisplayNameFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(DisplayNameFixtureActivity).FullName);

        Assert.Equal("Display Name Fixture Activity", model.DisplayName);
    }

    [Fact]
    public void Category_IsHumanized_FromAssemblySegment()
    {
        // The fixture assembly's last name segment ("ClrFixture") humanizes to "Clr Fixture", proving the
        // category resolver spaces PascalCase segments instead of emitting the raw token.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(DisplayNameFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(DisplayNameFixtureActivity).FullName);

        Assert.Equal("Clr Fixture", model.Category);
    }

    [Fact]
    public void Plain_typed_activity_maps_stable_input_and_result_projection_keys()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(PlainFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(candidate => candidate.ActivityTypeKey == typeof(PlainFixtureActivity).FullName);
        var input = Assert.Single(model.Inputs);
        var output = Assert.Single(model.Outputs);

        Assert.Equal("message", input.ReferenceKey);
        Assert.Equal(nameof(PlainFixtureActivity.Message), input.Name);
        Assert.True(input.IsRequired);
        Assert.Equal("length", output.ReferenceKey);
        Assert.Equal(nameof(PlainFixtureResult.Length), output.Name);
        Assert.True(output.IsRequired);
    }

    [Fact]
    public void ActivityInputAttribute_ParsesIntegerDefaultAsNumber()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var input = InputFor<ComplexInputFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(ComplexInputFixtureActivity.Count));

        Assert.Equal(40, input.Order);
        Assert.Equal(JsonValueKind.Number, input.DefaultValue?.ValueKind);
        Assert.Equal(1, input.DefaultValue?.GetInt32());
        Assert.Equal("Literal", input.DefaultSyntax);
    }

    [Fact]
    public void InheritedActivityInputAttribute_MapsMetadata()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var inputs = InputsFor<InheritsRequiredFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(RequiredInputBaseActivity.InheritedRequired));

        Assert.All(inputs, input =>
        {
            Assert.Equal(42, input.Order);
            Assert.Equal("Advanced", input.Category);
            Assert.Equal("inherited-default", input.DefaultValue?.GetString());
            Assert.Equal("Literal", input.DefaultSyntax);
        });
        Assert.NotEmpty(inputs);
    }

    [Fact]
    public void ActivityInputOptions_MapStaticTypedEnumAndProviderMetadata()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(InputOptionsFixtureActivity).Assembly);
        var models = CreateScanner().Scan(folder.Path);

        var color = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.Color));
        Assert.Equal(ActivityInputUIHints.Dropdown, color.UiHint);
        AssertOptions(color, ("red", "\"red\""), ("green", "\"green\""));

        var priority = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.Priority));
        AssertOptions(priority, ("Low", "1"), ("High", "10"));

        var mode = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.Mode));
        AssertOptions(mode, ("Automatic", "\"Auto\""), ("Disabled", "\"Off\""));

        var enabled = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.Enabled));
        AssertOptions(enabled, ("Enabled", "true"), ("Disabled", "false"));

        var boundaries = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.JsSafeIntegerBoundary));
        AssertOptions(
            boundaries,
            ("Minimum JS-safe integer", "-9007199254740991"),
            ("Maximum JS-safe integer", "9007199254740991"));

        var field = InputFor<InputOptionsFixtureActivity>(models, nameof(InputOptionsFixtureActivity.Field));
        var provider = field.UISpecifications!.Value.GetProperty("optionsProvider");
        Assert.Equal("fixture.fields", provider.GetProperty("key").GetString());
        Assert.Equal([nameof(InputOptionsFixtureActivity.Entity)], provider.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void ActivityInputOptions_InheritUntilNearestPropertyReplacesThem()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(InputOptionsFixtureActivity).Assembly);
        var models = CreateScanner().Scan(folder.Path);

        AssertOptions(
            InputFor<InheritedInputOptionsFixtureActivity>(models, nameof(InheritedInputOptionsFixtureActivity.Choice)),
            ("base-a", "\"base-a\""),
            ("base-b", "\"base-b\""));
        AssertOptions(
            InputFor<ReplacedInputOptionsFixtureActivity>(models, nameof(ReplacedInputOptionsFixtureActivity.Choice)),
            ("Derived first", "2"),
            ("Derived second", "3"));
    }

    [Theory]
    [InlineData(typeof(ConflictingOptionsActivity), nameof(ConflictingOptionsActivity.Value), "conflicting")]
    [InlineData(typeof(DuplicateOptionsActivity), nameof(DuplicateOptionsActivity.Value), "duplicate")]
    [InlineData(typeof(BlankOptionActivity), nameof(BlankOptionActivity.Value), "blank")]
    [InlineData(typeof(IncompatibleOptionActivity), nameof(IncompatibleOptionActivity.Value), "compatible")]
    [InlineData(typeof(UnknownDependencyActivity), nameof(UnknownDependencyActivity.Value), "dependency")]
    [InlineData(typeof(DependenciesWithoutProviderActivity), nameof(DependenciesWithoutProviderActivity.Value), "provider")]
    [InlineData(typeof(UnsafePositiveIntegerOptionActivity), nameof(UnsafePositiveIntegerOptionActivity.Value), "safe integer")]
    [InlineData(typeof(UnsafeNegativeIntegerOptionActivity), nameof(UnsafeNegativeIntegerOptionActivity.Value), "safe integer")]
    [InlineData(typeof(NonFiniteOptionActivity), nameof(NonFiniteOptionActivity.Value), "finite")]
    [InlineData(typeof(NumericDuplicateOptionsActivity), nameof(NumericDuplicateOptionsActivity.Value), "duplicate")]
    public void InvalidActivityInputOptions_AreRejectedWithInputIdentity(Type activityType, string inputName, string expectedMessage)
    {
        var property = activityType.GetProperty(inputName)!;
        var exception = Assert.Throws<InvalidOperationException>(() => InvokeReadActivityInputMetadata(property, property.PropertyType, activityType));

        Assert.Contains(activityType.FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(inputName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InputDefinition InputFor<TActivity>(IReadOnlyList<ActivityVersionReconciliationModel> models, string inputName) =>
        models.Single(m => m.ActivityTypeKey == typeof(TActivity).FullName).Inputs.Single(i => i.Name == inputName);

    private static IReadOnlyList<InputDefinition> InputsFor<TActivity>(IReadOnlyList<ActivityVersionReconciliationModel> models, string inputName) =>
        models.Single(m => m.ActivityTypeKey == typeof(TActivity).FullName).Inputs.Where(i => i.Name == inputName).ToList();

    private static void AssertOptions(InputDefinition input, params (string Label, string RawValue)[] expected)
    {
        var options = input.UISpecifications!.Value.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, options.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Label, options[i].GetProperty("label").GetString());
            Assert.Equal(expected[i].RawValue, options[i].GetProperty("value").GetRawText());
        }
    }

    private static object InvokeReadActivityInputMetadata(PropertyInfo property, Type valueType, Type activityType)
    {
        var method = typeof(ClrAssemblyScanner).GetMethod("ReadActivityInputMetadata", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ClrAssemblyScanner), "ReadActivityInputMetadata");
        var inputNames = activityType.GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        try
        {
            return method.Invoke(null, [property, valueType, inputNames, activityType.FullName!])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private abstract class ConflictingOptionsActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput(Options = ["one"])]
        [ActivityInputOption("Two", "two")]
        public string Value { get; set; } = null!;
    }

    private abstract class DuplicateOptionsActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("One", 1)]
        [ActivityInputOption("Also one", 1)]
        public int Value { get; set; }
    }

    private abstract class BlankOptionActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput(Options = [""])]
        public string Value { get; set; } = null!;
    }

    private abstract class IncompatibleOptionActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("Wrong", true)]
        public int Value { get; set; }
    }

    private abstract class UnknownDependencyActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput(OptionsProvider = "fixture", OptionsProviderDependencies = ["Missing"])]
        public string Value { get; set; } = null!;
    }

    private abstract class DependenciesWithoutProviderActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput(OptionsProviderDependencies = [nameof(Other)])]
        public string Value { get; set; } = null!;

        [ActivityInput]
        public string Other { get; set; } = null!;
    }

    private abstract class UnsafePositiveIntegerOptionActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("Unsafe", 9007199254740992L)]
        public long Value { get; set; }
    }

    private abstract class UnsafeNegativeIntegerOptionActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("Unsafe", -9007199254740992L)]
        public long Value { get; set; }
    }

    private abstract class NonFiniteOptionActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("Infinite", double.PositiveInfinity)]
        public double Value { get; set; }
    }

    private abstract class NumericDuplicateOptionsActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        [ActivityInputOption("Integer", 1)]
        [ActivityInputOption("Floating point", 1.0)]
        public double Value { get; set; }
    }

    [Fact]
    public void DecimalOption_MustRoundTripExactlyThroughDouble()
    {
        var method = typeof(ClrAssemblyScanner).GetMethod("ValidateNumericFidelity", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ClrAssemblyScanner), "ValidateNumericFidelity");

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, [0.1000000000000000000000000001m, typeof(decimal), typeof(DecimalFidelityActivity).FullName!, nameof(DecimalFidelityActivity.Value)]));

        var error = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("round-trip", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DecimalFidelityActivity.Value), error.Message, StringComparison.Ordinal);
    }

    private abstract class DecimalFidelityActivity : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [ActivityInput]
        public decimal Value { get; set; }
    }

    [Fact]
    public void ApplicationOutputFolder_DiscoversPrimitiveActivities()
    {
        var models = CreateScanner().Scan(AppContext.BaseDirectory);

        Assert.Contains(models, m => m.ActivityTypeKey == typeof(WriteLine).FullName);
    }

    [Fact]
    public void StructureAttributes_MapToDesignFacet()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(StructuredFixtureActivity).FullName);
        var facet = Assert.Single(model.DesignFacets);
        var output = Assert.Single(model.Outputs);

        Assert.Equal("summary", output.ReferenceKey);
        Assert.Equal(nameof(StructuredFixtureResult.Summary), output.Name);

        Assert.Equal("fixture.structure", facet.Kind);
        Assert.Equal("1.0.0", facet.SchemaVersion);

        var payload = facet.Payload;
        Assert.Equal("sequence", payload.GetProperty("mode").GetString());
        Assert.True(payload.GetProperty("supportsScopedVariables").GetBoolean());

        var slots = payload.GetProperty("slots").EnumerateArray().ToArray();
        Assert.Equal(2, slots.Length);
        Assert.Contains(slots, slot =>
            slot.GetProperty("name").GetString() == "Fixture.Activities" &&
            slot.GetProperty("property").GetString() == "activities" &&
            slot.GetProperty("cardinality").GetString() == ActivityChildSlotCardinalities.Many);
        Assert.Contains(slots, slot =>
            slot.GetProperty("name").GetString() == "Fixture.Body" &&
            slot.GetProperty("property").GetString() == "body" &&
            slot.GetProperty("cardinality").GetString() == ActivityChildSlotCardinalities.Single);

        var initialPayload = payload.GetProperty("initialPayload");
        Assert.Equal(JsonValueKind.Array, initialPayload.GetProperty("activities").ValueKind);
        Assert.Equal(JsonValueKind.Null, initialPayload.GetProperty("body").ValueKind);
    }

    [Fact]
    public void ActivityOutcomeAttributes_MapToDesignFacetPorts()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(OutcomeFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(OutcomeFixtureActivity).FullName);
        var facet = model.DesignFacets.Single(f => f.Kind == "elsa.outcomes");

        Assert.Equal("1", facet.SchemaVersion);
        var ports = facet.Payload.GetProperty("ports").EnumerateArray().ToArray();
        var portNames = ports.Select(p => p.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "Done", "Success", "Failed" }, portNames);
        Assert.All(ports, p => Assert.Equal("outcome", p.GetProperty("type").GetString()));
    }

    [Fact]
    public void InheritedOutcomeAttributes_CollectedFromBaseChain()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(InheritedOutcomeFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(InheritedOutcomeFixtureActivity).FullName);
        var facet = model.DesignFacets.Single(f => f.Kind == "elsa.outcomes");
        var portNames = facet.Payload.GetProperty("ports").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("DerivedOutcome", portNames);
        Assert.Contains("Done", portNames);
        Assert.Contains("BaseOutcome", portNames);
    }

    [Fact]
    public void ActivityWithoutOutcomeAttributes_HasNoOutcomeFacet()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(UnannotatedFixtureActivity).FullName);

        Assert.DoesNotContain(model.DesignFacets, f => f.Kind == "elsa.outcomes");
    }

    [Fact]
    public void SendHttpRequest_OutcomeFacet_ContainsStaticOutcomesAndDynamicStatusCodeDescriptor()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(SendHttpRequest).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(SendHttpRequest).FullName);
        var facet = model.DesignFacets.Single(f => f.Kind == "elsa.outcomes");
        var portNames = facet.Payload.GetProperty("ports").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        // Static base ports (issue #926 replaced Matched/Unmatched with per-status dynamic outcomes).
        Assert.Contains("Done", portNames);
        Assert.Contains("Failed", portNames);
        Assert.Contains("Timeout", portNames);
        Assert.Equal(3, portNames.Count);

        // The dynamic-outcomes descriptor tells the designer to render one port per authored ExpectedStatusCodes item.
        var dynamicOutcomes = facet.Payload.GetProperty("dynamicOutcomes");
        Assert.Equal("valuePerItem", dynamicOutcomes.GetProperty("kind").GetString());
        Assert.Equal(nameof(SendHttpRequest.ExpectedStatusCodes), dynamicOutcomes.GetProperty("source").GetString());
        Assert.Equal("Unmatched status code", dynamicOutcomes.GetProperty("unmatched").GetString());
    }

    [Fact]
    public void SendHttpRequest_ResponseBody_IsDiscoveredAsFormattedContent()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(SendHttpRequest).Assembly);

        var model = CreateScanner().Scan(folder.Path).Single(m => m.ActivityTypeKey == typeof(SendHttpRequest).FullName);
        var responseBody = model.Outputs.Single(output => output.ReferenceKey == "ResponseBody");

        Assert.Equal(ValueRepresentation.FormattedContent, responseBody.SourceRepresentation);
    }

    [Fact]
    public void DiscoveredActivities_AreCategorised_ByAssemblyNameLastSegment()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var models = CreateScanner().Scan(folder.Path);

        // The fixture assembly is "Elsa.Activities.Design.Tests.ClrFixture" → segment "ClrFixture",
        // humanized to "Clr Fixture" for display (issue #928).
        Assert.All(models, m => Assert.Equal("Clr Fixture", m.Category));
    }

    [Fact]
    public void NonActivityAssembly_IsSilentlySkipped()
    {
        // Elsa.Primitives carries no IActivity implementations.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(ClrActivityDescriptor).Assembly);

        Assert.Empty(CreateScanner().Scan(folder.Path));
    }

    [Fact]
    public void DuplicateAssemblyName_AcrossResolverSources_IsWarned()
    {
        // The folder DLL is added to the resolver map first (author-wins precedence), then the same
        // simple-name is re-encountered while enumerating the AppDomain — Elsa.Primitives is already
        // loaded into the test host's AppDomain, so copying it into the scan folder guarantees a
        // simple-name collision. The scanner keeps the first (folder) path but must surface the drop.
        var primitives = typeof(ClrActivityDescriptor).Assembly;
        var simpleName = primitives.GetName().Name!;
        using var folder = TempAssemblyFolder.WithCopyOf(primitives);
        var logger = new CapturingLogger();

        _ = CreateScanner(logger).Scan(folder.Path);

        Assert.Contains(logger.Warnings, w => w.Contains(simpleName, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateResolverPath_IsIgnoredWithoutWarning()
    {
        var logger = new CapturingLogger();
        var scanner = CreateScanner(logger);
        var path = typeof(ClrActivityDescriptor).Assembly.Location;

        var paths = InvokeBuildResolverPaths(scanner, [path, path]);

        Assert.Equal(1, paths.Count(candidate => candidate == path));
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void UnreadableDll_IsLoggedAndSkipped_ScanStillCompletes()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        File.WriteAllText(Path.Combine(folder.Path, "garbage.dll"), "this is not a portable executable");

        // The junk DLL is skipped; discovery of activities from the valid fixture assembly still completes.
        Assert.Contains(
            CreateScanner().Scan(folder.Path),
            model => model.ActivityTypeKey == typeof(UnannotatedFixtureActivity).FullName);
    }

    [Fact]
    public void RepeatedScans_YieldIdenticalResults_WithCachedFrameworkPaths()
    {
        // The framework resolver paths (base dir, TPA, runtime dir) are cached across Scan() calls
        // (issue #417 item 2). A second scan must resolve exactly the same activities and versions —
        // proving the cached overlay is re-applied intact and per-call state is not corrupted.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var scanner = CreateScanner();

        var first = scanner.Scan(folder.Path).ToDictionary(m => m.ActivityTypeKey, m => m.Version);
        var second = scanner.Scan(folder.Path).ToDictionary(m => m.ActivityTypeKey, m => m.Version);

        Assert.Equal(first, second);
        Assert.Equal("2.1.0", second[typeof(UnannotatedFixtureActivity).FullName!]);
        Assert.Equal("3.0.0", second[typeof(VersionedFixtureActivity).FullName!]);
    }

    [Fact]
    public void FolderAssembly_IsResolvable_EvenAfterCachedFrameworkPathsAreWarm()
    {
        // Warm the framework-path cache with a base-directory scan first, then scan a temp folder.
        // The folder DLLs must still be added ahead of the cached framework closure so the author's
        // assembly resolves (folder-precedence overlay preserved) — a broken overlay would drop these
        // activities.
        var scanner = CreateScanner();
        _ = scanner.Scan(AppContext.BaseDirectory);

        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var models = scanner.Scan(folder.Path);

        Assert.Contains(models, m => m.ActivityTypeKey == typeof(UnannotatedFixtureActivity).FullName);
    }

    [Fact]
    public void EmptyFolder_YieldsNoModels()
    {
        using var folder = TempAssemblyFolder.Empty();

        Assert.Empty(CreateScanner().Scan(folder.Path));
    }

    [Fact]
    public void NonexistentFolder_YieldsNoModels()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Empty(CreateScanner().Scan(missing));
    }

    private static IReadOnlyCollection<string> InvokeBuildResolverPaths(ClrAssemblyScanner scanner, IEnumerable<string> folderDlls)
    {
        var method = typeof(ClrAssemblyScanner).GetMethod("BuildResolverPaths", BindingFlags.Instance | BindingFlags.NonPublic);
        var result = method?.Invoke(scanner, [folderDlls]);
        return Assert.IsAssignableFrom<IReadOnlyCollection<string>>(result);
    }

    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> double that captures formatted <see cref="LogLevel.Warning"/>
    /// messages. The project takes no logging-test package, so this hand-rolled double is the
    /// zero-dependency way to assert a warning fired.
    /// </summary>
    private sealed class CapturingLogger : ILogger<ClrAssemblyScanner>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class TempAssemblyFolder : IDisposable
    {
        public string Path { get; }

        private TempAssemblyFolder(string path) => Path = path;

        public static TempAssemblyFolder Empty()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clr-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempAssemblyFolder(dir);
        }

        public static TempAssemblyFolder WithCopyOf(Assembly assembly)
        {
            var folder = Empty();
            var source = assembly.Location;
            File.Copy(source, System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(source)), overwrite: true);
            return folder;
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
