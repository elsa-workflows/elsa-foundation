namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Unavailable capability/setup differs from failure of the owning preparation infrastructure.</summary>
public enum RuntimeTestContextFindingKind
{
    Unavailable,
    InfrastructureFailure
}

/// <summary>Safe preparation diagnostic, without rejected values or provider exception data.</summary>
public sealed record RuntimeTestContextFinding(string Code, string Message, RuntimeTestContextFindingKind Kind,
    string? ScopeId = null, string? MemberKey = null);

/// <summary>Expected setup/model refusal with safe diagnostics; provider exception data belongs only in InnerException.</summary>
public sealed class RuntimeTestContextUnavailableException(string code, string message, string? scopeId = null,
    string? memberKey = null, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
    public string? ScopeId { get; } = scopeId;
    public string? MemberKey { get; } = memberKey;
}

/// <summary>Immutable prepared data or safe findings. Neither proves admission or creates runtime state.</summary>
public sealed class RuntimeTestContextPreparation
{
    private RuntimeTestContextPreparation(RuntimeTestContextState? context, VariableFrameState? root,
        IReadOnlyList<RuntimeTestContextFinding> findings)
    {
        Context = context;
        RootVariableFrame = root;
        Findings = Array.AsReadOnly(findings.ToArray());
    }

    public bool IsAvailable => Context is not null;
    public RuntimeTestContextState? Context { get; }
    public VariableFrameState? RootVariableFrame { get; }
    public IReadOnlyList<RuntimeTestContextFinding> Findings { get; }

    public static RuntimeTestContextPreparation Available(RuntimeTestContextState context, VariableFrameState root)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);
        return new(context, root, []);
    }

    public static RuntimeTestContextPreparation Unavailable(IReadOnlyList<RuntimeTestContextFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0) throw new ArgumentException("Unavailable preparation requires a finding.", nameof(findings));
        return new(null, null, findings);
    }
}
