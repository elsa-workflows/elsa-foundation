using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record GetVersion(string VersionId) : IRequest<WorkflowDefinitionVersionDetailsView>;
