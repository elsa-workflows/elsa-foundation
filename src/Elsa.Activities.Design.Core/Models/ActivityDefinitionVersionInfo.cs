namespace Elsa.Activities.Design.Core.Models;

public sealed record ActivityDefinitionVersionInfo(string Id, string Version, DateTimeOffset CreatedAt, ActivityExecutionType ExecutionType);
