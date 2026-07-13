using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record ListPublicationSlots(string DefinitionId);

public sealed record GetPublicationSlot(string DefinitionId, string SlotName);

public sealed record GetWorkflowPublicationPolicy(string DefinitionId);

public sealed record SetWorkflowPublicationPolicy(
    string DefinitionId,
    PublicationPolicyDefaultActionView DefaultAction,
    string DefaultSlotName = "default",
    long ExpectedRevision = 0);

public sealed record PreflightWorkflowPublication(
    string VersionId,
    PublicationActionView? Action = null,
    string? SlotName = null,
    string? ExpectedPublicationId = null);

public sealed record PublishWorkflowRequest(
    string VersionId,
    PublicationActionView? Action = null,
    string? SlotName = null,
    string? ExpectedPublicationId = null);

public sealed record UnpublishPublicationSlotRequest(string DefinitionId, string SlotName);

public sealed record RestorePublicationSlotRequest(string DefinitionId, string SlotName);
