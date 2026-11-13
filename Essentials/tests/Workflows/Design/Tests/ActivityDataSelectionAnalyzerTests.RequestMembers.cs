using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// Member reads of a workflow request member and of a declared input: admitted exactly as the whole read of the same
/// member or input, never typed, offered for a focused query with a member path; and an explicit conversion on any
/// member read publication cannot type is reported here, before publishing.
/// </summary>
public sealed partial class ActivityDataSelectionAnalyzerTests
{
    [Theory]
    [InlineData("WorkflowRequestMember", """{"memberKey":"customer","memberPath":["a"]}""")]
    [InlineData("InputMember", """{"referenceKey":"customer-ref","memberPath":["a"]}""")]
    public async Task With_member_reads_off_a_request_or_input_member_read_is_refused(string role, string payload)
    {
        using var provider = Provider();

        var result = await Analyzer(provider).AnalyzeAsync(RequestDocument(Input(Json(role, payload))));

        Assert.Equal("ActivityData/UnsupportedMemberBinding", Assert.Single(result.Findings).Type);
    }

    public static TheoryData<string, string, string?> RequestMemberReads => new()
    {
        // A workflow's request is keyed by input name; an Input or InputMember read names the input by reference key.
        { "WorkflowRequestMember", """{"memberKey":"customer","memberPath":["a.b",""]}""", null },
        { "WorkflowRequestMember", """{"memberKey":"customer-ref","memberPath":["a"]}""", "ActivityData/UnknownInput" },
        { "WorkflowRequestMember", """{"memberKey":"customer","path":"a","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { "WorkflowRequestMember", """{"memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { "WorkflowRequestMember", """{"memberKey":"customer","memberPath":[]}""", "ActivityData/InvalidMemberPath" },
        { "InputMember", """{"referenceKey":"customer-ref","memberPath":["a"]}""", null },
        { "InputMember", """{"referenceKey":"customer","memberPath":["a"]}""", "ActivityData/UnknownInput" },
        { "InputMember", """{"referenceKey":"customer-ref","path":"a","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { "InputMember", """{"referenceKey":"customer-ref","memberPath":["a",1]}""", "ActivityData/InvalidMemberPath" }
    };

    [Theory]
    [MemberData(nameof(RequestMemberReads))]
    public async Task A_request_or_input_member_read_is_admitted_as_its_whole_read_and_never_typed(string role, string payload, string? finding)
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(RequestDocument(Input(Json(role, payload))));

        Assert.Empty(result.MemberReads);
        if (finding is null)
            Assert.Empty(result.Findings);
        else
            Assert.Equal(("consumer/inputs/CustomerId", finding), (Assert.Single(result.Findings).Path, result.Findings[0].Type));
    }

    [Fact]
    public async Task A_request_member_read_of_an_open_request_is_admitted()
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer",
            Input(Json("WorkflowRequestMember", """{"memberKey":"anything","memberPath":["a"]}"""))))));

        Assert.Empty(result.Findings);
    }

    public static TheoryData<string, string, string, string, string> OfferedMemberReads => new()
    {
        // document kind, focused role, focused payload, offered role, offered key property=value
        { "WorkflowDefinition", "WorkflowRequest", """{"memberKey":"customer"}""", "WorkflowRequestMember", "memberKey=customer" },
        { "WorkflowDefinition", "Input", """{"referenceKey":"customer-ref"}""", "WorkflowRequestMember", "memberKey=customer" },
        { "ActivityDefinition", "Input", """{"referenceKey":"customer-ref"}""", "InputMember", "referenceKey=customer-ref" }
    };

    /// <summary>What is offered is what is admitted: a focused request or input source with a member path offers the
    /// member read of it, and that binding, stored unchanged, is admitted.</summary>
    [Theory]
    [MemberData(nameof(OfferedMemberReads))]
    public async Task The_offered_request_or_input_member_read_is_the_admitted_one(string kind, string role, string payload, string offeredRole, string offeredKey)
    {
        using var provider = MemberProvider(Known());
        var document = RequestDocument() with { ResourceKind = kind };

        var offered = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { Source = Json(role, payload), MemberPath = ["a.b", ""] });
        var binding = Assert.Single(offered.Sources).Binding!;
        var stored = await Analyzer(provider).AnalyzeAsync(RequestDocument(Input(binding)) with { ResourceKind = kind });

        Assert.Empty(offered.Findings);
        Assert.Equal(offeredRole, binding.ExpressionType);
        var json = Assert.IsType<JsonElement>(binding.Value);
        var (property, value) = (offeredKey.Split('=')[0], offeredKey.Split('=')[1]);
        Assert.Equal(value, json.GetProperty(property).GetString());
        Assert.Equal(["a.b", ""], json.GetProperty("memberPath").EnumerateArray().Select(segment => segment.GetString()));
        Assert.Empty(stored.Findings);
    }

    public static TheoryData<string, string, bool> ConvertedMemberReads => new()
    {
        { "VariableMember", "Json", true },
        { "VariableMember", "Auto", false },
        { "WorkflowRequestMember", "Json", true },
        { "WorkflowRequestMember", "None", true },
        { "ActivityResultMember", "Json", false },
        { "ActivityResultMember+untyped", "Json", true }
    };

    /// <summary>
    /// An explicit conversion on a member read whose member publication cannot type is reported before publishing:
    /// a variable or request member always, a result member when the producer's shape does not type it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvertedMemberReads))]
    public async Task An_explicit_conversion_on_an_untyped_member_read_is_a_design_time_finding(string role, string mode, bool reported)
    {
        using var provider = role == "ActivityResultMember+untyped" ? Provider(MemberReadsOn) : MemberProvider(Known());
        var value = role switch
        {
            "VariableMember" => VariableMember("customer", "workflow"),
            "WorkflowRequestMember" => Json(role, """{"memberKey":"customer","memberPath":["a"]}"""),
            _ => Member("producer")
        };
        var input = Input(value) with { Conversion = new AuthoredValueConversionRequest(Enum.Parse<AuthoredValueConversionMode>(mode)) };
        var document = RequestDocument(input) with { Variables = [Variable("customer", "Customer")] };
        document = role.StartsWith("ActivityResultMember", StringComparison.Ordinal)
            ? document with { RootActivity = Sequence(Node("producer"), Node("consumer", input)) }
            : document;

        var result = await Analyzer(provider).AnalyzeAsync(document);

        Assert.Equal(reported, result.Findings.Any(finding => finding.Path == "consumer/inputs/CustomerId" && finding.Type == "ActivityData/SelectedMemberContractUnknown"));
        Assert.All(result.Findings, finding => Assert.Equal("ActivityData/SelectedMemberContractUnknown", finding.Type));
    }

    private static ActivityDataSelectionDocument RequestDocument(params ArgumentState[] inputs) =>
        Document(Sequence(Node("consumer", inputs))) with { Inputs = [RenamedInput] };

    private static ArgumentValue Json(string role, string payload) => new(JsonDocument.Parse(payload).RootElement.Clone(), role);
}
