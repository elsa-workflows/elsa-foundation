using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 188, FR-008 at the Elsa 3 collection import: the apply admits every workflow state the mapping produces, a
/// version's and a reusable activity's body alike, before it commits anything. The apply is all or nothing, so a literal
/// mapped onto an input the installed activity declares a credential refuses the whole apply, naming the rule, the node
/// and the input and never the value, and the commit port is never called.
/// </summary>
public sealed class ReusableActivityCollectionCredentialLiteralTests
{
    private const string CredentialInput = "ApiKey";
    private const string NodeId = "send";

    /// <summary>A literal no refusal may echo. Built at run time, never credential-shaped.</summary>
    private static readonly string Literal = $"literal-value-{Guid.NewGuid():N}";

    private readonly ReusableActivityImportFixtures.BuiltInActivityLookup _catalog = new(
        new InputDefinition(CredentialInput, CredentialInput, new TypeReference("String"), null, CredentialInput, null, IsNullable: true)
            with { IsSensitive = true, IsCredential = true });

    private readonly CapturingCommand _command = new();

    /// <summary>Whether the source is a reusable workflow, whose mapped body becomes a graph activity, or a plain workflow version.</summary>
    public static TheoryData<bool> Sources => new() { false, true };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task A_literal_on_a_credential_input_refuses_the_apply_and_commits_nothing(bool reusable)
    {
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => ApplyAsync(reusable, Argument("Literal", Literal)));

        var finding = Assert.Single(refusal.Findings);
        Assert.Equal($"{NodeId}/inputs/{CredentialInput}", finding.Path);
        Assert.Equal("Inputs/CredentialLiteral", finding.Type);
        Assert.StartsWith("Inputs/CredentialLiteral", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(NodeId, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(CredentialInput, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Literal, refusal.Message, StringComparison.Ordinal);
        Assert.Null(_command.Mutation);
    }

    [Fact]
    public async Task The_refused_apply_answers_400_naming_the_rule_node_and_input_and_never_the_value()
    {
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => ApplyAsync(reusable: false, Argument("Literal", Literal)));
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        // The renderer the apply route's endpoint pipeline hands every dispatch failure to.
        await new ReusableActivityImportFaultRenderer().TryWriteAsync(context, refusal);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("Inputs/CredentialLiteral", body, StringComparison.Ordinal);
        Assert.Contains(NodeId, body, StringComparison.Ordinal);
        Assert.Contains(CredentialInput, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Literal, body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task The_same_collection_without_the_literal_is_imported(bool reusable)
    {
        await ApplyAsync(reusable);

        Assert.NotNull(_command.Mutation);
    }

    /// <summary>Applies a one-version collection whose root is the credential activity, carrying <paramref name="properties"/>.</summary>
    private async Task ApplyAsync(bool reusable, params (string Name, JsonElement Value)[] properties)
    {
        var root = new Elsa3Activity
        {
            Id = $"activity-{NodeId}",
            NodeId = NodeId,
            Name = NodeId,
            Type = "Elsa.SendRequest",
            Version = 1,
            CustomProperties = new() { CanStartWorkflow = true },
            AdditionalProperties = properties.ToDictionary(property => property.Name, property => property.Value)
        };
        var collection = ReusableActivityImportFixtures.Collection(ReusableActivityImportFixtures.Workflow("source", "source-v1", 1, reusable, root));
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var importer = new ReusableActivityCollectionImporter(
            analyzer,
            ReusableActivityImportFixtures.Materializer(_catalog),
            _command,
            ReusableActivityImportFixtures.Validator(_catalog));

        await importer.ApplyAsync(new(plan.PlanId, collection, ["source-v1"]));
    }

    private static (string Name, JsonElement Value) Argument(string expressionType, string value) =>
        (CredentialInput, JsonSerializer.SerializeToElement(new { expression = new { type = expressionType, value } }));

    private sealed class CapturingCommand : IReusableActivityImportCommand
    {
        public ReusableActivityImportMutation? Mutation { get; private set; }

        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default)
        {
            Mutation = mutation;
            return ValueTask.FromResult(new ReusableActivityImportCommitResult(false));
        }
    }
}
