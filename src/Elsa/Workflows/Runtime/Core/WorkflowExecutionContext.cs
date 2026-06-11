using System.Globalization;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core;

public sealed class WorkflowExecutionContext : IWorkflowExecutionContext
{
    public const string DefinitionVersionMetadataKey = "runtime.definitionVersion";

    private readonly WorkflowExecutionState _state;
    private readonly IDictionary<string, InputArgument> _workflowInputs;
    private readonly Dictionary<string, RuntimeVariable> _variables;
    private readonly Dictionary<string, WorkflowExecutionContextActivityOutput> _outputsByActivityExecutionAndName;
    private readonly Dictionary<string, string> _activityExecutionIdByName;
    private readonly Dictionary<IExpressionExecutionContext, IActivityExecutionContext> _activityContexts;
    private object? _lastActivityResult;

    public WorkflowExecutionContext(
        WorkflowExecutionState state,
        IDictionary<string, InputArgument>? workflowInputs = null,
        IReadOnlyDictionary<string, object?>? workflowInputValues = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        IReadOnlyCollection<WorkflowExecutionContextActivityOutput>? activityOutputs = null,
        IReadOnlyDictionary<IExpressionExecutionContext, IActivityExecutionContext>? activityContexts = null,
        string? name = null,
        object? lastActivityResult = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        _workflowInputs = new Dictionary<string, InputArgument>(workflowInputs ?? new Dictionary<string, InputArgument>(), StringComparer.Ordinal);
        Memory = new RuntimeMemoryRegister();
        _variables = new Dictionary<string, RuntimeVariable>(StringComparer.Ordinal);
        _outputsByActivityExecutionAndName = new Dictionary<string, WorkflowExecutionContextActivityOutput>(StringComparer.Ordinal);
        _activityExecutionIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _activityContexts = new Dictionary<IExpressionExecutionContext, IActivityExecutionContext>(activityContexts ?? new Dictionary<IExpressionExecutionContext, IActivityExecutionContext>());
        Name = name;
        _lastActivityResult = lastActivityResult;
        CorrelationId = state.CorrelationId;

        SeedWorkflowInputs(workflowInputValues ?? new Dictionary<string, object?>());
        SeedVariables(variables ?? new Dictionary<string, object?>());
        SeedActivityOutputs(activityOutputs ?? []);
    }

    public IMemoryRegister Memory { get; }
    public string WorkflowDefinitionId => _state.PinnedExecutable.DefinitionId;
    public string WorkflowDefinitionVersionId => _state.PinnedExecutable.DefinitionVersionId;
    public int WorkflowDefinitionVersion => ResolveWorkflowDefinitionVersion(_state);
    public string InstanceId => _state.WorkflowExecutionId;
    public string? CorrelationId { get; set; }
    public string? Name { get; set; }

    public IActivityExecutionContext GetActivityContextForExpression(IExpressionExecutionContext expressionContext)
    {
        ArgumentNullException.ThrowIfNull(expressionContext);

        if (_activityContexts.TryGetValue(expressionContext, out var activityContext))
            return activityContext;

        if (expressionContext is IActivityExecutionContext directActivityContext)
            return directActivityContext;

        throw new InvalidOperationException("Expression execution context is not associated with a runtime activity execution context.");
    }

    public object GetInput(string inputName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);

        if (!_workflowInputs.TryGetValue(inputName, out var input))
            throw new InvalidOperationException($"Workflow input '{inputName}' is not available in the runtime workflow execution context.");

        var blockReference = input.MemoryBlockReference();
        if (!Memory.TryGetBlock(blockReference.Id, out var block))
            throw new InvalidOperationException($"Workflow input '{inputName}' has no runtime value.");

