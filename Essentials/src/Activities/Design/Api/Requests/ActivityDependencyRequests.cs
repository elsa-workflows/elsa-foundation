using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record GetActivityDependencies(
    string VersionId,
    string Direction = "outbound",
    bool Transitive = false,
    string Include = "versions",
    string? Cursor = null,
    int? Limit = null) : IRequest<ActivityDependencyPageView>;
