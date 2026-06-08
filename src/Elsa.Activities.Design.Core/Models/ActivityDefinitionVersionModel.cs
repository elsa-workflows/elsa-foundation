using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// A lightweight, persistence-agnostic <see cref="IActivityDefinitionVersion"/> produced by
/// <see cref="IActivityDefinitionVersionFactory"/> (with <c>Id</c> and <c>Hash</c> already set). The
/// persistence layer converts it to its concrete entity via that entity's <c>From</c> method.
/// </summary>
public sealed record ActivityDefinitionVersionModel(
    string Id,
    string Version,
    string DefinitionId,
    string DescriptorType,
    JsonElement DescriptorPayload,
    string SourceKind,
    string SourceId,
    IActivityDefinition Definition,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    ActivityExecutionType ExecutionType,
    string Hash
) : IActivityDefinitionVersion;
