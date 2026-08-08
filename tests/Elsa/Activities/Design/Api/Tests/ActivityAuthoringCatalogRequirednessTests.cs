using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// G1/G2 catalog projection tests (spec 149 / RFC #1191, §2.23.2): the catalog handler's private
/// projection must carry output requiredness + binding key (G2) and the static-default
/// disambiguator (G1) into the served views. Uses the same reflective private-static invocation
/// style as the ToPorts contract test.
/// </summary>
public sealed class ActivityAuthoringCatalogRequirednessTests
{
    private static readonly Type Handler = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
        "Elsa.Activities.Design.Api.Handlers.ListActivityAuthoringCatalogRequestHandler")!;

    private static TView Project<TDefinition, TView>(TDefinition definition)
    {
        var method = Handler.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == "ToView" &&
                                 candidate.GetParameters() is [var parameter] &&
                                 parameter.ParameterType == typeof(TDefinition));
        return (TView)method.Invoke(null, [definition])!;
    }

    private static OutputDefinition Output(string referenceKey, bool isRequired) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: new TypeReference("Object"),
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsNullable: false,
        IsRequired: isRequired);

    private static InputDefinition Input(JsonElement? defaultValue, bool hasStaticDefault) => new(
        ReferenceKey: "input",
        Name: "Input",
        Type: new TypeReference("String"),
        StorageDriverType: null,
        DisplayName: "Input",
        Category: null,
        IsNullable: true,
        DefaultValue: defaultValue,
        HasStaticDefault: hasStaticDefault);

    [Fact]
    public void Output_view_carries_requiredness_and_binding_key()
    {
        var required = Project<OutputDefinition, ActivityOutputDescriptorView>(Output("Request", isRequired: true));
        var optional = Project<OutputDefinition, ActivityOutputDescriptorView>(Output("ParsedContent", isRequired: false));

        Assert.True(required.IsRequired);
        Assert.Equal("Request", required.ReferenceKey);
        Assert.False(optional.IsRequired);
        Assert.Equal("ParsedContent", optional.ReferenceKey);
    }

    [Fact]
    public void Input_view_carries_static_default_disambiguator()
    {
        var value = JsonSerializer.SerializeToElement("async");

        var concrete = Project<InputDefinition, ActivityInputDescriptorView>(Input(value, hasStaticDefault: true));
        var nullDefault = Project<InputDefinition, ActivityInputDescriptorView>(Input(null, hasStaticDefault: true));
        var notStatic = Project<InputDefinition, ActivityInputDescriptorView>(Input(null, hasStaticDefault: false));

        Assert.True(concrete.HasStaticDefault);
        Assert.Equal("async", concrete.DefaultValue?.GetString());
        Assert.True(nullDefault.HasStaticDefault);
        Assert.Null(nullDefault.DefaultValue);
        Assert.False(notStatic.HasStaticDefault);
    }

    [Fact]
    public void PreG1_persisted_row_with_attribute_default_still_reads_as_static()
    {
        // Rows reconciled before G1 deserialize HasStaticDefault=false even when an attribute default
        // was persisted; the projection must not tell consumers such an input has no default.
        var view = Project<InputDefinition, ActivityInputDescriptorView>(
            Input(JsonSerializer.SerializeToElement("legacy"), hasStaticDefault: false));

        Assert.True(view.HasStaticDefault);
        Assert.Equal("legacy", view.DefaultValue?.GetString());
    }
}
