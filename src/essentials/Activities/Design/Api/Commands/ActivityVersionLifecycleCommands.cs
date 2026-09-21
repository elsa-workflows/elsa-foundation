using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record RetireReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason,
    ActivityRecommendationDecision? RecommendationDecision = null) : ICommand<ReusableActivityVersionLifecycleView>;

public sealed record RestoreReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason) : ICommand<ReusableActivityVersionLifecycleView>;

public sealed record RevokeReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason,
    ActivityRecommendationDecision? RecommendationDecision = null) : ICommand<ReusableActivityVersionLifecycleView>;
