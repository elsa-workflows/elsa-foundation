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
    [InlineData("")]
    [InlineData(null)]
    public void A_blank_connection_placeholder_compiles_like_an_omitted_optional_input(string? value)
    {
        var state = Bound(value, "Literal");
        var omittedCatalog = Assert.Single(_compiler.CompileAll(NodeId, [CatalogInput()], [])).Value;
        var omittedPinned = Assert.Single(_compiler.CompileAll(NodeId, [PinnedContract()], [])).Value;
        var blankCatalog = Assert.Single(_compiler.CompileAll(NodeId, [CatalogInput()], [state])).Value;
        var blankPinned = Assert.Single(_compiler.CompileAll(NodeId, [PinnedContract()], [state])).Value;

        Assert.Equal(omittedCatalog.Source, blankCatalog.Source);
        Assert.Equal(omittedCatalog.Literal!.InlineValue, blankCatalog.Literal!.InlineValue);
        Assert.Equal(omittedPinned.Source, blankPinned.Source);
        Assert.Equal(omittedPinned.Literal!.InlineValue, blankPinned.Literal!.InlineValue);
    }

    [Fact]
    public void A_blank_required_connection_remains_required()
    {
        var state = Bound("", "Literal");
        var catalog = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [CatalogInput(isRequired: true)], [state]));
        var pinned = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [PinnedContract(isRequired: true)], [state]));

        Assert.Contains("omits required input", catalog.Message, StringComparison.Ordinal);
        Assert.Contains("omits required input", pinned.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_legacy_null_connection_default_does_not_supply_a_value(bool savedBlank)
    {
        var input = CatalogInputWithSerializedDefault("Connection", null);
        WorkflowArgumentState[] states = savedBlank ? [Bound("", "Literal")] : [];
        var omitted = Assert.Single(_compiler.CompileAll(NodeId, [CatalogInput()], [])).Value;
        var actual = Assert.Single(_compiler.CompileAll(NodeId, [input], states)).Value;

        Assert.Equal(omitted.Source, actual.Source);
        Assert.Equal(omitted.Literal!.InlineValue, actual.Literal!.InlineValue);
        Assert.Equal("Connection", input.DefaultSyntax);

        var required = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [input with { IsRequired = true }], states));
        Assert.Contains("omits required input", required.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_null_connection_default_is_still_refused()
    {
        var input = CatalogInputWithSerializedDefault("Connection", new { connection = "acme.api", type = "http" });
        var error = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [input], []));

        Assert.Contains("Canonical activity defaults must be pinned literals", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_literal_null_connection_default_is_still_declared()
    {
        var input = CatalogInputWithSerializedDefault("Literal", null);

        Assert.Equal(JsonValueKind.Null, input.DefaultValue?.ValueKind);
        _compiler.CompileAll(NodeId, [input], [Bound(null, "Default")]);
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
        var catalog = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [CatalogInput()], [state]));
        var pinned = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [PinnedContract()], [state]));
        Assert.Contains("http", catalog.Message, StringComparison.Ordinal);
        Assert.Contains("smtp", catalog.Message, StringComparison.Ordinal);
        Assert.Contains("http", pinned.Message, StringComparison.Ordinal);
        Assert.Contains("smtp", pinned.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_connection_reference_is_refused()
    {
        var state = Bound(new { connection = "", type = "http" }, "Connection");
        var catalog = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [CatalogInput()], [state]));
        var pinned = Assert.Throws<ArgumentException>(() => _compiler.CompileAll(NodeId, [PinnedContract()], [state]));

        Assert.Contains("Connection reference", catalog.Message, StringComparison.Ordinal);
        Assert.Contains("Connection reference", pinned.Message, StringComparison.Ordinal);
    }

    private static InputDefinition CatalogInput(bool isRequired = false) => new(
        InputKey,
        InputKey,
        new TypeReference(ConnectionAlias),
        null,
        InputKey,
        null,
        IsNullable: true,
        IsRequired: isRequired,
        ConnectionType: "http");

    private static InputDefinition CatalogInputWithSerializedDefault(string syntax, object? value)
    {
        var json = JsonSerializer.SerializeToNode(CatalogInput() with { DefaultSyntax = syntax }, JsonSerializerOptions.Web)!.AsObject();
        json["defaultValue"] = JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web);
        return json.Deserialize<InputDefinition>(JsonSerializerOptions.Web)!;
    }

    private static RuntimeActivityInputContract PinnedContract(bool isRequired = false) => new(
        InputKey,
        InputKey,
        new ValueTypeDescriptor(ConnectionAlias),
        isRequired: isRequired,
        isNullable: true,
        hasDefault: false,
        defaultValue: null,
        ActivityValuePolicy.Default,
        connectionType: "http");

    private static WorkflowArgumentState Bound(object? value, string expressionType) =>
        new(InputKey, new ArgumentValue(JsonSerializer.SerializeToElement(value, Elsa.Serialization.Core.JsonBaseline.Options), expressionType), null, null, null, null);
}
