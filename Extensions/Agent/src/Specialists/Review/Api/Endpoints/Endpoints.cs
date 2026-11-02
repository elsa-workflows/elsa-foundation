using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Review.Api.Endpoints;

[Get("/" + HumanReviewApi.Route)]
[RequirePermission(HumanReviewIds.ReadPermission)]
internal sealed class GetReview(IHumanReviews reviews, ReviewRequestCaller caller) : ApiEndpoint<HumanReviewRequest, HumanReviewView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentGetReview";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override async Task<HumanReviewView> HandleAsync(HumanReviewRequest request, CancellationToken cancellationToken) =>
        await reviews.GetAsync(new(request.WorkflowExecutionId, request.ActivityExecutionId), caller.Get(), cancellationToken)
        ?? throw new EntityNotFoundException("No visible review matches.");
}

[Post("/" + HumanReviewApi.Route + "/decision")]
[RequirePermission(HumanReviewIds.DecidePermission)]
internal sealed class DecideReview(IHumanReviews reviews, ReviewRequestCaller caller) : ApiEndpoint<ReviewDecisionRequest, ReviewDecisionReceipt>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentDecideReview";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ReviewDecisionReceipt> HandleAsync(ReviewDecisionRequest request, CancellationToken cancellationToken) =>
        reviews.DecideAsync(new(request.WorkflowExecutionId, request.ActivityExecutionId, request.ContentHash,
            request.Decision, request.Comment, request.RequestId), caller.Get(), cancellationToken).AsTask();
}
