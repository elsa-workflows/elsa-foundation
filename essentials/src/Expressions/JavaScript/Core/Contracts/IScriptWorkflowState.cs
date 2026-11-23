using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>Bounded JSON and identity capability passed only to a script body.</summary>
public interface IScriptWorkflowState
{
    bool TryGetOutput(string name, out JsonElement value);
    void SetOutput(string name, JsonElement value);
    string? CorrelationId { get; set; }
    string? InstanceName { get; set; }
    bool HasReadProtectedValue { get; }

    /// <summary>Latches a value-free host refusal, even when user code catches its exception.</summary>
    void Refuse(string code, string reason);

    /// <summary>Checks the latched refusal after evaluation and serialization.</summary>
    void ThrowIfRefused();
}
