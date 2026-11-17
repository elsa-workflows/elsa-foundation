using Elsa.Agent.Core.Models;
using Elsa.Agent.Api.Endpoints;
using Elsa.Persistence.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalEndpoints;

namespace Elsa.Agent.Api;

/// <summary>Maps the agent authoring reads. Credentials stay in Connections; nothing here returns a secret or opens an inference client.</summary>
public static class AgentApi
{
    internal const string OwnerId = "Elsa.Agent.Api";
    internal const string ModelProvidersRoute = "agent/model-providers";
    internal const string ModelCapabilitiesRoute = "agent/model-capabilities";

    // Reading what a model supports is part of authoring an agent's implementation payload.
    internal const string AuthorProvider = "activities/design:author";
    internal const string DesignManage = "activities/design:manage";

    public static void MapAgentApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, AgentApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(AgentApi).Assembly);
    }
}

/// <summary>One registered model provider and the operational connection type its connections use.</summary>
public sealed record AgentModelProviderView(string ProviderKey, string ConnectionTypeName, string DisplayName);

public sealed record AgentModelProvidersResponse(IReadOnlyList<AgentModelProviderView> Items);

/// <summary>The logical model reference a manifest would save.</summary>
public sealed record AgentModelCapabilitiesRequest(string ProviderKey, string ConnectionName, string ModelId);

/// <summary>Current authorized capabilities of one connection's model, read without inference.</summary>
public sealed record AgentModelCapabilitiesView(JsonElement SettingsSchema, bool SupportsJsonSchema, int MaximumOutputTokens);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AgentSourceCreate))]
[JsonSerializable(typeof(AgentSourceSave))]
[JsonSerializable(typeof(AgentSourceRead))]
[JsonSerializable(typeof(AgentSourceHistory))]
[JsonSerializable(typeof(AgentSourceRevisionRead))]
[JsonSerializable(typeof(AgentSourceSnapshotRead))]
[JsonSerializable(typeof(AgentSourceAllHistory))]
[JsonSerializable(typeof(AgentRevisionPage))]
[JsonSerializable(typeof(AgentSourceFork))]
[JsonSerializable(typeof(AgentSourceList))]
[JsonSerializable(typeof(AgentSourceCapture))]
[JsonSerializable(typeof(AgentSourceDiscard))]
[JsonSerializable(typeof(AgentSourceDraft))]
[JsonSerializable(typeof(AgentSourcePage))]
[JsonSerializable(typeof(AgentDefinitionPage))]
[JsonSerializable(typeof(AgentSourceRevision))]
[JsonSerializable(typeof(IReadOnlyList<AgentSourceRevision>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(AgentModelProvidersResponse))]
[JsonSerializable(typeof(AgentModelCapabilitiesRequest))]
[JsonSerializable(typeof(AgentModelCapabilitiesView))]
internal partial class AgentApiJsonContext : JsonSerializerContext
{
    internal static AgentApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly AgentApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>Known refusals become plain problems; an unavailable connection never explains which check failed.</summary>
internal sealed class AgentApiFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is Elsa.Primitives.Exceptions.SchemaWriteRefusedException refusal)
        {
            // Preserve the framework's actionable schema/dormancy metadata and stable refusal code.
            await new Elsa.Api.AspNetCore.ElsaFallbackEndpointProblemWriter().WriteAsync(context, Elsa.Api.AspNetCore.SchemaWriteRefusalProblem.For(refusal));
            return true;
        }
        if (exception is SourceRevisionException source)
        {
            var code = source.Code;
            await Results.Problem(statusCode: code == "not-found" ? 404 : code is "storage-unavailable" or "outcome-unknown" ? 503 : 409,
                title: "The Agent source operation was refused.", extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
            return true;
        }
        var (status, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status422UnprocessableEntity, "The Agent source is invalid."),
            AgentModelUnavailableException => (StatusCodes.Status422UnprocessableEntity, "The selected model connection is unavailable for agents."),
            _ => (StatusCodes.Status500InternalServerError, "The agent operation failed.")
        };
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
        return true;
    }
}

internal sealed class AgentModelUnavailableException(Exception? inner = null) : Exception("The selected model connection is unavailable for agents.", inner);
