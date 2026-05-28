using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using System.Linq.Expressions;

namespace Elsa.Activities.Design.Api.Constants;

internal static class Expressions
{
    public static readonly Expression<Func<ActivityDefinitionVersion, ActivityDefinitionVersionInfo>> VersionInfoSelector = (e) => new(e.Id, e.Version, e.CreatedAt, e.ExecutionType);
}
