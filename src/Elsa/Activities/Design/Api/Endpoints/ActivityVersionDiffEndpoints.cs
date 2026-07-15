using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions
{
    internal sealed class Diff(IRequestSender sender, ILogger<Diff> logger)
        : ActivityAuthoringRequestEndpoint<CompareActivityVersions, ActivityVersionDiffView>(sender, logger)
    {
        public override void Configure()
        {
            Get(RouteConstants.GetRoute("versions/{fromVersionId}/diff/{toVersionId}"));
            ConfigurePermissions();
        }
    }
}

namespace Elsa.Activities.Design.Api.Endpoints.Drafts
{
    internal sealed class Diff(IRequestSender sender, ILogger<Diff> logger)
        : ActivityAuthoringRequestEndpoint<PreviewActivityDraftDiff, ActivityVersionDiffView>(sender, logger)
    {
        public override void Configure()
        {
            Post(RouteConstants.GetRoute("drafts/{draftId}/diff"));
            ConfigurePermissions();
        }
    }
}
