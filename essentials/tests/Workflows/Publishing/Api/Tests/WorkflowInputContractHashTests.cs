using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowInputContractHashTests
{
    [Fact]
    public void Input_nullability_changes_the_behavioral_hash()
    {
        var type = new TypeReference("String");

        Assert.NotEqual(
            Hash(new WorkflowDeclaredInput("value", type, false, defaultValue: null, isNullable: false)),
            Hash(new WorkflowDeclaredInput("value", type, false, defaultValue: null, isNullable: true)));
    }

    [Fact]
    public void Default_false_nullability_preserves_the_existing_hash()
    {
        var type = new TypeReference("String");

        Assert.Equal(
            Hash(new WorkflowDeclaredInput("value", type, false)),
            Hash(new WorkflowDeclaredInput("value", type, false, defaultValue: null, isNullable: false)));
    }

    [Fact]
    public void An_explicit_null_default_is_distinct_from_an_absent_default()
    {
        var type = new TypeReference("String");
        var nullDefault = JsonSerializer.SerializeToElement<string?>(null);

        Assert.NotEqual(
            Hash(new WorkflowDeclaredInput("value", type, false, defaultValue: null, isNullable: true)),
            Hash(new WorkflowDeclaredInput("value", type, false, defaultValue: nullDefault, isNullable: true)));
    }

    [Fact]
    public void Legacy_absent_default_json_retains_its_behavioral_hash()
    {
        const string legacy = """
            {"name":"value","type":{"alias":"String","collectionKind":"Single"},"isRequired":false,"defaultValue":null}
            """;
        var restored = JsonSerializer.Deserialize<WorkflowDeclaredInput>(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(Hash(new WorkflowDeclaredInput("value", new TypeReference("String"), false)), Hash(restored));
    }

    [Fact]
    public void An_explicit_null_default_retains_its_behavioral_hash_after_reload()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var input = new WorkflowDeclaredInput("value", new TypeReference("Int32"), false,
            JsonSerializer.SerializeToElement<int?>(null), isNullable: true);
        var restored = JsonSerializer.Deserialize<WorkflowDeclaredInput>(JsonSerializer.Serialize(input, options), options)!;

        Assert.Equal(Hash(input), Hash(restored));
    }

    private static string Hash(WorkflowDeclaredInput input) => new WorkflowExecutableHasher().ComputeHash(
        WholeValueBindingHashTests.Node(new Dictionary<string, RuntimeInputBinding>()),
        new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, [input]), []);
}