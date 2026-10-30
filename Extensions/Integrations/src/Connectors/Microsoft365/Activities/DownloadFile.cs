using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "download")]
[Version("1.0.0")]
public sealed class DownloadFile(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<GraphFileDownload>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(DriveId), DisplayName = "Drive id", Tier = InputTier.Optional, Description = "OneDrive or SharePoint drive id. Default the signed-in user's drive.")]
    public string? DriveId { get; set; }

    [ActivityInput(Key = nameof(ItemId), DisplayName = "Item id", Tier = InputTier.Optional, Description = "Drive item id. Use this or Item path. Requires Files.Read.")]
    public string? ItemId { get; set; }

    [ActivityInput(Key = nameof(ItemPath), DisplayName = "Item path", Tier = InputTier.Optional, Description = "Path under the drive root, for example /Reports/note.txt.")]
    public string? ItemPath { get; set; }

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN when Drive id is empty. Default /me/drive.")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<GraphFileDownload>> ExecuteAsync(ActivityExecutionContext context)
    {
        string relative;
        try
        {
            if (!string.IsNullOrWhiteSpace(ItemId))
                relative = $"{Microsoft365Paths.DriveItemById(DriveId, UserId, ItemId)}/content";
            else if (!string.IsNullOrWhiteSpace(ItemPath))
                relative = $"{Microsoft365Paths.DriveItemByPath(DriveId, UserId, ItemPath)}/content";
            else
            {
                return ActivityTransition.Fault<GraphFileDownload>(
                    new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Download File requires Item id or Item path."));
            }

            var (content, contentType, fileName) = await CreateClient()
                .GetContentAsync(
                    relative,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.FilesRead)
                .ConfigureAwait(false);

            return ActivityTransition.Complete(new GraphFileDownload
            {
                FileName = fileName ?? Microsoft365Paths.FileNameFromPath(ItemPath) ?? ItemId,
                ContentType = contentType,
                Content = content
            });
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
