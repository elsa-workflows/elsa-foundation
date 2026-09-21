namespace Elsa.Activities.Design.Core.Models;

public sealed record ActivityDefinitionVersionSummary(string Id, string Version, DateTimeOffset CreatedAt, ActivityExecutionType ExecutionType);
