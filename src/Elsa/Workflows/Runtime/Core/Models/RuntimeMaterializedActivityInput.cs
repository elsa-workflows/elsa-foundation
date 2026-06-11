using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeMaterializedActivityInput(
    string Name,
    InputArgument Argument,
    object? Value);
