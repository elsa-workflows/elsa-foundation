using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>One Elsa method on a captured stack: its name, whether it only carries commands, and its file and line.</summary>
internal readonly record struct ElsaFrame(string Name, bool IsInfrastructure, string Location);

/// <summary>
/// The Elsa frames behind one command, innermost first, and how they were obtained:
/// <list type="bullet">
/// <item><c>stack</c>: the stack the command was issued on.</item>
/// <item><c>save-changes</c>: the stack <c>SaveChanges</c> was entered on, for a command EF sent inside it after an
/// asynchronous hop, such as a batched write after the savepoint round trip.</item>
/// <item><c>stitched</c>: a stack that lost its callers in an asynchronous hop, completed with the latest whole stack of
/// the same asynchronous flow that ran through its outermost frame. <see cref="Seam"/> is where the two meet.</item>
/// <item><c>none</c>: no Elsa caller could be found.</item>
/// </list>
/// </summary>
internal sealed partial record CommandCostAttribution(IReadOnlyList<ElsaFrame> Frames, string Source, int Seam = -1)
{
    private const int ShownFrames = 14;

    /// <summary>
    /// Frames that only carry a command to the database: EF stores and their helpers, store decorators, the coalescing
    /// store overlays and paging extensions. The caller of a command is the first frame past them. The EF checkpoint
    /// participants are the exception: inside a commit they decide which rows are read and written, so they own those
    /// statements rather than the lease wrapper that happens to sit above them.
    /// </summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "Elsa.Persistence.EntityFramework",
        "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore"
    ];

    private static readonly string[] InfrastructureSuffixes = ["Store", "Decorator", "Boundary", "Extensions"];

    /// <summary>The first Elsa frame on the stack: the store or boundary that issued the command.</summary>
    public string Store => Frames.Count > 0 ? Frames[0].Name : "?";

    /// <summary>The first Elsa frame that is not persistence or coalescing infrastructure.</summary>
    public string Caller => CallerFrame?.Name ?? "?";

    public string CallerLocation => CallerFrame?.Location ?? "-";

    public bool HasCaller => CallerFrame is not null;

    /// <summary>The frames, innermost first, with <c>&lt;~</c> where a stitched stack's two halves meet.</summary>
    public string Text => string.Join(string.Empty, Frames.Take(ShownFrames).Select((frame, index) =>
        index == 0 ? frame.Name : (index == Seam ? " <~ " : " < ") + frame.Name));

    private ElsaFrame? CallerFrame => Frames.FirstOrDefault(frame => !frame.IsInfrastructure) is { Name: not null } frame ? frame : null;

    /// <summary>The Elsa frames of <paramref name="trace"/>, with consecutive frames of one method merged.</summary>
    public static CommandCostAttribution Capture(StackTrace trace)
    {
        var frames = new List<ElsaFrame>();
        foreach (var frame in trace.GetFrames())
        {
            if (frame.GetMethod() is not { DeclaringType: { } type } method)
                continue;
            var owner = Owner(type);
            if (owner.FullName is not { } fullName || !fullName.StartsWith("Elsa.", StringComparison.Ordinal) ||
                fullName.StartsWith("Elsa.Activities.Http.CommandCost.", StringComparison.Ordinal))
                continue;

            var name = $"{owner.Name}.{LogicalMethodName(type, method)}";
            if (frames.Count == 0 || frames[^1].Name != name)
                frames.Add(new ElsaFrame(name, IsInfrastructure(owner), Location(frame)));
        }

        var attribution = new CommandCostAttribution(frames, "stack");
        return attribution.HasCaller ? attribution : attribution with { Source = "none" };
    }

    /// <summary>
    /// This stack, which reaches no caller, completed by <paramref name="whole"/>: the frames of <paramref name="whole"/>
    /// outward of this stack's outermost frame. Null when <paramref name="whole"/> never ran through that frame.
    /// </summary>
    public CommandCostAttribution? StitchOnto(CommandCostAttribution whole)
    {
        if (Frames.Count == 0)
            return null;
        var outermost = Frames[^1].Name;
        for (var index = 0; index < whole.Frames.Count; index++)
        {
            if (whole.Frames[index].Name != outermost)
                continue;
            var stitched = new CommandCostAttribution([.. Frames, .. whole.Frames.Skip(index + 1)], "stitched", Frames.Count);
            return stitched.HasCaller ? stitched : null;
        }

        return null;
    }

    private static Type Owner(Type type)
    {
        while (type.DeclaringType is { } declaring && (type.Name.StartsWith('<') || type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
            type = declaring;
        return type;
    }

    private static string LogicalMethodName(Type type, MethodBase method)
    {
        var generated = string.Join(' ', GeneratedNames(type).Append(method.Name));
        var outer = OuterMethod().Match(generated);
        if (!outer.Success)
            return method.Name;
        var local = LocalFunction().Match(generated);
        return local.Success ? $"{outer.Groups["name"].Value}.{local.Groups["name"].Value}" : outer.Groups["name"].Value;
    }

    private static IEnumerable<string> GeneratedNames(Type type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null && current.Name.StartsWith('<'); current = current.DeclaringType)
            names.Push(current.Name);
        return names;
    }

    private static bool IsInfrastructure(Type owner) =>
        !(owner.Name.StartsWith("EfRuntimeCheckpoint", StringComparison.Ordinal) && owner.Name != "EfRuntimeCheckpointParticipantRows") &&
        (InfrastructureNamespaces.Any(prefix => owner.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
         owner.Name.StartsWith("Coalescing", StringComparison.Ordinal) ||
         InfrastructureSuffixes.Any(suffix => owner.Name.EndsWith(suffix, StringComparison.Ordinal)));

    private static string Location(StackFrame frame)
    {
        if (frame.GetFileName() is not { } file)
            return "-";
        var relative = file.IndexOf("/Essentials/", StringComparison.Ordinal);
        return $"{(relative >= 0 ? file[(relative + 1)..] : Path.GetFileName(file))}:{frame.GetFileLineNumber()}";
    }

    [GeneratedRegex("<(?<name>[A-Za-z_][A-Za-z0-9_]*)>")]
    private static partial Regex OuterMethod();

    [GeneratedRegex(@"g__(?<name>[A-Za-z_][A-Za-z0-9_]*)\|")]
    private static partial Regex LocalFunction();
}

/// <summary>
/// The whole stacks one asynchronous flow has issued commands from, newest last. A store method that awaits real I/O
/// resumes on a thread-pool stack that holds the store but not its caller; its earlier command, issued before the hop,
/// carried the whole stack, and that is the one a later command of the same flow is stitched onto. Flows that run
/// concurrently and share one memory could stitch onto each other's stacks; the runtime drains one execution at a time,
/// so within one measured run they do not.
/// </summary>
internal sealed class CommandCostFlowMemory
{
    private const int Capacity = 256;
    private readonly List<CommandCostAttribution> _whole = [];
    private readonly Lock _gate = new();

    public void Remember(CommandCostAttribution whole)
    {
        lock (_gate)
        {
            if (_whole.Count == Capacity)
                _whole.RemoveAt(0);
            _whole.Add(whole);
        }
    }

    public CommandCostAttribution? Stitch(CommandCostAttribution truncated)
    {
        lock (_gate)
        {
            for (var index = _whole.Count - 1; index >= 0; index--)
                if (truncated.StitchOnto(_whole[index]) is { } stitched)
                    return stitched;
        }

        return null;
    }
}
