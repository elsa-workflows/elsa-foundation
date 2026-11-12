using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// Member reads of an activity result: refused while member reads are off; with them on, offered and admitted by the
/// same decision as the whole read of the same result, then typed by the producer's serialized shape.
/// </summary>
public sealed partial class ActivityDataSelectionAnalyzerTests
{
    [Fact]
    public async Task With_member_reads_off_a_result_member_read_is_refused_and_a_member_path_query_offers_the_whole_read()
    {
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new FixedShape(Known())));

        var stored = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer"))))));
        var offered = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))),
            new("consumer", "CustomerId"), new() { Source = ScopeFree("producer"), MemberPath = ["customerId"] });

        Assert.Equal("ActivityData/UnsupportedMemberBinding", Assert.Single(stored.Findings).Type);
        Assert.Empty(stored.MemberReads);
        Assert.Equal("ActivityResult", Assert.Single(offered.Sources).Binding!.ExpressionType);
    }

    [Fact]
    public async Task A_typed_member_read_is_admitted_with_the_selected_member_facts()
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer", ["address", "a.b", ""]))))));

        Assert.Empty(result.Findings);
        var facts = Assert.Single(result.MemberReads);
        Assert.Equal("consumer/inputs/CustomerId", facts.Path);
        Assert.Equal("producer", facts.ProducerNodeId);
        Assert.Equal("$result", facts.ProjectionKey);
        Assert.Equal(["address", "a.b", ""], facts.MemberPath.ToArray());
        Assert.True(facts.IsKnown);
        Assert.Equal("String", facts.SelectedType!.Alias);
        Assert.True(facts.MayBeAbsent);
        Assert.False(facts.MayBeNull);
        Assert.Equal(ValueRepresentation.TextValue, facts.Representation);
        Assert.Equal("profile", facts.ProfileRevision);
    }

    [Theory]
    [InlineData("activity.shape.dynamic-json")]
    [InlineData("activity.shape.converter-opaque")]
    [InlineData("activity.shape.metadata-unavailable")]
    public async Task A_member_read_whose_shape_is_uncertain_is_admitted_as_not_known(string reason)
    {
        using var provider = MemberProvider(new ActivitySerializedShape(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, null,
            "contract", "profile", "metadata") { ReasonCode = reason });

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer"))))));

        Assert.Empty(result.Findings);
        var facts = Assert.Single(result.MemberReads);
        Assert.False(facts.IsKnown);
        Assert.Null(facts.SelectedType);
    }

    [Fact]
    public async Task A_member_read_without_a_shape_owner_is_admitted_as_not_known()
    {
        using var provider = Provider(MemberReadsOn);

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer"))))));

        Assert.Empty(result.Findings);
        Assert.False(Assert.Single(result.MemberReads).IsKnown);
    }

    [Theory]
    [InlineData("activity.shape.member-not-declared", "ActivityData/UnknownMember")]
    [InlineData("activity.shape.member-parent-not-object", "ActivityData/MemberParentNotObject")]
    public async Task A_member_the_shape_proves_unreadable_is_refused_and_not_offered(string reason, string finding)
    {
        using var provider = MemberProvider(new ActivitySerializedShape(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, null,
            "contract", "profile", "metadata") { ReasonCode = reason });

        var stored = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer"))))));
        var offered = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))),
            new("consumer", "CustomerId"), new() { Source = ScopeFree("producer"), MemberPath = ["customerId"] });

        Assert.Equal(("consumer/inputs/CustomerId", finding), (Assert.Single(stored.Findings).Path, stored.Findings[0].Type));
        Assert.Empty(stored.MemberReads);
        var source = Assert.Single(offered.Sources);
        Assert.Null(source.Binding);
        Assert.Equal(finding["ActivityData/".Length..], source.ReasonCode);
    }

    public static TheoryData<string, string> MalformedMemberReads => new()
    {
        { """{"producerNodeId":"producer","projectionKey":"$result","producerScopeId":"root","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"producerNodeId":"producer","projectionKey":"$result","path":"a","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"projectionKey":"$result","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"producerNodeId":"producer","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"producerNodeId":"producer","projectionKey":"$result"}""", "ActivityData/InvalidMemberPath" },
        { """{"producerNodeId":"producer","projectionKey":"$result","memberPath":[]}""", "ActivityData/InvalidMemberPath" },
        { """{"producerNodeId":"producer","projectionKey":"$result","memberPath":"a"}""", "ActivityData/InvalidMemberPath" },
        { """{"producerNodeId":"producer","projectionKey":"$result","memberPath":["a",null]}""", "ActivityData/InvalidMemberPath" },
        { """{"producerNodeId":"producer","projectionKey":"$result","memberPath":["a",1]}""", "ActivityData/InvalidMemberPath" }
    };

    [Theory]
    [MemberData(nameof(MalformedMemberReads))]
    public async Task A_malformed_member_read_is_a_finding_not_an_exception(string payload, string finding)
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"),
            Node("consumer", Input(new(JsonDocument.Parse(payload).RootElement.Clone(), "ActivityResultMember"))))));

        Assert.Equal(("consumer/inputs/CustomerId", finding), (Assert.Single(result.Findings).Path, result.Findings[0].Type));
    }

    [Theory]
    [InlineData(64, 1, true)]
    [InlineData(65, 1, false)]
    [InlineData(1, 4096, true)]
    [InlineData(2, 2049, false)]
    public async Task A_member_path_is_bounded_in_segments_and_length(int segments, int length, bool admitted)
    {
        using var provider = MemberProvider(Known());
        string[] path = [.. Enumerable.Repeat(new string('a', length), segments)];

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer", path))))));

        Assert.Equal(admitted, result.Findings.IsEmpty);
        if (!admitted)
            Assert.Equal("ActivityData/InvalidMemberPath", Assert.Single(result.Findings).Type);
    }

    /// <summary>
    /// What is offered is what is admitted: the member binding offered for a focused result and path is stored
    /// unchanged and admitted, with the exact segments echoed and no scope.
    /// </summary>
    [Fact]
    public async Task The_offered_member_read_is_the_admitted_one()
    {
        using var provider = MemberProvider(Known());
        var document = Document(Sequence(Node("producer"), Node("consumer")));

        var offered = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { Source = ScopeFree("producer"), MemberPath = ["a.b", ""] });
        var binding = Assert.Single(offered.Sources).Binding!;
        var stored = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(binding)))));

        Assert.Empty(offered.Findings);
        Assert.Equal("ActivityResultMember", binding.ExpressionType);
        var payload = Assert.IsType<JsonElement>(binding.Value);
        Assert.Equal(["a.b", ""], payload.GetProperty("memberPath").EnumerateArray().Select(segment => segment.GetString()));
        Assert.False(payload.TryGetProperty("producerScopeId", out _));
        Assert.Empty(stored.Findings);
        Assert.Single(stored.MemberReads);
    }

    /// <summary>A member read is refused exactly where, and with the same code as, the whole read of the same result.</summary>
    [Fact]
    public async Task A_member_read_is_refused_with_the_whole_reads_reason_where_the_whole_read_is()
    {
        using var provider = MemberProvider(Known());

        var whole = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(ScopeFree("producer"))), Node("producer"))));
        var member = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(Member("producer"))), Node("producer"))));

        Assert.NotEmpty(whole.Findings);
        Assert.Equal(whole.Findings.Select(finding => (finding.Path, finding.Type)), member.Findings.Select(finding => (finding.Path, finding.Type)));
        Assert.Empty(member.MemberReads);
    }

    [Fact]
    public async Task A_member_read_passes_an_owning_restriction_that_admits_the_whole_result_read()
    {
        using var provider = MemberProvider(Known(), services =>
            services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint(["ActivityResult"])));

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(Member("producer"))))));

        Assert.DoesNotContain(result.Findings, finding => finding.Type == "ActivityData/SourceRestricted");
    }

    /// <summary>
    /// A result member read the whole-read rules refuse is not typed as well, so a read that names an undeclared member
    /// of a producer that is not available reports the one refusal of the whole read, not two.
    /// </summary>
    [Fact]
    public async Task A_member_read_the_whole_read_refuses_reports_one_finding_even_for_an_unknown_member()
    {
        using var provider = MemberProvider(new ActivitySerializedShape(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, null,
            "contract", "profile", "metadata") { ReasonCode = "activity.shape.member-not-declared" });

        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(Member("producer"))), Node("producer"))));

        var finding = Assert.Single(result.Findings);
        Assert.NotEqual("ActivityData/UnknownMember", finding.Type);
    }

    [Fact]
    public async Task With_member_reads_off_a_variable_member_read_is_refused()
    {
        using var provider = Provider();

        var result = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(VariableMember("customer", "workflow"))));

        Assert.Equal("ActivityData/UnsupportedMemberBinding", Assert.Single(result.Findings).Type);
    }

    [Fact]
    public async Task A_variable_member_read_is_admitted_like_the_whole_read_and_never_typed()
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(VariableMember("customer", "workflow", ["address", "a.b", ""]))));

        Assert.Empty(result.Findings);
        Assert.Empty(result.MemberReads);
    }

    public static TheoryData<string, string> MalformedVariableMemberReads => new()
    {
        { """{"referenceKey":"customer","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"referenceKey":"customer","declaringScopeId":"","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"declaringScopeId":"workflow","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"referenceKey":"customer","declaringScopeId":"workflow","path":"a","memberPath":["a"]}""", "ActivityData/InvalidSource" },
        { """{"referenceKey":"customer","declaringScopeId":"workflow"}""", "ActivityData/InvalidMemberPath" },
        { """{"referenceKey":"customer","declaringScopeId":"workflow","memberPath":[]}""", "ActivityData/InvalidMemberPath" },
        { """{"referenceKey":"customer","declaringScopeId":"workflow","memberPath":["a",1]}""", "ActivityData/InvalidMemberPath" }
    };

    [Theory]
    [MemberData(nameof(MalformedVariableMemberReads))]
    public async Task A_malformed_variable_member_read_is_a_finding(string payload, string finding)
    {
        using var provider = MemberProvider(Known());

        var result = await Analyzer(provider).AnalyzeAsync(VariableDocument(
            Input(new(JsonDocument.Parse(payload).RootElement.Clone(), "VariableMember"))));

        Assert.Equal(("consumer/inputs/CustomerId", finding), (Assert.Single(result.Findings).Path, result.Findings[0].Type));
    }

    [Fact]
    public async Task A_variable_member_read_of_an_unknown_variable_is_refused_with_the_whole_reads_code()
    {
        using var provider = MemberProvider(Known());

        var whole = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(new(new VariableReference("missing", "workflow"), "Variable"))));
        var member = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(VariableMember("missing", "workflow"))));

        Assert.Equal("Variable/UnresolvableReference", Assert.Single(whole.Findings).Type);
        Assert.Equal(whole.Findings.Select(finding => (finding.Path, finding.Type)), member.Findings.Select(finding => (finding.Path, finding.Type)));
    }

    [Fact]
    public async Task The_offered_variable_member_read_is_the_admitted_one()
    {
        using var provider = MemberProvider(Known());

        var offered = await Analyzer(provider).AnalyzeAsync(VariableDocument(), new("consumer", "CustomerId"),
            new() { Source = new(new VariableReference("customer", "workflow"), "Variable"), MemberPath = ["a.b", ""] });
        var binding = Assert.Single(offered.Sources).Binding!;
        var stored = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(binding)));

        Assert.Empty(offered.Findings);
        Assert.Equal("VariableMember", binding.ExpressionType);
        var payload = Assert.IsType<JsonElement>(binding.Value);
        Assert.Equal("workflow", payload.GetProperty("declaringScopeId").GetString());
        Assert.Equal(["a.b", ""], payload.GetProperty("memberPath").EnumerateArray().Select(segment => segment.GetString()));
        Assert.Empty(stored.Findings);
    }

    [Fact]
    public async Task A_variable_member_read_passes_an_owning_restriction_that_admits_the_whole_variable_read()
    {
        using var provider = MemberProvider(Known(), services =>
            services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint(["Variable"])));

        var result = await Analyzer(provider).AnalyzeAsync(VariableDocument(Input(VariableMember("customer", "workflow"))));

        Assert.Empty(result.Findings);
    }

    private static ActivityDataSelectionDocument VariableDocument(params ArgumentState[] inputs) =>
        Document(Sequence(Node("consumer", inputs))) with { Variables = [Variable("customer", "Customer")] };

    private static ArgumentValue VariableMember(string key, string scope, string[]? path = null) =>
        new(JsonSerializer.SerializeToElement(new { referenceKey = key, declaringScopeId = scope, memberPath = path ?? ["customerId"] }), "VariableMember");

    private static void MemberReadsOn(IServiceCollection services) =>
        services.Configure<WorkflowDesignValidatorOptions>(options => options.MemberReads = true);

    private static ServiceProvider MemberProvider(ActivitySerializedShape shape, Action<IServiceCollection>? configure = null) => Provider(services =>
    {
        MemberReadsOn(services);
        services.AddSingleton<IActivitySerializedShapeSource>(new FixedShape(shape));
        configure?.Invoke(services);
    });

    private static ActivitySerializedShape Known() => new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Scalar, new("String"),
        "contract", "profile", "metadata")
    {
        IsNullable = false, Presence = ActivitySerializedPresence.Optional, Representation = ValueRepresentation.TextValue
    };

    private static ArgumentValue ScopeFree(string producer) =>
        new(JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = "$result" }), "ActivityResult");

    private static ArgumentValue Member(string producer, string[]? path = null) =>
        new(JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = "$result", memberPath = path ?? ["customerId"] }), "ActivityResultMember");

    // Describes every result the same way, so the classification of one selected shape can be pinned.
    private sealed class FixedShape(ActivitySerializedShape shape) : IActivitySerializedShapeSource
    {
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(memberPath.IsEmpty
                ? new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Object, new("Object"), "contract", "profile", "metadata")
                : shape);

        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