        return block.Value!;
    }

    public object? GetLastActivityResult() => _lastActivityResult;

    public object GetOutput(string outputName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        var matches = _outputsByActivityExecutionAndName.Values
            .Where(output => StringComparer.Ordinal.Equals(output.OutputName, outputName))
            .ToArray();

        return matches.Length switch
        {
            0 => throw new InvalidOperationException($"Activity output '{outputName}' is not available in the runtime workflow execution context."),
            1 => matches[0].Value!,
            _ => throw new InvalidOperationException($"Activity output '{outputName}' is ambiguous. Use activity execution ID or runtime activity name.")
        };
    }

    public object GetOutput(string outputName, string activityIdOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityIdOrName);

        if (TryGetOutput(activityIdOrName, outputName, out var output))
            return output.Value!;

        if (TryGetOutput(outputName, activityIdOrName, out output))
            return output.Value!;

        throw new InvalidOperationException($"Activity output '{outputName}' from runtime activity '{activityIdOrName}' is not available in the runtime workflow execution context.");
    }

    public object GetVariable(string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        if (!_variables.TryGetValue(variableName, out var variable))
            throw new InvalidOperationException($"Workflow variable '{variableName}' is not available in the runtime workflow execution context.");

        return Memory.TryGetBlock(variable.Id, out var block) ? block.Value! : variable.DefaultValue!;
    }

    public IEnumerable<IVariable> GetVariables() => _variables.Values.ToArray();

    public IDictionary<string, InputArgument> GetWorkflowInputs() =>
        new Dictionary<string, InputArgument>(_workflowInputs, StringComparer.Ordinal);

    public void SetVariable(string variableName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        if (!_variables.TryGetValue(variableName, out var variable))
        {
            variable = new RuntimeVariable(variableName, value);
            _variables.Add(variableName, variable);
        }

        Memory.Declare(variable).Value = value;
    }

    public void SetLastActivityResult(object? value) => _lastActivityResult = value;

    public void SetActivityOutput(WorkflowExecutionContextActivityOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        _outputsByActivityExecutionAndName[OutputKey(output.ActivityExecutionId, output.OutputName)] = output;
        if (!string.IsNullOrWhiteSpace(output.ActivityName))
            _activityExecutionIdByName[output.ActivityName] = output.ActivityExecutionId;
    }

    private void SeedWorkflowInputs(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var (name, input) in _workflowInputs)
        {
            if (!values.TryGetValue(name, out var value))
                continue;

            Memory.Declare(input.MemoryBlockReference()).Value = value;
        }
    }

    private void SeedVariables(IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var (name, value) in variables)
            SetVariable(name, value);
    }

    private void SeedActivityOutputs(IReadOnlyCollection<WorkflowExecutionContextActivityOutput> outputs)
    {
        foreach (var output in outputs)
            SetActivityOutput(output);
    }

    private bool TryGetOutput(string activityIdOrName, string outputName, out WorkflowExecutionContextActivityOutput output)
    {
        var activityExecutionId = _activityExecutionIdByName.TryGetValue(activityIdOrName, out var resolvedActivityExecutionId)
            ? resolvedActivityExecutionId
            : activityIdOrName;

        return _outputsByActivityExecutionAndName.TryGetValue(OutputKey(activityExecutionId, outputName), out output!);
    }

    private static string OutputKey(string activityExecutionId, string outputName) =>
        $"{activityExecutionId}:{outputName}";

    private static int ResolveWorkflowDefinitionVersion(WorkflowExecutionState state)
    {
        if (state.SystemMetadata.TryGetValue(DefinitionVersionMetadataKey, out var versionText))
            return ParseVersion(versionText, DefinitionVersionMetadataKey);

        var artifactVersion = state.PinnedExecutable.ArtifactVersion;
        var majorVersion = artifactVersion.Split('.', 2)[0];
        return ParseVersion(majorVersion, nameof(state.PinnedExecutable.ArtifactVersion));
    }

    private static int ParseVersion(string versionText, string source)
    {
        if (int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return version;

        throw new InvalidOperationException($"Runtime workflow definition version from '{source}' must be numeric.");
    }

    private sealed class RuntimeMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>(StringComparer.Ordinal);
    }

    private sealed class RuntimeMemoryBlock(object? value = null) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; }
    }

    private sealed class RuntimeVariable(string name, object? defaultValue = null) : IVariable
    {
        public string Id { get; set; } = $"variable:{name}";
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new RuntimeMemoryBlock(DefaultValue);

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context)
        {
            var value = memoryRegister.Declare(this).Value;
            return value is T typed ? typed : default;
        }

        public T? Get<T>(IExpressionExecutionContext context)
        {
            var value = context.Get(this);
            return value is T typed ? typed : default;
        }
    }
}

public sealed record WorkflowExecutionContextActivityOutput
{
    public WorkflowExecutionContextActivityOutput(
        string activityExecutionId,
        string? activityName,
        string outputName,
        object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        ActivityExecutionId = activityExecutionId;
        ActivityName = activityName;
        OutputName = outputName;
        Value = value;
    }

    public string ActivityExecutionId { get; }
    public string? ActivityName { get; }
    public string OutputName { get; }
    public object? Value { get; }
}
