using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints
{
    internal abstract class ActivityAuthoringCommandEndpoint<TCommand, TResponse>(
        ICommandSender sender,
        ILogger logger) : ElsaEndpoint<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
        where TResponse : notnull
    {
        protected virtual int SuccessStatusCode => 200;

        protected virtual string? GetLocation(TResponse response) => null;

        public override async Task HandleAsync(TCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await sender.Send(request, cancellationToken);
                var location = GetLocation(response);
                if (location is not null)
                    HttpContext.Response.Headers.Location = location;
                await Send.ResponseAsync(response, SuccessStatusCode, cancellationToken);
            }
            catch (ActivityAuthoringException exception)
            {
                await SendProblemAsync(exception, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity authoring command failure for {CommandType}", typeof(TCommand));
                await SendProblemAsync(
                    new(500, "activity.operation.failed", "Activity authoring operation failed", "The activity authoring operation failed."),
                    cancellationToken);
            }
        }

        private async Task SendProblemAsync(ActivityAuthoringException exception, CancellationToken cancellationToken)
        {
            HttpContext.Response.StatusCode = exception.StatusCode;
            HttpContext.Response.ContentType = "application/problem+json";
            await HttpContext.Response.WriteAsJsonAsync(ToProblem(exception), cancellationToken);
        }

        private ActivityProblemDetailsView ToProblem(ActivityAuthoringException exception) => new(
            $"https://elsa.dev/problems/{exception.ErrorCode.Replace('.', '-')}",
            exception.Title,
            exception.StatusCode,
            exception.Message,
            HttpContext.Request.Path,
            exception.ErrorCode,
            HttpContext.TraceIdentifier,
            exception.Diagnostics);
    }

    internal abstract class ActivityAuthoringRequestEndpoint<TRequest, TResponse>(
        IRequestSender sender,
        ILogger logger) : ElsaEndpoint<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        public override async Task HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await Send.OkAsync(await sender.Send(request, cancellationToken), cancellationToken);
            }
            catch (ActivityAuthoringException exception)
            {
                await WriteProblemAsync(new ActivityProblemDetailsView(
                    $"https://elsa.dev/problems/{exception.ErrorCode.Replace('.', '-')}",
                    exception.Title,
                    exception.StatusCode,
                    exception.Message,
                    HttpContext.Request.Path,
                    exception.ErrorCode,
                    HttpContext.TraceIdentifier,
                    exception.Diagnostics), exception.StatusCode, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity authoring request failure for {RequestType}", typeof(TRequest));
                await WriteProblemAsync(new ActivityProblemDetailsView(
                    "https://elsa.dev/problems/activity-operation-failed",
                    "Activity authoring operation failed",
                    500,
                    "The activity authoring operation failed.",
                    HttpContext.Request.Path,
                    "activity.operation.failed",
                    HttpContext.TraceIdentifier,
                    []), 500, cancellationToken);
            }
        }

        private async Task WriteProblemAsync(ActivityProblemDetailsView problem, int statusCode, CancellationToken cancellationToken)
        {
            HttpContext.Response.StatusCode = statusCode;
            HttpContext.Response.ContentType = "application/problem+json";
            await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
    }
}

namespace Elsa.Activities.Design.Api.Endpoints.Definitions
{
    internal sealed class Add(ICommandSender sender, ILogger<Add> logger)
        : ActivityAuthoringCommandEndpoint<CreateReusableActivityDefinition, ReusableActivityDefinitionDetailsView>(sender, logger)
    {
        protected override int SuccessStatusCode => 201;
        protected override string GetLocation(ReusableActivityDefinitionDetailsView response) =>
            $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}";

