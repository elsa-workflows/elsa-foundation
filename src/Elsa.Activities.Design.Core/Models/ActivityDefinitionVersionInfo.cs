namespace Elsa.Activities.Design.Core.Models;

public sealed record ActivityDefinitionVersionInfo(string Id, int Version, DateTimeOffset CreatedAt, ActivityExecutionType ExecutionType);
