using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.CredentialLiteralTestSupport;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class SubmitDraftValidationTests
{
    [Fact]
    public async Task The_mapped_submit_route_refuses_an_unknown_activity_with_promotion_errors_and_no_persisted_rows()
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync(configure: services =>
        {
            services.AddLogging();
            services.AddRouting().AddElsaEndpoints();
            new WorkflowsDesignApiFeature().ConfigureServices(services);
        });
        var response = await host.CallAsync<SubmitRoute, RouteResponse>(route => route.PostAsync());

        Assert.Equal(StatusCodes.Status409Conflict, response.Status);
        using var problem = JsonDocument.Parse(response.Body);
        Assert.Equal(StatusCodes.Status409Conflict, problem.RootElement.GetProperty("statusCode").GetInt32());
        Assert.True(problem.RootElement.GetProperty("errors").GetProperty(NodeId).GetArrayLength() > 0);
        await AssertEmptyAsync(host);
    }

    [Fact]
    public async Task Submit_aggregates_all_blocking_findings_and_a_refused_operation_can_be_corrected_and_retried()
    {
        var validator = new SubmissionValidator
        {
            Findings = [new(NodeId, "Test/First", "First refusal"), new(NodeId + "/inputs/label", "Test/Second", "Second refusal")]
        };
        await using var host = await DesignAdmissionTestHost.CreateAsync(configure: services =>
            services.AddSingleton<IDraftValidator>(validator));

        var refusal = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => SubmitAsync(host));
        Assert.Equal(["Test/First", "Test/Second"], refusal.Errors.Select(error => error.Type));
        await AssertEmptyAsync(host);

        validator.Findings = [];
        var accepted = await SubmitAsync(host);
        Assert.Equal((1, 1, 1), await host.CountRowsAsync());
        Assert.False(string.IsNullOrWhiteSpace(accepted.VersionId));
    }

    [Fact]
    public async Task Warning_only_submission_succeeds_and_committed_replay_does_not_rerun_validators()
    {
        var validator = new SubmissionValidator
        {
            Findings = [new(NodeId, "Test/Warning", "Advisory") { Severity = ValidationSeverities.Warning }]
        };
        await using var host = await DesignAdmissionTestHost.CreateAsync(configure: services =>
            services.AddSingleton<IDraftValidator>(validator));

        var accepted = await SubmitAsync(host);
        Assert.Equal(1, validator.Calls);
        validator.Findings = [new(NodeId, "Test/Changed", "New rule after commit")];
        var replay = await SubmitAsync(host);

        Assert.Equal(accepted, replay);
        Assert.Equal(1, validator.Calls);
        Assert.Equal((1, 1, 1), await host.CountRowsAsync());
    }

    [Fact]
    public async Task A_throwing_validator_leaves_no_draft_version_layout_or_operation_marker()
    {
        var validator = new SubmissionValidator { Failure = new InvalidOperationException("submission-validator-fault") };
        await using var host = await DesignAdmissionTestHost.CreateAsync(configure: services =>
            services.AddSingleton<IDraftValidator>(validator));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => SubmitAsync(host));

        Assert.Contains("submission-validator-fault", failure.ToString(), StringComparison.Ordinal);
        await AssertEmptyAsync(host);
    }

    private static Task<SubmittedWorkflowDefinition> SubmitAsync(DesignAdmissionTestHost host) => host.InScopeAsync(services =>
        services.GetRequiredService<ISubmitWorkflowDefinitionCommand>().Execute(
            new DesignOperationKey("validated-submit"), "Validated submission", null, State()));

    private static async Task AssertEmptyAsync(DesignAdmissionTestHost host)
    {
        Assert.Equal((0, 0, 0), await host.CountRowsAsync());
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<WorkflowsDesignDbContext>();
            Assert.Equal(0, await db.DraftLayouts.CountAsync());
            Assert.Equal(0, await db.VersionLayouts.CountAsync());
            Assert.Equal(0, await db.Operations.CountAsync());
            return true;
        });
    }

    private sealed class SubmissionValidator : IDraftValidator
    {
        public ValidationError[] Findings { get; set; } = [];
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }

        public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return ValueTask.FromResult<IEnumerable<ValidationError>>(Findings);
        }
    }

    private sealed record RouteResponse(int Status, string Body);

    private sealed class SubmitRoute(IHttpContextAccessor accessor)
    {
        public async Task<RouteResponse> PostAsync()
        {
            var context = accessor.HttpContext!;
            var routes = new TestEndpointRouteBuilder(context.RequestServices);
            WorkflowsDesignApi.MapWorkflowsDesignApi(routes);
            var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Single(candidate => candidate.RoutePattern.RawText == "design/workflows/definitions/submit");
            context.SetEndpoint(endpoint);
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/design/workflows/definitions/submit";
            context.Request.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes("""
                {"operationKey":"invalid-http-submit","name":"Invalid submission","state":{
                  "rootActivity":{"nodeId":"send","activityVersionId":"av-not-installed","inputs":[],"outputs":[]}}}
                """);
            context.Request.Body = new MemoryStream(body);
            context.Request.ContentLength = body.Length;
            using var output = new MemoryStream();
            context.Response.Body = output;
            await endpoint.RequestDelegate!(context);
            return new RouteResponse(context.Response.StatusCode, Encoding.UTF8.GetString(output.ToArray()));
        }
    }
}
