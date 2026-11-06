using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Core.Resources;

public sealed record DashboardActor(string TenantId, string UserId, IReadOnlySet<string> RoleIds, IReadOnlySet<string> Permissions)
{
    /// <summary>Trusted normalized authentication subject for correlation with an interactive Agent session, never a user-grant ID.</summary>
    public string? AuthenticationSubjectId { get; init; }
}

public static class DashboardResourceKinds
{
    public const string Question = "question";
    public const string Dashboard = "dashboard";
}

public sealed record DashboardAudienceGrant(string Kind, string Id, string Access);
public sealed record DashboardResourceCapabilities(bool CanRead, bool CanEdit, bool CanShare, bool CanDelete, bool CanCopy);

public sealed record DashboardTileInput
{
    public string Id { get; init; } = "";
    public string? QuestionId { get; init; }
    public string Size { get; init; } = "medium";
    public DashboardVisualization? Presentation { get; init; }
    public IReadOnlyDictionary<string, DashboardValue>? Parameters { get; init; }
    public bool PreserveReference { get; init; }
}

public sealed record DashboardTile(string Id, string QuestionId, string Size,
    DashboardVisualization? Presentation = null, IReadOnlyDictionary<string, DashboardValue>? Parameters = null);

public sealed record DashboardTileView(string Id, string? QuestionId, string Size, string ReferenceState,
    DashboardVisualization? Presentation = null, IReadOnlyDictionary<string, DashboardValue>? Parameters = null);

public sealed record DashboardResourceContent(string Name, string? Description, DashboardQueryRequest? Definition,
    IReadOnlyList<DashboardTile> Tiles);

public sealed record DashboardResourceRevision(string Revision, DashboardResourceContent Content, string ActorId, DateTimeOffset UpdatedAt);

public sealed record DashboardResourceDocument(string Id, string Kind, string TenantId, string OwnerId,
    string Revision, DashboardResourceContent Content, IReadOnlyList<DashboardAudienceGrant> Audience,
    IReadOnlyList<DashboardResourceRevision> History, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public string? UpdatedBy { get; init; }
}

public sealed record DashboardResourceView(string Id, string Kind, string OwnerId, string Revision, string Name,
    string? Description, DashboardResourceCapabilities Capabilities, DashboardQueryRequest? Definition,
    string DefinitionState, IReadOnlyList<DashboardTileView> Tiles, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>Current readable dashboard uses of this question; omitted for collection items and other resources. This is not an inventory of inaccessible uses.</summary>
    public IReadOnlyList<DashboardQuestionUse>? Uses { get; init; }
}

public sealed record DashboardQuestionUse(string Id, string Name, int TileCount);

public sealed record DashboardResourcePage(IReadOnlyList<DashboardResourceView> Items, string? ContinuationToken = null);
public sealed record DashboardAudiencePage(IReadOnlyList<DashboardAudienceGrant> Items);
public sealed record DashboardAudienceCandidate(string Kind, string Id, string Name);
public sealed record DashboardAudienceCandidatePage(IReadOnlyList<DashboardAudienceCandidate> Items, string? ContinuationToken = null);
public sealed record DashboardHistoryItem(string Revision, string Name, string? Description, string ActorId, DateTimeOffset UpdatedAt);
public sealed record DashboardHistoryPage(IReadOnlyList<DashboardHistoryItem> Items);

public sealed record DashboardResourceInput
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? OperationKey { get; init; }
    public DashboardQueryRequest? Definition { get; init; }
    public IReadOnlyList<DashboardTileInput> Tiles { get; init; } = [];
    public bool PreserveDefinition { get; init; }
}

public sealed record DashboardCopyInput(string OperationKey, string Name, string? Description = null);
public sealed record DashboardRestoreInput(string Revision);

public sealed class DashboardResourceException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed record DashboardResourceWrite(DashboardResourceDocument Document, string? ExpectedRevision,
    string? OperationKey = null, string? OperationFingerprint = null, bool Delete = false);

public sealed record DashboardResourceWriteResult(string Status, DashboardResourceDocument? Document = null);
