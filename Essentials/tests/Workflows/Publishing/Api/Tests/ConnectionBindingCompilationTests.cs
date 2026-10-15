using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A connection-typed input compiles to an inline name-and-type literal. A string credential on
/// that input is refused on both <c>CompileAll</c> overloads so it never enters publication JSON.
/// </summary>
public sealed class ConnectionBindingCompilationTests
{
    private const string NodeId = "http-1";
    private const string InputKey = "Connection";
    private static readonly string Sentinel = $"Bearer sk-live-{Guid.NewGuid():N}";
    private static readonly string ConnectionAlias = typeof(ConnectionReference).FullName!;

    private readonly RuntimeInputBindingCompiler _compiler = new(TestWellKnownTypeRegistry.Create());

    [Fact]
    public void A_connection_binding_compiles_to_an_inline_name_and_type()
    {
        var state = Bound(new { connection = "acme.api", type = "http" }, "Connection");
        var catalog = Assert.Single(_compiler.CompileAll(NodeId, [CatalogInput()], [state])).Value;
        var pinned = Assert.Single(_compiler.CompileAll(NodeId, [PinnedContract()], [state])).Value;

        Assert.Equal(RuntimeInputBindingSource.Literal, catalog.Source);
        Assert.Equal(RuntimeInputBindingSource.Literal, pinned.Source);
        var text = catalog.Literal!.InlineValue!.Value.GetRawText();
        Assert.Contains("acme.api", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(text, pinned.Literal!.InlineValue!.Value.GetRawText());
    }

    [Theory]
    [InlineData("Literal")]
    [InlineData("Secret")]
    [InlineData("JavaScript")]
    public void A_non_connection_binding_is_refused_on_both_compile_overloads(string expressionType)
    {
        var state = Bound(Sentinel, expressionType);
        var catalog = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [CatalogInput()], [state]));
        var pinned = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [PinnedContract()], [state]));

        Assert.Contains("Connection reference", catalog.Message, StringComparison.Ordinal);
        Assert.Contains("Connection reference", pinned.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, catalog.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, pinned.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_connection_type_is_refused()
    {
        var state = Bound(new { connection = "smtp-one", type = "smtp" }, "Connection");
        var exception = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [CatalogInput()], [state]));
        Assert.Contains("http", exception.Message, StringComparison.Ordinal);
        Assert.Contains("smtp", exception.Message, StringComparison.Ordinal);
    }

    private static InputDefinition CatalogInput() => new(
        InputKey,
        InputKey,
        new TypeReference(ConnectionAlias),
        null,
        InputKey,
        null,
        IsNullable: true,
        ConnectionType: "http");

    private static RuntimeActivityInputContract PinnedContract() => new(
        InputKey,
        InputKey,
        new ValueTypeDescriptor(ConnectionAlias),
        isRequired: false,
        isNullable: true,
        hasDefault: false,
        defaultValue: null,
        ActivityValuePolicy.Default);

    private static WorkflowArgumentState Bound(object value, string expressionType) =>
        new(InputKey, new ArgumentValue(JsonSerializer.SerializeToElement(value), expressionType), null, null, null, null);
}
