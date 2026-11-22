using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class MemberDefaultParityTests
{
    [Theory]
    [InlineData("absent", 2)]
    [InlineData("null", 2)]
    [InlineData("{}", 2)]
    [InlineData("{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", 1)]
    [InlineData("{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":null}}", 1)]
    [InlineData("{\"typeName\":\"Boolean\",\"expression\":{\"type\":\"Literal\",\"value\":\" \"}}", 1)]
    public async Task The_legacy_For_boundary_survives_import_publication_and_execution(string shape, int iterations)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var loop = Activity("loop", "Elsa.For");
        loop["start"] = Literal("0"); loop["end"] = Literal("1"); loop["step"] = Literal("1");
        loop["body"] = Write("iteration");
        if (shape != "absent") loop["outerBoundInclusive"] = JsonNode.Parse(shape);
        var workflow = await ImportPublish(host, Definition(Sequence(loop)));
        var run = await host.RunAsync(workflow);
        Completed(run);
        Assert.Equal(iterations, run.ExecutedNodeIds.Count(id => id == "iteration"));
    }

    [Fact]
    public async Task Memory_only_input_is_refused_before_import_writes()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var loop = Activity("loop", "Elsa.For");
        loop["outerBoundInclusive"] = new JsonObject { ["typeName"] = "Boolean", ["memoryReference"] = new JsonObject { ["id"] = "inclusive" } };
        var definition = Definition(Sequence(loop));
        definition["variables"] = new JsonArray(new JsonObject { ["id"] = "inclusive", ["name"] = "inclusive", ["typeName"] = "Boolean", ["value"] = "false" });
        await RefusedWithoutWrites(host, definition, "outerBoundInclusive");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_resolved_expression_wins_over_serialized_memory_metadata(bool variableExpression)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var loop = Activity("loop", "Elsa.For");
        loop["start"] = Literal("0"); loop["end"] = Literal("1"); loop["step"] = Literal("1"); loop["body"] = Write("iteration");
        loop["outerBoundInclusive"] = new JsonObject
        {
            ["typeName"] = "Boolean",
            ["expression"] = variableExpression
                ? new JsonObject { ["type"] = "Variable", ["value"] = new JsonObject { ["id"] = "selected", ["name"] = "selected" } }
                : new JsonObject { ["type"] = "Literal", ["value"] = false },
            ["memoryReference"] = new JsonObject { ["id"] = "ignored" }
        };
        var definition = Definition(Sequence(loop));
        definition["variables"] = new JsonArray(
            new JsonObject { ["id"] = "selected", ["name"] = "selected", ["typeName"] = "Boolean", ["value"] = "false" },
            new JsonObject { ["id"] = "ignored", ["name"] = "ignored", ["typeName"] = "Boolean", ["value"] = "true" });
        var run = await host.RunAsync(await ImportPublish(host, definition));
        Completed(run);
        Assert.Single(run.ExecutedNodeIds, id => id == "iteration");
    }

    [Fact]
    public async Task A_deliberate_null_Delay_schedules_zero_in_the_real_runtime()
    {
        var timers = new ConcurrentQueue<DurableTimer>();
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true, configureServices: services =>
            services.AddScoped<IDurableTimerScheduler>(provider => new ObservedScheduler(
                ActivatorUtilities.CreateInstance<DurableTimerScheduler>(provider), timers)));
        var delay = Activity("delay", "Elsa.Delay");
        delay["timeSpan"] = new JsonObject { ["typeName"] = "TimeSpan", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = null } };
        var workflow = await ImportPublish(host, Definition(Sequence(delay, Write("after"))));
        var run = await host.RunAsync(workflow);
        Completed(run);
        var timer = Assert.Single(timers);
        Assert.Equal(timer.CreatedAt, timer.DueTime);
        Assert.Contains("after", run.ExecutedNodeIds);
    }

    [Theory]
    [InlineData("Elsa.SendHttpRequest")]
    [InlineData("Elsa.FlowSendHttpRequest")]
    public async Task Blank_content_type_refuses_the_legacy_header_mismatch_before_any_request(string type)
    {
        await using var receiver = await Receiver.Start();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var call = Activity("call", type);
        call["url"] = Literal(receiver.Url); call["content"] = Literal("hello"); call["contentType"] = Literal(" ");
        await RefusedWithoutWrites(host, Definition(Sequence(call)), "contentType");
        Assert.Empty(receiver.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Computed_or_referenced_empty_String_content_is_refused_before_writing(bool reference)
    {
        await using var receiver = await Receiver.Start();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var call = Activity("call", "Elsa.FlowSendHttpRequest");
        call["url"] = Literal(receiver.Url); call["contentType"] = Literal("application/json");
        call["content"] = reference
            ? new JsonObject { ["typeName"] = "String", ["memoryReference"] = new JsonObject { ["id"] = "body" } }
            : new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = "return '';" } };
        var definition = Definition(Sequence(call));
        if (reference) definition["variables"] = new JsonArray(new JsonObject { ["id"] = "body", ["name"] = "body", ["typeName"] = "String", ["value"] = "" });
        await RefusedWithoutWrites(host, definition, "content");
        Assert.Empty(receiver.Requests);
    }

    [Theory]
    [InlineData("Elsa.SendHttpRequest", "absent")]
    [InlineData("Elsa.SendHttpRequest", "null")]
    [InlineData("Elsa.FlowSendHttpRequest", "{}")]
    [InlineData("Elsa.FlowSendHttpRequest", "{\"typeName\":\"String\",\"expression\":{\"type\":\"Literal\",\"value\":null}}")]
    [InlineData("Elsa.SendHttpRequest", "{\"typeName\":\"String\",\"expression\":{\"type\":\"Literal\",\"value\":\" \"}}")]
    [InlineData("Elsa.FlowSendHttpRequest", "{\"typeName\":\"String\",\"expression\":{\"type\":\"Literal\",\"value\":\" \"}}")]
    public async Task Known_null_content_keeps_the_no_body_path(string type, string contentType)
    {
        await using var receiver = await Receiver.Start();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var call = Activity("call", type); call["url"] = Literal(receiver.Url);
        call["content"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = null } };
        if (contentType != "absent") call["contentType"] = JsonNode.Parse(contentType);
        Completed(await host.RunAsync(await ImportPublish(host, Definition(Sequence(call)))));
        var request = Assert.Single(receiver.Requests);
        Assert.Equal("GET", request.Method); Assert.Equal("", request.Body); Assert.Null(request.ContentType);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Default_status_list_routes_Done_and_numeric_or_unmatched_for_200_404_and_500(string shape)
    {
        await using var receiver = await Receiver.Start();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var definition = JsonNode.Parse(Elsa3ImportHost.Collection("http-expected-codes-done"))!.AsArray()[0]!.DeepClone();
        var chart = definition["root"]!;
        var call = chart["activities"]![0]!.AsObject();
        call["url"] = Literal(receiver.Url);
        call.Remove("expectedStatusCodes");
        if (shape != "absent") call["expectedStatusCodes"] = JsonNode.Parse(shape);
        chart["activities"]!.AsArray().Add(Write("matched"));
        chart["activities"]!.AsArray().Add(Write("unmatched"));
        chart["connections"]!.AsArray().Add(Connection("call", "200", "matched"));
        chart["connections"]!.AsArray().Add(Connection("call", "Unmatched status code", "unmatched"));
        var workflow = await ImportPublish(host, definition);
        foreach (var status in new[] { 200, 404, 500 })
        {
            receiver.Status = status;
            var run = await host.RunAsync(workflow);
            Completed(run);
            Assert.Contains("after", run.ExecutedNodeIds);
            Assert.Contains(status == 200 ? "matched" : "unmatched", run.ExecutedNodeIds);
            Assert.DoesNotContain(status == 200 ? "unmatched" : "matched", run.ExecutedNodeIds);
            Assert.Equal([status == 200 ? "200" : "Unmatched status code"], Assert.Single(run.Activities, s => s.Execution.ExecutableNodeId == "call").Completion?.OutcomeNames);
        }
        Assert.Equal(3, receiver.Requests.Count);
    }

    [Theory]
    [InlineData("contentType", "absent", "hello")]
    [InlineData("contentType", "absent", "")]
    [InlineData("contentType", "null", "")]
    [InlineData("contentType", "{}", "hello")]
    [InlineData("content", "{\"typeName\":\"String\",\"expression\":{\"type\":\"Literal\",\"value\":\"\"}}", "")]
    [InlineData("expectedStatusCodes", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":null}}", null)]
    [InlineData("expectedStatusCodes", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", null)]
    [InlineData("expectedStatusCodes", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":[]}}", null)]
    [InlineData("expectedStatusCodes", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":\"[]\"}}", null)]
    [InlineData("expectedStatusCodes", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":\" \"}}", null)]
    public async Task Refused_requests_write_no_design_or_publication_records_and_make_no_request(string member, string shape, string? content)
    {
        await using var receiver = await Receiver.Start();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var call = Activity("call", "Elsa.FlowSendHttpRequest");
        call["url"] = Literal(receiver.Url);
        if (content is not null) call["content"] = Literal(content);
        if (member == "content") call["contentType"] = Literal(" ");
        if (shape != "absent") call[member] = JsonNode.Parse(shape);
        await RefusedWithoutWrites(host, Definition(Sequence(call)), member);
        Assert.Empty(receiver.Requests);
    }

    [Theory]
    [InlineData("Elsa.WriteHttpResponse", "statusCode", "0")]
    [InlineData("Elsa.WriteHttpResponse", "statusCode", "-1")]
    [InlineData("Elsa.Timer", "interval", "00:00:00")]
    [InlineData("Elsa.Timer", "interval", "-00:00:01")]
    public async Task Invalid_status_or_recurrence_is_refused_before_any_write(string type, string member, string value)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true, withScheduling: true);
        var node = Activity("invalid", type); node[member] = Literal(value);
        if (type == "Elsa.Timer") node["customProperties"] = new JsonObject { ["canStartWorkflow"] = true };
        await RefusedWithoutWrites(host, Definition(Sequence(node)), member);
    }

    private static async Task RefusedWithoutWrites(Elsa3ImportHost host, JsonNode definition, string member)
    {
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(definition).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var before = await host.CountDesignRowsAsync();
        var apply = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);
        Assert.Contains(apply.Body["diagnostics"]!.AsArray(), d => ((string?)d?["path"])?.EndsWith("." + member, StringComparison.Ordinal) == true);
        Assert.Equal(before.OrderBy(p => p.Key), (await host.CountDesignRowsAsync()).OrderBy(p => p.Key));
        Assert.Empty(await host.PublicationRecordsAsync((string)definition["definitionId"]!));
        Assert.Null(await host.ActivePublicationAsync((string)definition["definitionId"]!));
    }

    private static async Task<PublishedWorkflow> ImportPublish(Elsa3ImportHost host, JsonNode definition)
    {
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(definition).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var item = Assert.Single(items);
        var apply = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(item, "sourceVersionId"));
        Assert.True(apply.StatusCode == HttpStatusCode.Created, apply.Body.ToJsonString());
        return await host.PublishWorkflowAsync(Elsa3ImportHost.Required(item, "workflowVersionId"));
    }

    private static JsonNode Definition(JsonNode root)
    {
        var definition = JsonNode.Parse(Elsa3ImportHost.Collection("http-expected-codes-done"))!.AsArray()[0]!.DeepClone();
        definition["root"] = root;
        return definition;
    }

    private static JsonObject Literal(string text) => new() { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = text } };
    private static JsonObject Activity(string id, string type) => new() { ["id"] = id, ["nodeId"] = id, ["type"] = type, ["version"] = 1 };
    private static JsonObject Write(string id) { var node = Activity(id, "Elsa.WriteLine"); node["text"] = Literal(id); return node; }
    private static JsonObject Sequence(params JsonNode[] children) { var node = Activity("root", "Elsa.Sequence"); node["activities"] = new JsonArray(children); return node; }
    private static JsonObject Connection(string source, string port, string target) => new()
    {
        ["source"] = new JsonObject { ["activity"] = source, ["port"] = port },
        ["target"] = new JsonObject { ["activity"] = target, ["port"] = "In" }
    };
    private static void Completed(RunState run) { Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe()); Assert.Empty(run.Incidents); Assert.All(run.Activities, a => Assert.Null(a.Fault)); }

    private sealed class ObservedScheduler(IDurableTimerScheduler inner, ConcurrentQueue<DurableTimer> timers) : IDurableTimerScheduler
    {
        public ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
        { timers.Enqueue(timer); return inner.ScheduleAsync(timer, cancellationToken); }
    }

    private sealed record Request(string Method, string? ContentType, string Body);
    private sealed class Receiver(WebApplication app, string url) : IAsyncDisposable
    {
        public string Url { get; } = url;
        public int Status { get; set; } = 200;
        public ConcurrentQueue<Request> Requests { get; } = new();
        public static async Task<Receiver> Start()
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build(); Receiver? receiver = null;
            app.Run(async context =>
            {
                using var body = new StreamReader(context.Request.Body);
                receiver!.Requests.Enqueue(new(context.Request.Method, context.Request.ContentType, await body.ReadToEndAsync()));
                context.Response.StatusCode = receiver.Status;
            });
            await app.StartAsync();
            receiver = new(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            return receiver;
        }
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
