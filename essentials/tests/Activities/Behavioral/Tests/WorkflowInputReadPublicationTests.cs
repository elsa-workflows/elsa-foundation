using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Activities.Behavioral;

/// <summary>Verifies that Studio's input reference lowers to the same canonical name the runtime validates.</summary>
public sealed class WorkflowInputReadPublicationTests
{
    // The reference key and the name differ, as they do for an input Studio has renamed.
    private static readonly InputDefinition Greeting = new("greeting-ref", "greeting", new TypeReference("String"), null, "Greeting", null,
        IsNullable: false, IsRequired: false, DefaultValue: JsonSerializer.SerializeToElement("hello from the default"), DefaultSyntax: "Literal");

    private static ActivityNode Root() => new("reader", "reader-version",
        [new(nameof(MemberReadPublicationTests.MemberReader.Value),
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "greeting-ref" }), "Input"), null, null, null, null)], []);

    [Theory]
    [InlineData("hello from the caller", "hello from the caller")]
    [InlineData(null, "hello from the default")]
    public async Task The_published_input_read_uses_the_name_of_the_validated_supplied_or_default_value(string? supplied, string expected)
    {
        using var publication = new MemberReadPublicationTests.Publication(memberReads: false, typed: false);
        var executable = await publication.Compile(Root(), inputs: [Greeting]);
        var binding = executable.RootActivity.InputBindings[nameof(MemberReadPublicationTests.MemberReader.Value)];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("greeting", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.WorkflowRequest.Path);

        var contract = Assert.IsType<WorkflowExecutableInputContract>(executable.InputContract);
        Assert.Equal("greeting", Assert.Single(contract.Inputs).Name);
        var inputs = new Dictionary<string, JsonElement>();
        if (supplied is not null)
            inputs["greeting"] = JsonSerializer.SerializeToElement(supplied);

        var types = SerializationServices.TypeRegistry();
        types.RegisterType(typeof(string), "String");
        var result = new WorkflowExecutableInputValidator(types).Validate(contract, inputs);
        Assert.Empty(result.Findings);
        Assert.Equal(expected, result.NormalizedInputs[binding.WorkflowRequest.MemberKey].GetString());
    }
}
