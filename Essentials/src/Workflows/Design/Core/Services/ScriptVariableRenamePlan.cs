namespace Elsa.Workflows.Design.Core.Services;

/// <summary>The planned script updates of one variable rename.</summary>
public sealed record ScriptVariableRenamePlan(
    string OldName,
    string NewName,
    IReadOnlyList<ScriptVariableRenameEdit> Edits,
    IReadOnlyList<ScriptVariableRenameFlag> Flags);

/// <summary>A script input whose source the rename updates: replace <see cref="OldSource"/> with <see cref="NewSource"/>.</summary>
public sealed record ScriptVariableRenameEdit(string NodeId, string InputKey, string OldSource, string NewSource);

/// <summary>A reference the rename cannot safely update, surfaced for manual repair.</summary>
public sealed record ScriptVariableRenameFlag(string NodeId, string InputKey, ScriptVariableRenameFlagReason Reason, string Message);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ScriptVariableRenameFlagReason>))]
public enum ScriptVariableRenameFlagReason
{
    /// <summary>The language gives no source positions (Liquid).</summary>
    NoSourcePositions,

    /// <summary>A <c>variables.X</c> read whose new name is not a JavaScript identifier.</summary>
    NameNotAnIdentifier,

    /// <summary>A computed read whose candidates include the renamed variable.</summary>
    ComputedRead,

    /// <summary>A read of the new name that the renamed variable would capture.</summary>
    Captured,

    /// <summary>A read of the renamed variable that, under the new name, a nearer declaration would capture.</summary>
    Retargeted
}
