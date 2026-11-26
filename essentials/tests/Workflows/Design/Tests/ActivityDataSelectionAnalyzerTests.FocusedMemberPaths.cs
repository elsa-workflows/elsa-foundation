using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// A focused query with a member path asks for a member read and is never answered with the whole read: with member
/// reads off it is refused as a stored member read is, and a source that has no member read is invalid. Every answer
/// says whether member reads are on.
/// </summary>
public sealed partial class ActivityDataSelectionAnalyzerTests
{
    public static TheoryData<string, string> FocusedMemberSources => new()
    {
        // focused source, the member read it offers with member reads on
        { "result", "ActivityResultMember" },
        { "variable", "VariableMember" },
        { "request", "WorkflowRequestMember" },
        { "workflow-input", "WorkflowRequestMember" },
        { "body-input", "InputMember" }
    };

    [Theory]
    [MemberData(nameof(FocusedMemberSources))]
    public async Task With_member_reads_off_a_member_path_query_is_refused_and_offers_no_source(string source, string _)
    {
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new FixedShape(Known())));
        var (document, focused) = Focused(source);

        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { Source = focused, MemberPath = ["customerId"] });

        Assert.Empty(result.Sources);
        Assert.Equal(("consumer/inputs/CustomerId", "ActivityData/UnsupportedMemberBinding"),
            (Assert.Single(result.Findings).Path, result.Findings[0].Type));
        Assert.False(result.MemberReadsEnabled);
    }

    [Theory]
    [MemberData(nameof(FocusedMemberSources))]
    public async Task With_member_reads_on_a_member_path_query_offers_the_member_read(string source, string offeredRole)
    {
        using var provider = MemberProvider(Known());
        var (document, focused) = Focused(source);

        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { Source = focused, MemberPath = ["customerId"] });

        Assert.Empty(result.Findings);
        Assert.Equal(offeredRole, Assert.Single(result.Sources).Binding!.ExpressionType);
        Assert.True(result.MemberReadsEnabled);
    }

    public static TheoryData<string, bool> SourcesWithoutMemberRead => new()
    {
        { """{"producerNodeId":"producer","projectionKey":"$result","producerScopeId":"main"}""", false },
        { """{"producerNodeId":"producer","projectionKey":"$result","producerScopeId":"main"}""", true },
        { """{"memberKey":"customer","path":"a"}""", false },
        { """{"memberKey":"customer","path":"a"}""", true }
    };

    /// <summary>A scoped result read and a legacy dotted request read have no member read, so a member path on them is
    /// invalid rather than answered with the whole read's binding and the member's shape.</summary>
    [Theory]
    [MemberData(nameof(SourcesWithoutMemberRead))]
    public async Task A_member_path_on_a_source_without_a_member_read_is_invalid(string payload, bool memberReads)
    {
        using var provider = memberReads
            ? MemberProvider(Known())
            : Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new FixedShape(Known())));
        var role = payload.Contains("memberKey", StringComparison.Ordinal) ? "WorkflowRequest" : "ActivityResult";
        var document = RequestDocument() with { RootActivity = Sequence(Node("producer"), Node("consumer")) };

        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { Source = Json(role, payload), MemberPath = ["customerId"] });

        Assert.Empty(result.Sources);
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/InvalidSource");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_answer_says_whether_member_reads_are_on(bool memberReads)
    {
        using var provider = memberReads ? MemberProvider(Known()) : Provider();
        var document = Document(Sequence(Node("producer"), Node("consumer", Input(ScopeFree("producer")))));

        var discovery = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        var whole = await Analyzer(provider).AnalyzeAsync(document);

        Assert.Equal(memberReads, discovery.MemberReadsEnabled);
        Assert.Equal(memberReads, whole.MemberReadsEnabled);
    }

    private static (ActivityDataSelectionDocument Document, ArgumentValue Source) Focused(string source) => source switch
    {
        "result" => (Document(Sequence(Node("producer"), Node("consumer"))), ScopeFree("producer")),
        "variable" => (VariableDocument(), new(new VariableReference("customer", "workflow"), "Variable")),
        "request" => (RequestDocument(), Json("WorkflowRequest", """{"memberKey":"customer"}""")),
        "workflow-input" => (RequestDocument(), Json("Input", """{"referenceKey":"customer-ref"}""")),
        "body-input" => (RequestDocument() with { ResourceKind = "ActivityDefinition" }, Json("Input", """{"referenceKey":"customer-ref"}""")),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
