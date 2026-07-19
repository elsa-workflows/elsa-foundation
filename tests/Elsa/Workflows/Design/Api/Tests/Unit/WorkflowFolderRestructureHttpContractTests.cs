using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Exercises the public folder-restructure HTTP contract through the real FastEndpoints handlers.</summary>
public sealed class WorkflowFolderRestructureHttpContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Folder_restructure_endpoints_have_exact_routes_and_manage_authorization()
    {
        AssertEndpoint("Rename", "POST", "design/workflows/folders/{folderId}/rename");
        AssertEndpoint("Move", "POST", "design/workflows/folders/{folderId}/move");
        AssertEndpoint("Delete", "DELETE", "design/workflows/folders/{folderId}");
    }

    [Fact]
    public void Move_body_requires_parent_id_and_uses_explicit_null_for_root()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MoveWorkflowFolder>("{\"folderId\":\"folder\"}", JsonOptions));

        var root = JsonSerializer.Deserialize<MoveWorkflowFolder>("{\"folderId\":\"folder\",\"parentId\":null}", JsonOptions);

        Assert.NotNull(root);
        Assert.Equal("folder", root!.FolderId);
        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task Folder_restructure_endpoints_return_the_contract_success_statuses()
    {
        var view = new WorkflowFolderView("folder", null, "Folder", "FOLDER", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var rename = CreateEndpoint("Rename", new CommandSender(view));
        var move = CreateEndpoint("Move", new CommandSender(view));
        var delete = CreateEndpoint("Delete", new CommandSender());

        await InvokeAsync(rename, new RenameWorkflowFolder("folder", "Folder"));
        await InvokeAsync(move, new MoveWorkflowFolder("folder", null));
        await InvokeAsync(delete, new DeleteWorkflowFolder("folder"));

        Assert.Equal(StatusCodes.Status200OK, rename.HttpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, move.HttpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status204NoContent, delete.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Folder_restructure_http_routes_enforce_manage_auth_bind_explicit_null_and_map_problem_statuses()
    {
        await using var host = await FolderApiHost.StartAsync();
        using var unauthenticated = await host.Client.PostAsJsonAsync("/design/workflows/folders/folder/move", new { parentId = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        host.Authorize(PermissionNames.WorkflowDesignRead);
        using var forbidden = await host.Client.PostAsJsonAsync("/design/workflows/folders/folder/move", new { parentId = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        host.Authorize(PermissionNames.WorkflowDesignManage);
        using var omitted = await host.Client.PostAsync("/design/workflows/folders/folder/move", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);

        using var root = await host.Client.PostAsJsonAsync("/design/workflows/folders/folder/move", new { parentId = (string?)null });
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Contains("\"parentId\":null", await root.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        host.Scenario.Exception = new WorkflowFolderRestructureConflictException();
        using var conflict = await host.Client.PostAsJsonAsync("/design/workflows/folders/folder/rename", new { name = "Folder" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var metadataConflict = await host.Client.PatchAsJsonAsync("/design/workflows/definitions/definition", new { name = "Definition" });
        using var lifecycleConflict = await host.Client.DeleteAsync("/design/workflows/definitions/definition");
        Assert.Equal(HttpStatusCode.Conflict, metadataConflict.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, lifecycleConflict.StatusCode);

        host.Scenario.Exception = EntityNotFoundException.ForEntity(typeof(WorkflowFolder), "foreign-id");
        using var hidden = await host.Client.DeleteAsync("/design/workflows/folders/foreign-id");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("tenant-b", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        host.Scenario.Exception = new ArgumentException("Bad folder request.");
        using var invalid = await host.Client.PostAsJsonAsync("/design/workflows/folders/folder/move", new { parentId = "bad" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static void AssertEndpoint(string endpointName, string verb, string route)
    {
        var endpoint = CreateEndpoint(endpointName, new CommandSender());
        endpoint.Configure();

        Assert.Equal(verb, Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal(route, Assert.Single(endpoint.Definition.Routes));
        Assert.Contains(PermissionNames.WorkflowDesignManage, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    private static BaseEndpoint CreateEndpoint(string endpointName, ICommandSender sender)
    {
        var type = typeof(MoveWorkflowFolder).Assembly.GetType(
            $"Elsa.Workflows.Design.Api.Endpoints.Folders.{endpointName}", throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(type);
        var logger = loggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
                     ?? loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var second] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(type);
        Action<DefaultHttpContext> configure = context => context.Response.Body = new MemoryStream();
        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { sender, logger }])!;
    }

    private static Task InvokeAsync<TRequest>(BaseEndpoint endpoint, TRequest request) =>
        (Task)endpoint.GetType().GetMethod("HandleAsync", [typeof(TRequest), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private static async Task<string> ResponseBodyAsync(BaseEndpoint endpoint)
    {
        endpoint.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(endpoint.HttpContext.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class CommandSender(object? value = null) : ICommandSender
    {
        public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
            value is Exception exception ? Task.FromException<T>(exception) : Task.FromResult((T)value!);

        public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) =>
            value is Exception exception ? Task.FromException(exception) : Task.CompletedTask;
    }

    private sealed class FolderApiHost(WebApplication app, HttpClient client, FolderScenario scenario) : IAsyncDisposable
    {
        private const string PermissionHeader = "X-Test-Permission";
        public HttpClient Client { get; } = client;
        public FolderScenario Scenario { get; } = scenario;

        public static async Task<FolderApiHost> StartAsync()
        {
            var scenario = new FolderScenario();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            new WorkflowsDesignApiFeature().ConfigureServices(builder.Services);
            builder.Services.AddSingleton(scenario);
            builder.Services.AddScoped<ICommandSender, HostCommandSender>();
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(WorkflowsDesignApiFeature).Assembly];
                options.Filter = type => type.FullName is
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.Rename" or
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.Move" or
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.Delete" or
                    "Elsa.Workflows.Design.Api.Endpoints.Definitions.Update" or
                    "Elsa.Workflows.Design.Api.Endpoints.Definitions.Delete";
            });
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseFastEndpoints(options => options.Security.PermissionsClaimType = "permissions");
            await app.StartAsync();
            return new FolderApiHost(app, app.GetTestClient(), scenario);
        }

        public void Authorize(string permission)
        {
            Client.DefaultRequestHeaders.Remove(PermissionHeader);
            Client.DefaultRequestHeaders.Add(PermissionHeader, permission);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class FolderScenario
    {
        public Exception? Exception { get; set; }
        public WorkflowFolder Folder { get; } = new()
        {
            Id = "folder", Name = "Folder", NormalizedName = "FOLDER", CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class HostCommandSender(FolderScenario scenario) : ICommandSender
    {
        public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
        {
            if (scenario.Exception is not null)
                return Task.FromException<T>(scenario.Exception);
            if (command is MoveWorkflowFolder move)
                scenario.Folder.ParentFolderId = move.ParentId;
            var view = new WorkflowFolderView(
                scenario.Folder.Id,
                scenario.Folder.ParentFolderId,
                scenario.Folder.Name,
                scenario.Folder.NormalizedName,
                scenario.Folder.CreatedAt,
                scenario.Folder.LastModifiedAt);
            return Task.FromResult((T)(object)view);
        }

        public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) =>
            scenario.Exception is null ? Task.CompletedTask : Task.FromException(scenario.Exception);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FolderApiTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Permission", out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "operator-1"), new Claim("permissions", permission.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
