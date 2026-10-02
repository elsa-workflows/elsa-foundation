using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 188, FR-008 at the Elsa 3 collection import, composed as a host composes it: the apply judges every activity node
/// the commit would store, in a workflow version and in a reusable activity's body alike, the nodes the mapping nests
/// under containers at any depth included, before it commits anything. The apply is all or nothing, so a literal mapped
/// onto an input the installed activity declares a credential refuses the whole apply with one 400 naming the rule, and
/// the node and the input of every refused binding, never the value, and the commit port is never called.
/// </summary>
public sealed class ReusableActivityCollectionCredentialLiteralTests : IAsyncDisposable
{
    private const string CredentialInput = "ApiKey";
    private const string NodeId = "send";

    /// <summary>A literal no refusal may echo. Built at run time, never credential-shaped.</summary>
    private static readonly string Literal = $"literal-value-{Guid.NewGuid():N}";

    private readonly ReusableActivityImportFixtures.CapturingCommand _command = new();
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;

    public ReusableActivityCollectionCredentialLiteralTests()
    {
        var catalog = new ReusableActivityImportFixtures.BuiltInActivityLookup(
            new InputDefinition(CredentialInput, CredentialInput, new TypeReference("String"), null, CredentialInput, null, IsNullable: true)
                with { IsSensitive = true, IsCredential = true });
        _services = ReusableActivityImportFixtures.ImportServices(catalog, _command);
        _scope = _services.CreateAsyncScope();
    }

    /// <summary>
    /// Whether the source is a reusable workflow, whose mapped body becomes a graph activity, or a plain workflow version;
    /// and how many containers the credential activity sits under (0: it is the root).
    /// </summary>
    public static TheoryData<bool, int> SourcesAndDepths => new()
    {
        { false, 0 }, { false, 1 }, { false, 2 },
        { true, 0 }, { true, 1 }, { true, 2 }
    };

    [Theory]
    [MemberData(nameof(SourcesAndDepths))]
    public async Task A_literal_on_a_credential_input_at_any_depth_refuses_the_apply_with_400_and_commits_nothing(bool reusable, int depth)
    {
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() =>
            ApplyAsync(Source("source", reusable, depth, NodeId, Argument("Literal", Literal))));

        var finding = Assert.Single(refusal.Findings);
        Assert.Equal($"{NodeId}/inputs/{CredentialInput}", finding.Path);
        Assert.Equal("Inputs/CredentialLiteral", finding.Type);
        Assert.Null(_command.Mutation);
        var (status, body) = await RenderAsync(refusal);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.DoesNotContain(Literal, body, StringComparison.Ordinal);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal("elsa3.import.request-invalid", problem.GetProperty("errorCode").GetString());
        Assert.Equal(
            $"Inputs/CredentialLiteral: input '{CredentialInput}' on activity '{NodeId}' holds a credential and accepts only a secret reference.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Every_refused_binding_of_the_apply_is_named_in_one_refusal()
    {
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => ApplyAsync(
            Source("plain", reusable: false, depth: 2, "plain-send", Argument("Literal", Literal)),
            Source("reusable", reusable: true, depth: 1, "reusable-send", Argument("JavaScript", "'text'"))));

        Assert.Equal(
            ["plain-send/inputs/ApiKey", "reusable-send/inputs/ApiKey"],
            refusal.Findings.Select(finding => finding.Path).Order(StringComparer.Ordinal));
        Assert.Null(_command.Mutation);
    }

    [Theory]
    [MemberData(nameof(SourcesAndDepths))]
    public async Task The_same_collection_with_a_secret_reference_is_imported(bool reusable, int depth)
    {
        await ApplyAsync(Source("source", reusable, depth, NodeId, Argument("Secret", new { name = "service-key" })));

        Assert.NotNull(_command.Mutation);
    }

    [Theory]
    [MemberData(nameof(SourcesAndDepths))]
    public async Task The_same_collection_without_the_binding_is_imported(bool reusable, int depth)
    {
        await ApplyAsync(Source("source", reusable, depth, NodeId));

        Assert.NotNull(_command.Mutation);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    /// <summary>Analyzes <paramref name="sources"/> as one collection and applies all of them.</summary>
    private async Task ApplyAsync(params Elsa3WorkflowDefinition[] sources)
    {
        var importer = _scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>();
        var collection = ReusableActivityImportFixtures.Collection(sources);
        var plan = await importer.AnalyzeAsync(collection);

        await importer.ApplyAsync(new(plan.PlanId, collection, sources.Select(source => source.Id).ToArray()));
    }

    /// <summary>
    /// A one-version workflow whose credential activity, <paramref name="nodeId"/>, carries <paramref name="properties"/>
    /// and sits under <paramref name="depth"/> nested containers.
    /// </summary>
    private static Elsa3WorkflowDefinition Source(string definitionId, bool reusable, int depth, string nodeId, params (string Name, JsonElement Value)[] properties)
    {
        var activity = ReusableActivityImportFixtures.Leaf(nodeId, "Elsa.SendRequest");
        activity.AdditionalProperties = properties.ToDictionary(property => property.Name, property => property.Value);
        var root = Enumerable.Range(0, depth).Aggregate(activity, (child, level) =>
        {
            var container = ReusableActivityImportFixtures.Leaf($"{nodeId}-container-{level}", "Elsa.Sequence");
            container.Activities = [child];
            return container;
        });
        return ReusableActivityImportFixtures.Workflow(definitionId, $"{definitionId}-v1", 1, reusable, root);
    }

    private static (string Name, JsonElement Value) Argument(string expressionType, object value) =>
        (CredentialInput, JsonSerializer.SerializeToElement(new { expression = new { type = expressionType, value } }));

    /// <summary>The status and body the apply route answers <paramref name="refusal"/> with, through the renderer its endpoint pipeline hands every dispatch failure to.</summary>
    private static async Task<(int Status, string Body)> RenderAsync(Exception refusal)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await new ReusableActivityImportFaultRenderer().TryWriteAsync(context, refusal);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
