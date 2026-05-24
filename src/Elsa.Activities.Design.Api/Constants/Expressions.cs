using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Activities.Design.Api.Constants;

internal static class Expressions
{
    public static readonly Expression<Func<ActivityDefinitionVersion, ActivityDefinitionVersionView>> VersionViewSelector = (e) => new(e.Id, e.Version, e.Kind, e.CreatedAt);
}
