using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ActivityConnection(ActivityPortConnection Source, ActivityPortConnection Target) 
    : IActivityConnection;
