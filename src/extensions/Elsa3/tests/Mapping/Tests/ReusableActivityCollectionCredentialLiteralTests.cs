using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Models;
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
/// the node and the input of every refused binding, never the value, and the commit port is never called. The Elsa 3
/// property may spell the input's name in another case (<c>apiKey</c> for the declared <c>ApiKey</c>): the mapping stores
/// the binding under the declared key, where the rule finds it.
/// </summary>
public sealed class ReusableActivityCollectionCredentialLiteralTests : IAsyncDisposable
{
    private const string CredentialInput = "ApiKey";
    private const string OtherCaseSpelling = "apiKey";
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
    private static readonly (bool Reusable, int Depth)[] Placements =
        [(false, 0), (false, 1), (false, 2), (true, 0), (true, 1), (true, 2)];

    public static IEnumerable<object[]> SourcesAndDepths =>
        Placements.Select(placement => new object[] { placement.Reusable, placement.Depth });

    /// <summary><see cref="Placements"/>, with the Elsa 3 property spelled as declared and in another case.</summary>
    public static IEnumerable<object[]> SourcesDepthsAndSpellings =>
        new[] { CredentialInput, OtherCaseSpelling }.SelectMany(spelling =>
            Placements.Select(placement => new object[] { placement.Reusable, placement.Depth, spelling }));

    [Theory]
    [MemberData(nameof(SourcesDepthsAndSpellings))]
    public async Task A_literal_on_a_credential_input_at_any_depth_refuses_the_apply_with_400_and_commits_nothing(bool reusable, int depth, string propertyName)
    {
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() =>
            ApplyAsync(Source("source", reusable, depth, NodeId, Argument(propertyName, "Literal", Literal))));

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
            Source("plain", reusable: false, depth: 2, "plain-send", Argument(CredentialInput, "Literal", Literal)),
            Source("reusable", reusable: true, depth: 1, "reusable-send", Argument(OtherCaseSpelling, "JavaScript", "'text'"))));

        Assert.Equal(
            ["plain-send/inputs/ApiKey", "reusable-send/inputs/ApiKey"],
            refusal.Findings.Select(finding => finding.Path).Order(StringComparer.Ordinal));
        Assert.Null(_command.Mutation);
    }

    [Fact]
    public async Task A_text_payload_under_the_secret_type_refuses_the_apply_and_commits_nothing()
    {
        // The mapping passes the Elsa 3 expression type through, so an upload can name the Secret type over a text value.
        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() =>
            ApplyAsync(Source("source", reusable: false, depth: 1, NodeId, Argument(CredentialInput, "Secret", Literal))));

        Assert.Equal($"{NodeId}/inputs/{CredentialInput}", Assert.Single(refusal.Findings).Path);
        Assert.Null(_command.Mutation);
        Assert.DoesNotContain(Literal, (await RenderAsync(refusal)).Body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SourcesAndDepths))]
    public async Task Two_spellings_of_the_credential_input_refuse_the_apply_with_400_rather_than_keep_one(bool reusable, int depth)
    {
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => ApplyAsync(Source("source", reusable, depth, NodeId,
            Argument(CredentialInput, "Secret", new { name = "service-key" }),
            Argument(OtherCaseSpelling, "Literal", Literal))));

        Assert.Null(_command.Mutation);
        var (status, body) = await RenderAsync(refusal);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains($"'{CredentialInput}' and '{OtherCaseSpelling}'", JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Literal, body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SourcesDepthsAndSpellings))]
    public async Task The_same_collection_with_a_secret_reference_is_imported_under_the_declared_key(bool reusable, int depth, string propertyName)
    {
        await ApplyAsync(Source("source", reusable, depth, NodeId, Argument(propertyName, "Secret", new { name = "service-key" })));

        Assert.Equal([CredentialInput], StoredBindingKeys());
    }

    [Theory]
    [MemberData(nameof(SourcesDepthsAndSpellings))]
    public async Task The_same_collection_with_the_input_unbound_is_imported_under_the_declared_key(bool reusable, int depth, string propertyName)
    {
        await ApplyAsync(Source("source", reusable, depth, NodeId, (propertyName, JsonSerializer.SerializeToElement<object?>(null))));

        Assert.Equal([CredentialInput], StoredBindingKeys());
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

    private static (string Name, JsonElement Value) Argument(string propertyName, string expressionType, object value) =>
        (propertyName, JsonSerializer.SerializeToElement(new { expression = new { type = expressionType, value } }));

    /// <summary>The reference keys of the bindings the committed mutation stores on the credential activity's node.</summary>
    private IEnumerable<string> StoredBindingKeys()
    {
        var mutation = Assert.IsType<ReusableActivityImportMutation>(_command.Mutation);
        var roots = mutation.Workflows.Select(workflow => workflow.Version.State.RootActivity!)
            .Concat(mutation.Activities.Select(activity => activity.Body.RootActivity!));
        return roots.SelectMany(Elsa3ImportedActivityStructure.Nodes).Single(node => node.NodeId == NodeId).Inputs.Select(input => input.ReferenceKey);
    }

    /// <summary>The status and body the apply route answers <paramref name="refusal"/> with, through the renderer its endpoint pipeline hands every dispatch failure to.</summary>
    private static async Task<(int Status, string Body)> RenderAsync(Exception refusal)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await new ReusableActivityImportFaultRenderer().TryWriteAsync(context, refusal);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
