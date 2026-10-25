using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "upload")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Content), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class UploadFile(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<GraphDriveItem>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(DriveId), DisplayName = "Drive id", Tier = InputTier.Optional, Description = "OneDrive or SharePoint drive id. Default the signed-in user's drive.")]
    public string? DriveId { get; set; }

    [ActivityInput(Key = nameof(ItemPath), DisplayName = "Item path", Tier = InputTier.Optional, Description = "Path under the drive root, for example /Reports/note.txt.")]
    public string? ItemPath { get; set; }

    [ActivityInput(Key = nameof(FileName), DisplayName = "File name", Tier = InputTier.Optional, Description = "Used as /{FileName} when Item path is empty.")]
    public string? FileName { get; set; }

    [ActivityInput(Key = nameof(Content), DisplayName = "Content", Tier = InputTier.Essential, Description = "File bytes. Uploads up to 4 MB with PUT /content; larger files use createUploadSession. Chunk PUTs do not send Authorization.")]
    public byte[]? Content { get; set; }

    [ActivityInput(Key = nameof(ConflictBehavior), DisplayName = "Conflict behavior", Tier = InputTier.Optional, Description = "rename, replace, or fail. Default rename. Used for upload sessions.")]
    public string? ConflictBehavior { get; set; }

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN when Drive id is empty. Default /me/drive. Requires Files.ReadWrite (or Sites.ReadWrite.All for SharePoint drives).")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<GraphDriveItem>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Content is null)
        {
            return ActivityTransition.Fault<GraphDriveItem>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Upload File requires Content bytes."));
        }

        var path = ResolveItemPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return ActivityTransition.Fault<GraphDriveItem>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Upload File requires Item path or File name."));
        }

        try
        {
            var itemPath = Microsoft365Paths.DriveItemByPath(DriveId, UserId, path);
            var root = await CreateClient()
                .UploadItemAsync(
                    itemPath,
                    Content,
                    ConflictBehavior,
                    Connection,
                    context,
                    context.CancellationToken,
                    string.IsNullOrWhiteSpace(DriveId) ? Microsoft365Scopes.FilesReadWrite : Microsoft365Scopes.SitesReadWriteAll)
                .ConfigureAwait(false);

            var item = GraphPayloads.ReadDriveItem(root);
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                item = item with { Name = Microsoft365Paths.FileNameFromPath(path) };
            }

            return ActivityTransition.Complete(item);
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }

    private string? ResolveItemPath()
    {
        if (!string.IsNullOrWhiteSpace(ItemPath))
            return ItemPath.Trim();
        return string.IsNullOrWhiteSpace(FileName) ? null : FileName.Trim();
    }
}