        public override void Configure()
        {
            Post(RouteConstants.Definitions);
            ConfigurePermissions();
        }
    }

    internal sealed class Fork(ICommandSender sender, ILogger<Fork> logger)
        : ActivityAuthoringCommandEndpoint<ForkReusableActivityDefinition, ReusableActivityDefinitionDetailsView>(sender, logger)
    {
        protected override int SuccessStatusCode => 201;
        protected override string GetLocation(ReusableActivityDefinitionDetailsView response) =>
            $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}";

        public override void Configure()
        {
            Post(RouteConstants.GetRoute("definitions/{definitionId}/forks"));
            ConfigurePermissions();
        }
    }

    internal sealed class List(IRequestSender sender, ILogger<List> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityDefinitions, IReadOnlyList<ActivityDefinitionIdentityView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.Definitions);
            ConfigurePermissions();
        }
    }

    internal sealed class Get(IRequestSender sender, ILogger<Get> logger)
        : ActivityAuthoringRequestEndpoint<GetReusableActivityDefinition, ReusableActivityDefinitionDetailsView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}"));
            ConfigurePermissions();
        }
    }

    internal sealed class ListDrafts(IRequestSender sender, ILogger<ListDrafts> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityDrafts, IReadOnlyList<ReusableActivityDraftSummaryView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}/drafts"));
            ConfigurePermissions();
        }
    }

    internal sealed class AddDraft(ICommandSender sender, ILogger<AddDraft> logger)
        : ActivityAuthoringCommandEndpoint<CreateReusableActivityDraft, ReusableActivityDraftView>(sender, logger)
    {
        protected override int SuccessStatusCode => 201;
        protected override string GetLocation(ReusableActivityDraftView response) =>
            $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}";

        public override void Configure()
        {
            Post(RouteConstants.GetRoute("definitions/{definitionId}/drafts"));
            ConfigurePermissions();
        }
    }

    internal sealed class ListVersions(IRequestSender sender, ILogger<ListVersions> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityVersions, IReadOnlyList<ReusableActivityVersionSummaryView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
            ConfigurePermissions();
        }
    }
}

namespace Elsa.Activities.Design.Api.Endpoints.Drafts
{
    internal sealed class Get(IRequestSender sender, ILogger<Get> logger)
        : ActivityAuthoringRequestEndpoint<GetReusableActivityDraft, ReusableActivityDraftView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("drafts/{draftId}"));
            ConfigurePermissions();
        }
    }

    internal sealed class Replace(ICommandSender sender, ILogger<Replace> logger)
        : ActivityAuthoringCommandEndpoint<ReplaceReusableActivityDraft, ReusableActivityDraftView>(sender, logger)
    {
        public override void Configure()
        {
            Put(RouteConstants.GetRoute("drafts/{draftId}"));
            ConfigurePermissions();
        }
    }

    internal sealed class Validate(ICommandSender sender, ILogger<Validate> logger)
        : ActivityAuthoringCommandEndpoint<ValidateReusableActivityDraft, ActivityDraftValidationView>(sender, logger)
    {
        public override void Configure()
        {
            Post(RouteConstants.GetRoute("drafts/{draftId}/validate"));
            ConfigurePermissions();
        }
    }

    internal sealed class Discard(ICommandSender sender, ILogger<Discard> logger) : ElsaEndpoint<DiscardReusableActivityDraft>
    {
        public override void Configure()
        {
            Delete(RouteConstants.GetRoute("drafts/{draftId}"));
            ConfigurePermissions();
        }

        public override async Task HandleAsync(DiscardReusableActivityDraft request, CancellationToken cancellationToken)
        {
            try
            {
                await sender.Send(request, cancellationToken);
                await Send.NoContentAsync(cancellationToken);
            }
            catch (ActivityAuthoringException exception)
            {
                await WriteProblemAsync(new ActivityProblemDetailsView(
                    $"https://elsa.dev/problems/{exception.ErrorCode.Replace('.', '-')}",
                    exception.Title,
                    exception.StatusCode,
                    exception.Message,
                    HttpContext.Request.Path,
                    exception.ErrorCode,
                    HttpContext.TraceIdentifier,
                    exception.Diagnostics), exception.StatusCode, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity draft discard failure");
                await WriteProblemAsync(new ActivityProblemDetailsView(
                    "https://elsa.dev/problems/activity-operation-failed",
                    "Activity authoring operation failed",
                    500,
                    "The activity authoring operation failed.",
                    HttpContext.Request.Path,
                    "activity.operation.failed",
                    HttpContext.TraceIdentifier,
                    []), 500, cancellationToken);
            }
        }

        private async Task WriteProblemAsync(ActivityProblemDetailsView problem, int statusCode, CancellationToken cancellationToken)
        {
            HttpContext.Response.StatusCode = statusCode;
            HttpContext.Response.ContentType = "application/problem+json";
            await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
    }
}

namespace Elsa.Activities.Design.Api.Endpoints.Versions
{
    internal sealed class Get(IRequestSender sender, ILogger<Get> logger)
        : ActivityAuthoringRequestEndpoint<GetReusableActivityVersion, ReusableActivityVersionView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("versions/{versionId}"));
            ConfigurePermissions();
        }
    }
}
