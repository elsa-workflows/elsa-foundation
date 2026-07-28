using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Api.Commands;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

// Contract guard for elsa-foundation#951: the AddDefinition request must bind whether or not the client
// supplies an operationKey / initialState. Uses the same JsonSerializerOptions shape the shell's
// FastEndpoints request deserializer is configured with (camelCase + string enums).
public sealed class AddDefinitionDeserializationTests
{
    private static readonly JsonSerializerOptions Options = CreateFastEndpointsOptions();

    private static JsonSerializerOptions CreateFastEndpointsOptions()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        result.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return result;
    }

    [Fact]
    public void Request_without_operation_key_binds_with_a_null_operation_key()
    {
        // The Studio create dialog does not send an operationKey; it must still bind (the handler resolves
        // the missing key, see AddDefinitionOperationKeyTests). Before the fix, OperationKey was a required
        // non-nullable positional parameter — this documents that it is now optional on the wire.
        var command = JsonSerializer.Deserialize<AddDefinition>("""{"name":"X"}""", Options);
        Assert.NotNull(command);
        Assert.Null(command!.OperationKey);
        Assert.Null(command.InitialState);
    }

    [Fact]
    public void Empty_initial_state_binds()
    {
        var command = JsonSerializer.Deserialize<AddDefinition>("""{"name":"X","initialState":{}}""", Options);
        Assert.NotNull(command);
        Assert.NotNull(command!.InitialState);
    }

    [Fact]
    public void Supplied_operation_key_is_preserved()
    {
        var command = JsonSerializer.Deserialize<AddDefinition>("""{"operationKey":"op-1","name":"X"}""", Options);
        Assert.NotNull(command);
        Assert.Equal("op-1", command!.OperationKey);
    }
}
