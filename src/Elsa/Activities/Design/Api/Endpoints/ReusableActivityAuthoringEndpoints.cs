using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
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
                if (exception.StatusCode >= StatusCodes.Status500InternalServerError)
                    logger.LogError(exception, "Internal activity authoring command failure for {CommandType}", typeof(TCommand));
                await SendProblemAsync(exception, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity authoring command failure for {CommandType}", typeof(TCommand));
                await WriteProblemAsync(ActivityProblemDetails.Unexpected(HttpContext), 500, cancellationToken);
            }
        }

        private async Task SendProblemAsync(ActivityAuthoringException exception, CancellationToken cancellationToken)
        {
            var problem = ActivityProblemDetails.From(exception, HttpContext);
            await WriteProblemAsync(problem, problem.Status, cancellationToken);
        }

        private async Task WriteProblemAsync(ActivityProblemDetailsView problem, int statusCode, CancellationToken cancellationToken)
        {
            HttpContext.Response.StatusCode = statusCode;
            HttpContext.Response.ContentType = "application/problem+json";
            await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
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
                if (exception.StatusCode >= StatusCodes.Status500InternalServerError)
                    logger.LogError(exception, "Internal activity authoring request failure for {RequestType}", typeof(TRequest));
                var problem = ActivityProblemDetails.From(exception, HttpContext);
                await WriteProblemAsync(problem, problem.Status, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity authoring request failure for {RequestType}", typeof(TRequest));
                await WriteProblemAsync(ActivityProblemDetails.Unexpected(HttpContext), 500, cancellationToken);
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
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
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
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class List(IRequestSender sender, ILogger<List> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityDefinitions, IReadOnlyList<ActivityDefinitionIdentityView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.Definitions);
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
        }
    }

    internal sealed class Get(IRequestSender sender, ILogger<Get> logger)
        : ActivityAuthoringRequestEndpoint<GetReusableActivityDefinition, ReusableActivityDefinitionDetailsView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}"));
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
        }
    }

    internal sealed class Update(ICommandSender sender, ILogger<Update> logger)
        : ActivityAuthoringCommandEndpoint<UpdateReusableActivityDefinition, ReusableActivityDefinitionDetailsView>(sender, logger)
    {
        public override void Configure()
        {
            Patch(RouteConstants.GetRoute("definitions/{definitionId}"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class Recommendation(ICommandSender sender, ILogger<Recommendation> logger)
        : ActivityAuthoringCommandEndpoint<SetRecommendedReusableActivityVersion, ActivityDefinitionRecommendationView>(sender, logger)
    {
        public override void Configure()
        {
            Put(RouteConstants.GetRoute("definitions/{definitionId}/recommendation"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class Picker(IRequestSender sender, ILogger<Picker> logger)
        : ActivityAuthoringRequestEndpoint<ListRecommendedActivityDefinitions, RecommendedActivityDefinitionPageView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/picker"));
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
        }
    }

    internal sealed class ListDrafts(IRequestSender sender, ILogger<ListDrafts> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityDrafts, IReadOnlyList<ReusableActivityDraftSummaryView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}/drafts"));
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
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
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class ListVersions(IRequestSender sender, ILogger<ListVersions> logger)
        : ActivityAuthoringRequestEndpoint<ListReusableActivityVersions, IReadOnlyList<ReusableActivityVersionSummaryView>>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
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
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
        }
    }

    internal sealed class Replace(ICommandSender sender, ILogger<Replace> logger)
        : ActivityAuthoringCommandEndpoint<ReplaceReusableActivityDraft, ReusableActivityDraftView>(sender, logger)
    {
        public override void Configure()
        {
            Put(RouteConstants.GetRoute("drafts/{draftId}"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class Validate(ICommandSender sender, ILogger<Validate> logger)
        : ActivityAuthoringCommandEndpoint<ValidateReusableActivityDraft, ActivityDraftValidationView>(sender, logger)
    {
        public override void Configure()
        {
            Post(RouteConstants.GetRoute("drafts/{draftId}/validate"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class MigrateProvider(ICommandSender sender, ILogger<MigrateProvider> logger)
        : ActivityAuthoringCommandEndpoint<MigrateReusableActivityDraft, ReusableActivityDraftView>(sender, logger)
    {
        protected override int SuccessStatusCode => 201;
        protected override string GetLocation(ReusableActivityDraftView response) =>
            $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}";

        public override void Configure()
        {
            Post(RouteConstants.GetRoute("drafts/{draftId}/migrate-provider"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
        }
    }

    internal sealed class Discard(ICommandSender sender, ILogger<Discard> logger) : ElsaEndpoint<DiscardReusableActivityDraft>
    {
        public override void Configure()
        {
            Delete(RouteConstants.GetRoute("drafts/{draftId}"));
            ConfigurePermissions(PermissionNames.ActivityDesignManage);
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
                if (exception.StatusCode >= StatusCodes.Status500InternalServerError)
                    logger.LogError(exception, "Internal activity draft discard failure");
                var problem = ActivityProblemDetails.From(exception, HttpContext);
                await WriteProblemAsync(problem, problem.Status, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected activity draft discard failure");
                await WriteProblemAsync(ActivityProblemDetails.Unexpected(HttpContext), 500, cancellationToken);
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
            ConfigurePermissions(PermissionNames.ActivityDesignRead);
        }
    }
}
