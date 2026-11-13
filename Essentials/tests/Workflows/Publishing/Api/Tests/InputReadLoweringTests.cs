using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The one lowering of authored input reads: what it lowers, what it leaves as authored, and the declared-input index
/// it asks at workflow level.
/// </summary>
public sealed class InputReadLoweringTests
{
    private static string? Name(string referenceKey) => referenceKey == "customer-ref" ? "customer" : null;

    [Fact]
    public void An_input_member_read_becomes_the_request_member_read_with_its_path_exactly_as_authored()
    {
        // Even a path the request member read will refuse is carried unchanged, so it is refused for that path.
        var lowered = InputReadLowering.Lower(new ArgumentValue(
            JsonDocument.Parse("""{"referenceKey":"customer-ref","memberPath":["a.b","",1]}""").RootElement.Clone(), "InputMember"), Name);

        Assert.Equal(ActivityDataReadRoles.WorkflowRequestMember, lowered.ExpressionType);
        var payload = Assert.IsType<JsonElement>(lowered.Value);
        Assert.Equal("customer", payload.GetProperty("memberKey").GetString());
        Assert.Equal("""["a.b","",1]""", payload.GetProperty("memberPath").GetRawText());
        Assert.False(payload.TryGetProperty("path", out _));
    }

    [Fact]
    public void An_input_read_becomes_the_whole_request_read()
    {
        var stored = InputReadLowering.Lower(new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref" }), "Input"), Name);
        var inProcess = InputReadLowering.Lower(new ArgumentValue(new VariableReference("customer-ref"), "Input"), Name);

        Assert.All([stored, inProcess], lowered =>
        {
            Assert.Equal("WorkflowRequest", lowered.ExpressionType);
            Assert.Equal("""{"memberKey":"customer"}""", Assert.IsType<JsonElement>(lowered.Value).GetRawText());
        });
    }

    public static TheoryData<string, object?> LeftAsAuthored => new()
    {
        // An unknown reference key, a member path on the whole-value role, and payloads not in a handed-over form.
        { "Input", JsonSerializer.SerializeToElement(new { referenceKey = "missing" }) },
        { "InputMember", JsonSerializer.SerializeToElement(new { referenceKey = "missing", memberPath = new[] { "a" } }) },
        { "Input", JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref", memberPath = new[] { "a" } }) },
        { "Input", new { referenceKey = "customer-ref" } },
        { "Input", new { ReferenceKey = "customer-ref" } },
        { "InputMember", new VariableReference("customer-ref") },
        { "Variable", JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref" }) }
    };

    [Theory]
    [MemberData(nameof(LeftAsAuthored))]
    public void Anything_else_is_left_as_authored(string role, object? payload)
    {
        var value = new ArgumentValue(payload, role);

        Assert.Same(value, InputReadLowering.Lower(value, Name));
    }

    [Fact]
    public void The_declared_input_index_names_an_input_only_when_exactly_one_has_the_reference_key()
    {
        var index = new DeclaredInputIndex([
            new InputDefinition("one", "One", new("String"), null, "One", null, true),
            new InputDefinition("twice", "First", new("String"), null, "First", null, true),
            new InputDefinition("twice", "Second", new("String"), null, "Second", null, true)
        ]);

        Assert.True(index.TryFind("one", out var one));
        Assert.Equal("One", one.Name);
        Assert.False(index.TryFind("twice", out _));
        Assert.False(index.TryFind("missing", out _));
        Assert.False(index.TryFind("ONE", out _));
    }
}
