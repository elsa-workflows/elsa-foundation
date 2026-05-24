using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record GetDefinition(string Id) : IRequest<ActivityDefinitionDetailsView>;
