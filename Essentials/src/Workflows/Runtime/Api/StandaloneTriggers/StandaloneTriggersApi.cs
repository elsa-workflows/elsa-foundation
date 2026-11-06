using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal static class StandaloneTriggersApi
{
    internal const string Owner = "Elsa.Workflows.Runtime.Api.StandaloneTriggers";
    internal static readonly JsonSerializerOptions Json = CreateJson();

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapEndpointGroup(Owner, StandaloneTriggersJsonContext.Default, jsonContentType: "application/json");
        void Get(string path, string name, Type response, Func<HttpContext, Task<object>> action, params string[] permissions) =>
            api.MapUnboundOperation("GET", path, name, response, 200, null, context => ExecuteAsync(context, action), containFailures: false)
                .RequireAnyPermission(permissions);
        void Post(string path, string name, Type response, Func<HttpContext, JsonElement, Task<object>> action, int status = 200) =>
            api.MapUnboundOperation("POST", path, name, response, status, null, context => ExecuteAsync(context,
                    async current => await action(current, await ReadBodyAsync(current))), containFailures: false)
                .RequireAnyPermission(TriggerPermissions.Manage, TriggerPermissions.Operate, TriggerPermissions.Authorize,
                    TriggerPermissions.Read, TriggerPermissions.Retry, TriggerPermissions.Cancel);
        var readers = new[] { TriggerPermissions.Read, TriggerPermissions.Manage, TriggerPermissions.Operate };
        Get("runtime/standalone-triggers/identity", "StandaloneTriggerIdentity", typeof(TriggerActor),
            async context => await Access(context).ActorAsync(context.RequestAborted), readers);
        Get("runtime/standalone-trigger-types", "StandaloneTriggerTypes", typeof(TriggerPage<TriggerTypeView>),
            async context => await Operations(context).TypesAsync(context.RequestAborted), readers);
        Get("runtime/standalone-triggers", "StandaloneTriggersList", typeof(TriggerPage<TriggerView>), async context =>
        {
            var query = context.Request.Query;
            var state = Value(query, "state");
            TriggerDesiredState? desired = state is null ? null : Enum.TryParse<TriggerDesiredState>(state, false, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new TriggerOperationException("validation", "The requested trigger state is invalid.");
            return await Operations(context).ListAsync(new(Limit(query), Value(query, "after"), Value(query, "search"), Value(query, "typeKey"), desired), context.RequestAborted);
        }, readers);
        Get("runtime/standalone-triggers/{triggerId}", "StandaloneTriggerRead", typeof(TriggerView),
            async context => await Operations(context).ReadAsync(Route(context, "triggerId"), context.RequestAborted), readers);
        Post("runtime/standalone-triggers", "StandaloneTriggerCreate", typeof(TriggerView), async (context, body) =>
        {
            await Access(context).RequireAsync(TriggerPermissions.Manage, null, context.RequestAborted);
            var result = await Operations(context).CreateAsync(body, context.RequestAborted);
            context.Response.StatusCode = 201;
            context.Response.Headers.Location = $"runtime/standalone-triggers/{Uri.EscapeDataString(result.Id)}";
            return result;
        }, 201);
        Get("runtime/standalone-trigger-subscriptions", "StandaloneTriggerSubscriptions", typeof(TriggerPage<TriggerSubscriptionView>), async context =>
        {
            var query = context.Request.Query;
            return await Operations(context).SubscriptionsAsync(new(Value(query, "triggerId"), Value(query, "definitionId"), Value(query, "slotName"), Limit(query), Value(query, "after")), context.RequestAborted);
        }, readers);
        Get("runtime/standalone-trigger-subscriptions/{subscriptionId}", "StandaloneTriggerSubscription", typeof(TriggerSubscriptionView),
            async context => await Operations(context).SubscriptionAsync(Route(context, "subscriptionId"), context.RequestAborted), readers);
        Get("runtime/standalone-trigger-targets", "StandaloneTriggerTargets", typeof(TriggerPage<TriggerTargetView>),
            async context => await Operations(context).TargetsAsync(new(Limit(context.Request.Query), Value(context.Request.Query, "after"), Value(context.Request.Query, "search")), context.RequestAborted), readers);
        Get("runtime/standalone-trigger-targets/{definitionId}/{slotName}", "StandaloneTriggerTarget", typeof(TriggerTargetView),
            async context => await Operations(context).TargetAsync(Route(context, "definitionId"), Route(context, "slotName"), context.RequestAborted), readers);
        Post("runtime/standalone-triggers/{triggerId}/changes/preview", "StandaloneTriggerChangePreview", typeof(TriggerPreview),
            async (context, body) => await Operations(context).PreviewAsync(Route(context, "triggerId"), body, context.RequestAborted));
        Post("runtime/standalone-triggers/{triggerId}/changes/apply", "StandaloneTriggerChangeApply", typeof(TriggerChangeResult),
            async (context, body) => await Operations(context).ApplyAsync(Route(context, "triggerId"), body, context.RequestAborted));
        Post("runtime/standalone-triggers/{triggerId}/mapping-context", "StandaloneTriggerMappingContext", typeof(JsonElement),
            async (context, body) => await Operations(context).MappingContextAsync(Route(context, "triggerId"), body, context.RequestAborted));
        Post("runtime/standalone-triggers/{triggerId}/mapping-preview", "StandaloneTriggerMappingPreview", typeof(JsonElement),
            async (context, body) => await Operations(context).MappingPreviewAsync(Route(context, "triggerId"), body, context.RequestAborted));
        Get("runtime/standalone-trigger-deliveries", "StandaloneTriggerDeliveries", typeof(TriggerPage<TriggerDeliveryView>),
            async context => await DeliveryOperations(context).ListAsync(Value(context.Request.Query, "triggerId"), Limit(context.Request.Query),
                Value(context.Request.Query, "after"), context.RequestAborted), TriggerPermissions.DeliveriesRead);
        Get("runtime/standalone-trigger-deliveries/{deliveryId}", "StandaloneTriggerDelivery", typeof(TriggerDeliveryView),
            async context => await DeliveryOperations(context).ReadAsync(Route(context, "deliveryId"), context.RequestAborted), TriggerPermissions.DeliveriesRead);
    }

    private static async Task ExecuteAsync(HttpContext context, Func<HttpContext, Task<object>> action)
    {
        try
        {
            var result = await action(context);
            await Results.Json(result, Json, statusCode: context.Response.StatusCode).ExecuteAsync(context);
        }
        catch (TriggerOperationException exception)
        {
            var status = exception.Code switch { "unauthorized" => 401, "forbidden" => 403, "missing" => 404, "conflict" or "route-conflict" => 409, "validation" => 400, _ => 503 };
            await Results.Json(new TriggerProblem(exception.Code, exception.Message, exception.Fields), Json, statusCode: status).ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or KeyNotFoundException)
        { await Results.Json(new TriggerProblem("validation", "The trigger request is invalid."), Json, statusCode: 400).ExecuteAsync(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Owner).LogError(exception, "Standalone trigger request failed.");
            await Results.Json(new TriggerProblem("unexpected", "The trigger request could not be completed."), Json, statusCode: 500).ExecuteAsync(context);
        }
    }
    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) throw new TriggerOperationException("validation", "A JSON request body is required.");
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new TriggerOperationException("validation", "A JSON object is required.");
        return document.RootElement.Clone();
    }
    private static TriggerManagement Operations(HttpContext context) => context.RequestServices.GetRequiredService<TriggerManagement>();
    private static TriggerDeliveryOperations DeliveryOperations(HttpContext context) => context.RequestServices.GetRequiredService<TriggerDeliveryOperations>();
    private static ITriggerRequestAccess Access(HttpContext context) => context.RequestServices.GetRequiredService<ITriggerRequestAccess>();
    private static string Route(HttpContext context, string name) => Convert.ToString(context.Request.RouteValues[name], CultureInfo.InvariantCulture) ?? "";
    private static string? Value(IQueryCollection query, string name) => query.TryGetValue(name, out var values) ? values.ToString() : null;
    private static int Limit(IQueryCollection query) => Value(query, "limit") is not { } value ? 25 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit is >= 1 and <= 100 ? limit :
            throw new TriggerOperationException("validation", "Page size must be between 1 and 100.");
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}

[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(TriggerView))]
[JsonSerializable(typeof(TriggerPage<TriggerView>))]
[JsonSerializable(typeof(TriggerTargetView))]
[JsonSerializable(typeof(TriggerPage<TriggerTargetView>))]
[JsonSerializable(typeof(TriggerSubscriptionView))]
[JsonSerializable(typeof(TriggerPage<TriggerSubscriptionView>))]
[JsonSerializable(typeof(TriggerPage<TriggerTypeView>))]
[JsonSerializable(typeof(TriggerDeliveryView))]
[JsonSerializable(typeof(TriggerPage<TriggerDeliveryView>))]
[JsonSerializable(typeof(TriggerActor))]
[JsonSerializable(typeof(TriggerPreview))]
[JsonSerializable(typeof(TriggerChangeResult))]
[JsonSerializable(typeof(TriggerProblem))]
internal sealed partial class StandaloneTriggersJsonContext : JsonSerializerContext;
