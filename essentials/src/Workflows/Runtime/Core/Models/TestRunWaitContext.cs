using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record TestRunWaitContext(WorkflowExecutable Executable, ExecutableNode Node, BookmarkState Wait);
public sealed record TestRunWaitPrepareContext(TestRunWaitContext Wait, string ModeId, JsonElement? Data);
