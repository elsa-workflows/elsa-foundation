using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// ADR 0088 D4 / spec 260 FR-021: plain-source JavaScript is published under binding-pure-v2, every other language
/// keeps binding-pure-v1, a portable definition keeps the profile it declares, and a stored binding keeps its stamp.
/// </summary>
public sealed class ExpressionCapabilityProfileStampingTests
{
    [Theory]
    [InlineData("JavaScript", ExpressionCapabilityProfiles.BindingPureV2)]
    [InlineData("javascript", ExpressionCapabilityProfiles.BindingPureV2)]
    [InlineData("Liquid", ExpressionCapabilityProfiles.BindingPureV1)]
    public async Task A_plain_source_expression_is_stamped_by_its_language(string language, string expected)
    {
        var executable = await CompileAsync(nameof(WriteHttpResponse.Body), "40 + 2", language);

        Assert.Equal(expected, executable.RootActivity.InputBindings[nameof(WriteHttpResponse.Body)].Expression!.CapabilityProfile);
    }

    [Fact]
    public async Task A_portable_definition_keeps_the_profile_it_declares()
    {
        var plain = await CompileAsync(nameof(WriteHttpResponse.StatusCode), "40 + 2", "JavaScript");
        var resultAlias = plain.RootActivity.InputBindings[nameof(WriteHttpResponse.StatusCode)].Expression!.ResultType!.Id;
        var definition = JsonSerializer.SerializeToElement(new
        {
            language = "JavaScript",
            source = "args.now ? 200 : 500",
            resultType = new { alias = resultAlias, collectionKind = "Single" },
            capabilityProfile = ExpressionCapabilityProfiles.BindingPureV1,
            parameters = new Dictionary<string, object> { ["now"] = new { kind = "Clock" } },
            options = new { },
            metadata = new { }
        });

        var executable = await CompileAsync(nameof(WriteHttpResponse.StatusCode), definition, "JavaScript");

        var expression = executable.RootActivity.InputBindings[nameof(WriteHttpResponse.StatusCode)].Expression!;
        Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, expression.CapabilityProfile);
        Assert.IsType<ClockExpressionParameterBinding>(expression.Parameters["now"]);
    }

    [Theory]
    [InlineData(ExpressionCapabilityProfiles.BindingPureV1)]
    [InlineData(ExpressionCapabilityProfiles.BindingPureV2)]
    public void A_stored_binding_reads_back_with_its_stamp(string profile)
    {
        var stored = JsonSerializer.Serialize(new RuntimeExpressionBinding("JavaScript", "1 + 1", capabilityProfile: profile));

        var read = JsonSerializer.Deserialize<RuntimeExpressionBinding>(stored)!;

        Assert.Equal(profile, read.CapabilityProfile);
    }

    private static Task<WorkflowExecutable> CompileAsync(string inputKey, object value, string expressionType) =>
        SecretBindingCompilerFixture.CompileAsync(
            SecretBindingCompilerFixture.Node(
                typeof(WriteHttpResponse),
                new WorkflowArgumentState(inputKey, new ArgumentValue(value, expressionType), null, null, null, null)),
            [typeof(WriteHttpResponse)],
            includeDeclaredInputDefaults: true);
}
