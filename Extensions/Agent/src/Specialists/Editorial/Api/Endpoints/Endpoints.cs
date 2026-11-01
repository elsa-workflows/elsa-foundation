using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Editorial.Api.Endpoints;

[Get("/" + EditorialReviewApi.Route)]
[RequirePermission(EditorialReviewIds.ReadPermission)]
internal sealed class GetReview(IEditorialReviews reviews, EditorialRequestCaller caller) : ApiEndpoint<EditorialReviewRequest, EditorialReviewView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EditorialGetReview";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override async Task<EditorialReviewView> HandleAsync(EditorialReviewRequest request, CancellationToken cancellationToken) =>
        await reviews.GetAsync(new(request.WorkflowExecutionId, request.ActivityExecutionId), caller.Get(), cancellationToken)
        ?? throw new EntityNotFoundException("No visible editorial review matches.");
}

[Post("/" + EditorialReviewApi.Route + "/decision")]
[RequirePermission(EditorialReviewIds.DecidePermission)]
internal sealed class DecideReview(IEditorialReviews reviews, EditorialRequestCaller caller) : ApiEndpoint<EditorialDecisionRequest, EditorialDecisionReceipt>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EditorialDecideReview";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EditorialDecisionReceipt> HandleAsync(EditorialDecisionRequest request, CancellationToken cancellationToken) =>
        reviews.DecideAsync(new(request.WorkflowExecutionId, request.ActivityExecutionId, request.Round, request.ArticleHash,
            request.Decision, request.Comment, request.RequestId), caller.Get(), cancellationToken).AsTask();
}
